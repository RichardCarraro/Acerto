namespace Acerto.Core;

public enum CorrectionKind { Shortcut, Fixed, Accent, Typo }

public sealed record Correction(string Replacement, CorrectionKind Kind);

/// <summary>Decide se uma palavra terminada deve ser trocada. Na dúvida, não troca.</summary>
public sealed class Corrector
{
    /// <summary>A melhor forma acentuada precisa ser pelo menos isso vezes mais usada que a segunda.</summary>
    public const double AccentDominance = 3.0;

    /// <summary>
    /// Palavra que aparece pelo menos isso no corpus é escrita de propósito (curitiba, apps, voice, mail): não passa
    /// pela regra de digitação. Erros que muita gente comete (palvra, apra, mutio) também caem aqui; esses foram
    /// minerados do corpus, revisados um a um e estão em correcoes.txt.
    /// Tentativas de liberar automaticamente, todas reprovadas pelo teste de corpus:
    ///  - pela proporção de uso da correção: 286 palavras estragadas (voice→você, mail→mais, save→sabe);
    ///  - só inversão, letra dobrada ou letra faltando: 52, metade estrangeiras (mass→mas, toad→toda, problem→problema).
    /// </summary>
    public const long AttestedCount = 10;

    /// <summary>Palavras menores que isso só são corrigidas em casos bem restritos (ver TypoOptions.ShortWordMinCount).</summary>
    public const int MinTypoLength = 4;

    private readonly CoreData _data;
    private readonly AccentIndex _accents;
    private readonly TypoRanker _typos;

    public Corrector(CoreData data, TypoOptions? typoOptions = null)
    {
        _data = data;
        _accents = new AccentIndex(data.Frequency, data.Lexicon);
        _typos = new TypoRanker(data.Lexicon, data.Frequency, typoOptions, data.Bigrams, data.History);
        Suggester = new Suggester(_typos, data);
    }

    /// <summary>
    /// Campo onde se digita nome de site e busca (barra de endereço): usa o perfil cuidadoso mesmo com "corrigir mais",
    /// para "reddit", "docs" e "shopee" não virarem "redor", "dos" e "shop".
    /// </summary>
    public bool CarefulField { get; set; }

    /// <summary>Contar no histórico o que o usuário escreve (desligado nas configurações = não conta nada).</summary>
    public bool LearnFromTyping { get; set; } = true;

    /// <summary>
    /// A palavra ficou no texto (digitada, corrigida ou sugestão aceita): conta no histórico. Só palavras do dicionário
    /// ou das "Minhas palavras", para não guardar senha, código, e-mail ou número de documento.
    /// </summary>
    /// <param name="prev">Palavra anterior em minúsculas, <see cref="BigramModel.SentenceStart"/> ou null.</param>
    public void Record(string? prev, string word)
    {
        if (!LearnFromTyping || word.Length == 0) return;
        var w = word.ToLower(TextUtil.PtBr);
        if (!_data.Personal.Contains(w) && !(_data.Lexicon.IsValid(w) && w.All(c => char.IsLetter(c) || c == '-'))) return;
        _data.History.Record(prev, w);
    }

    /// <summary>Sugestões de completar palavra e de próxima palavra.</summary>
    public Suggester Suggester { get; }

    /// <summary>Candidatas do ranqueador de digitação, para diagnóstico (tests/Acerto.Eval).</summary>
    public TypoRanker Typos => _typos;

    /// <param name="word">Só letras e hífen, sem pontuação.</param>
    /// <param name="atSentenceStart">A palavra começa uma frase: maiúscula ali é só maiúscula de início de frase.
    /// No meio da frase, palavra com maiúscula é tratada como nome e não passa pela regra de digitação.</param>
    /// <param name="prev">Palavra anterior em minúsculas, <see cref="BigramModel.SentenceStart"/> no começo da frase,
    /// ou null se não se sabe. Com ela a escolha leva o contexto em conta ("minha cas" → casa, "esse cas" → caso).</param>
    public Correction? Correct(string word, bool atSentenceStart = false, string? prev = null)
    {
        if (word.Length == 0) return null;
        bool looksLikeName = char.IsUpper(word[0]) && !atSentenceStart;
        var w = word.ToLowerInvariant();
        var o = CarefulField ? TypoOptions.Careful : _typos.Options;

        // 1. Atalho do usuário
        if (_data.Shortcuts.TryGet(w, out var expansion))
        {
            var exp = char.IsUpper(word[0]) && expansion.Length > 0
                ? char.ToUpper(expansion[0], TextUtil.PtBr) + expansion[1..]
                : expansion;
            return exp == word ? null : new Correction(exp, CorrectionKind.Shortcut);
        }

        // 2. Palavra ensinada pelo usuário
        if (_data.Personal.Contains(w)) return null;

        // 2b. Locução com a anterior, da lista à mão: "isso ai" → aí, "a viajem" → viagem
        if (prev is not null && _data.Pairs.TryGet(prev + " " + w, out var pairRep) && pairRep.Split(' ') is [_, var second] && second != w)
            return new Correction(TextUtil.ApplyCase(word, second), CorrectionKind.Fixed);

        // 3. Lista embutida (inclui as formas válidas-mas-raras como agua→água, escolhidas à mão)
        if (_data.Fixed.TryGet(w, out var fixedRep))
        {
            // ...a não ser que a anterior peça outra vizinha com muita força e nunca venha antes da forma da lista:
            // "bom dai" → dia, não daí. Começo de frase não conta ("pao" viraria "Não").
            if (prev is not null && prev != BigramModel.SentenceStart && o.FixedContextMinPair > 0
                && _data.Bigrams.Count(prev, fixedRep) == 0 && _typos.Best(w, prev, o) is { } near && near.Word != fixedRep
                && _data.Bigrams.Count(prev, near.Word) >= o.FixedContextMinPair)
                return new Correction(TextUtil.ApplyCase(word, near.Word), CorrectionKind.Typo);
            return new Correction(TextUtil.ApplyCase(word, fixedRep), CorrectionKind.Fixed);
        }

        // 4. Caixa mista ("iPhone") e hífen só passam pelas regras acima
        if (TextUtil.IsMixedCase(word) || w.Contains('-')) return null;

        // formas com acento da mesma palavra, da mais provável (neste contexto) para a menos
        var accented = _accents.Lookup(TextUtil.StripAccents(w)).Where(c => c.Word != w)
            .Select(c => (c.Word, c.Count, Effective: _typos.EffectiveCount(c.Word, c.Count, prev)))
            .OrderByDescending(c => c.Effective).ToList();

        // 5. Palavra que o dicionário aceita não é trocada (ficara/ficará, duvida/dúvida, capita/capitã). Exceções:
        if (_data.Lexicon.IsValid(word))
        {
            if (looksLikeName || TextUtil.IsAllCaps(word)) return null;

            // 5a. O contexto mostra que faltou o acento: "ele esta" → "ele está". Só com evidência forte do par.
            if (prev is not null && o.ContextAccentRatio > 0 && accented.Count > 0)
            {
                var best = accented[0];
                double typedEffective = _typos.EffectiveCount(w, _data.Frequency.Count(w), prev);
                int pair = _data.Bigrams.Count(prev, best.Word);
                if ((pair >= o.ContextMinPairCount && best.Effective >= o.ContextAccentRatio * Math.Max(typedEffective, 1))
                    || (o.ContextPairRatio > 0 && pair >= o.ContextPairMinCount
                        && pair >= o.ContextPairRatio * Math.Max(_data.Bigrams.Count(prev, w), 1)))
                    return new Correction(TextUtil.ApplyCase(word, best.Word), CorrectionKind.Accent);
            }

            // 5b. Ela quase nunca aparece no corpus e está a um erro de uma palavra muito usada (erado → errado).
            //     Tentado em 2026-11-16 e reprovado: trocar palavra rara pela vizinha que a anterior pede ("tudo cetro"
            //     → certo) estragou +75 em 416 mil palavras certas (de menus → menos, moradoras → moradores).
            if (_data.Frequency.Count(w) >= AttestedCount || w.Length < MinTypoLength || _data.Protected.Contains(w)) return null;
            return _typos.Best(w, prev, o) is { } rare && rare.Count >= o.RareValidMinCandidate
                ? new Correction(TextUtil.ApplyCase(word, rare.Word), CorrectionKind.Typo)
                : null;
        }

        // 6. Erro de digitação: só minúsculas (maiúscula no meio da frase é nome; sigla fica como está)
        TypoMatch? typo = null;
        if (!TextUtil.IsAllCaps(word) && !looksLikeName && w.Length >= 3 && !_data.Protected.Contains(w) && _typos.Best(w, prev, o) is { } t)
        {
            long typedCount = _data.Frequency.Count(w);
            bool shortOk = w.Length >= MinTypoLength
                || ((t.Kind is TypoKind.MissingLetter || (t.Kind is TypoKind.Transposition && o.ShortWordTransposition))
                    && t.Count >= o.ShortWordMinCount);
            bool slip = t.Kind is TypoKind.Transposition or TypoKind.DoubledLetter or TypoKind.MissingLetter;
            long ratio = slip ? o.AttestedSlipRatio : t.Kind is TypoKind.Phonetic ? o.AttestedPhoneticRatio : o.AttestedOtherRatio;
            bool attestedOk = typedCount < AttestedCount
                || (ratio > 0 && t.Count >= ratio * typedCount);
            if (shortOk && attestedOk) typo = t;
        }

        // 7. Acento esquecido: a acentuada tem que ganhar com folga da segunda e da própria forma digitada
        //    ("ana", nome, é muito mais escrita que "anã"). Ganha do erro de digitação, a não ser que este seja
        //    uma explicação muito melhor (vaos → vamos, não o raro "vãos").
        //    Abaixo do mínimo de uso, a acentuada ainda ganha se for pelo menos tão usada quanto a candidata de
        //    digitação, que está um erro mais longe (almocaram → almoçaram, não almoçavam).
        if (accented.Count > 0
            && (accented[0].Count >= o.AccentMinCount || (typo is { } weaker && accented[0].Count >= weaker.Count))
            && (accented.Count == 1 || accented[0].Effective >= AccentDominance * accented[1].Effective)
            && accented[0].Count >= AccentDominance * _data.Frequency.Count(w))
        {
            var accent = accented[0];
            if (typo is { } better && better.Word != accent.Word
                && better.Score - Math.Log(Math.Max(accent.Effective, 0.01)) >= o.AccentYield)
                return new Correction(TextUtil.ApplyCase(word, better.Word), CorrectionKind.Typo);
            return new Correction(TextUtil.ApplyCase(word, accent.Word), CorrectionKind.Accent);
        }

        return typo is { } m ? new Correction(TextUtil.ApplyCase(word, m.Word), CorrectionKind.Typo) : null;
    }

    /// <summary>
    /// Segunda olhada numa palavra que ficou como estava, agora que a seguinte terminou: "isso e muito" → "é",
    /// "o almoço esta pronto" → "está". Só troca de acento, só palavra sem acento e só com o par (acentuada,
    /// seguinte) visto no corpus. Compara P(x | anterior) × P(seguinte | x) entre a digitada e a acentuada.
    /// </summary>
    /// <param name="word">A palavra como ficou no texto (sem pontuação).</param>
    /// <param name="prev">Palavra antes dela, como em <see cref="Correct"/>. Sem ela (depois de vírgula) não troca:
    /// ali "e" é quase sempre a conjunção.</param>
    /// <param name="next">Palavra seguinte, em minúsculas, como ficou no texto.</param>
    public Correction? CorrectByNext(string word, string? prev, string next)
    {
        // locução da lista à mão: "a vontade" → à vontade, "as vezes" → às vezes. Com a anterior na chave ("fica a
        // vontade") a entrada vale só nesse caso, e "todas as vezes" → "todas as vezes" serve de exceção à de duas.
        var lower = word.ToLower(TextUtil.PtBr);
        if (!TextUtil.IsMixedCase(word) && !_data.Personal.Contains(lower))
        {
            string? rep = null;
            if (prev is not null && _data.Pairs.TryGet(prev + " " + lower + " " + next, out var three)) rep = three.Split(' ')[1];
            else if (_data.Pairs.TryGet(lower + " " + next, out var two)) rep = two.Split(' ')[0];
            if (rep is not null) return rep == lower ? null : new Correction(TextUtil.ApplyCase(word, rep), CorrectionKind.Fixed);
        }

        var o = CarefulField ? TypoOptions.Careful : _typos.Options;
        if (o.NextAccentRatio <= 0 || prev is null || word.Length == 0 || TextUtil.IsMixedCase(word) || (TextUtil.IsAllCaps(word) && word.Length > 1)) return null;
        var w = word.ToLower(TextUtil.PtBr);
        if (TextUtil.StripAccents(w) != w || !_data.Lexicon.IsValid(w) || _data.Personal.Contains(w)) return null;

        double Score(string x) =>
            _typos.EffectiveCount(x, _data.Frequency.Count(x), prev)
            * (_data.Bigrams.Count(x, next) + 0.5) / Math.Max(_data.Bigrams.ContextTotal(x), 1);

        var best = _accents.Lookup(w).Where(c => c.Word != w).Select(c => c.Word)
            .Where(x => _data.Bigrams.Count(x, next) >= o.NextMinPairCount)
            .Select(x => (Word: x, Score: Score(x))).OrderByDescending(x => x.Score).FirstOrDefault();
        return best.Word is not null && best.Score >= o.NextAccentRatio * Score(w)
            ? new Correction(TextUtil.ApplyCase(word, best.Word), CorrectionKind.Accent)
            : null;
    }
}

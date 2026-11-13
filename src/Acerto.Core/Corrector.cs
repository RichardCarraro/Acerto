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
        var o = _typos.Options;

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

        // 3. Lista embutida (inclui as formas válidas-mas-raras como agua→água, escolhidas à mão)
        if (_data.Fixed.TryGet(w, out var fixedRep))
            return new Correction(TextUtil.ApplyCase(word, fixedRep), CorrectionKind.Fixed);

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
                if (_data.Bigrams.Count(prev, best.Word) >= o.ContextMinPairCount
                    && best.Effective >= o.ContextAccentRatio * Math.Max(typedEffective, 1))
                    return new Correction(TextUtil.ApplyCase(word, best.Word), CorrectionKind.Accent);
            }

            // 5b. Ela quase nunca aparece no corpus e está a um erro de uma palavra muito usada (erado → errado).
            if (_data.Frequency.Count(w) >= AttestedCount || w.Length < MinTypoLength) return null;
            return _typos.Best(w, prev) is { } rare && rare.Count >= o.RareValidMinCandidate
                ? new Correction(TextUtil.ApplyCase(word, rare.Word), CorrectionKind.Typo)
                : null;
        }

        // 6. Erro de digitação: só minúsculas (maiúscula no meio da frase é nome; sigla fica como está)
        TypoMatch? typo = null;
        if (!TextUtil.IsAllCaps(word) && !looksLikeName && w.Length >= 3 && _typos.Best(w, prev) is { } t)
        {
            long typedCount = _data.Frequency.Count(w);
            bool shortOk = w.Length >= MinTypoLength
                || ((t.Kind is TypoKind.MissingLetter || (t.Kind is TypoKind.Transposition && o.ShortWordTransposition))
                    && t.Count >= o.ShortWordMinCount);
            bool slip = t.Kind is TypoKind.Transposition or TypoKind.DoubledLetter or TypoKind.MissingLetter;
            long ratio = slip ? o.AttestedSlipRatio : o.AttestedOtherRatio;
            bool attestedOk = typedCount < AttestedCount
                || (ratio > 0 && t.Count >= ratio * typedCount && !_data.Protected.Contains(w));
            if (shortOk && attestedOk) typo = t;
        }

        // 7. Acento esquecido: a acentuada tem que ganhar com folga da segunda e da própria forma digitada
        //    ("ana", nome, é muito mais escrita que "anã"). Ganha do erro de digitação, a não ser que este seja
        //    uma explicação muito melhor (vaos → vamos, não o raro "vãos").
        if (accented.Count > 0
            && accented[0].Count >= o.AccentMinCount
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
}

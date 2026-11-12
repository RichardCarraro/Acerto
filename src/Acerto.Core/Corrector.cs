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
        _typos = new TypoRanker(data.Lexicon, data.Frequency, typoOptions);
    }

    /// <summary>Candidatas do ranqueador de digitação, para diagnóstico (tests/Acerto.Eval).</summary>
    public TypoRanker Typos => _typos;

    /// <param name="word">Só letras e hífen, sem pontuação.</param>
    /// <param name="atSentenceStart">A palavra começa uma frase: maiúscula ali é só maiúscula de início de frase.
    /// No meio da frase, palavra com maiúscula é tratada como nome e não passa pela regra de digitação.</param>
    public Correction? Correct(string word, bool atSentenceStart = false)
    {
        bool looksLikeName = char.IsUpper(word.Length > 0 ? word[0] : ' ') && !atSentenceStart;
        if (word.Length == 0) return null;
        var w = word.ToLowerInvariant();

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

        // 5. Palavra que o dicionário aceita não é trocada (ficara/ficará, duvida/dúvida, capita/capitã).
        //    Única exceção: ela quase nunca aparece no corpus e está a um erro de uma palavra muito usada (erado → errado).
        if (_data.Lexicon.IsValid(word))
        {
            if (_data.Frequency.Count(w) >= AttestedCount || w.Length < MinTypoLength || looksLikeName) return null;
            return _typos.Best(w) is { } rare && rare.Count >= _typos.Options.RareValidMinCandidate
                ? new Correction(TextUtil.ApplyCase(word, rare.Word), CorrectionKind.Typo)
                : null;
        }

        // 6. Erro de digitação: só minúsculas (maiúscula no meio da frase é nome; sigla fica como está)
        TypoMatch? typo = null;
        if (!TextUtil.IsAllCaps(word) && !looksLikeName && w.Length >= 3 && _typos.Best(w) is { } t)
        {
            long typedCount = _data.Frequency.Count(w);
            bool shortOk = w.Length >= MinTypoLength
                || ((t.Kind is TypoKind.MissingLetter || (t.Kind is TypoKind.Transposition && _typos.Options.ShortWordTransposition))
                    && t.Count >= _typos.Options.ShortWordMinCount);
            var o = _typos.Options;
            bool slip = t.Kind is TypoKind.Transposition or TypoKind.DoubledLetter or TypoKind.MissingLetter;
            long ratio = slip ? o.AttestedSlipRatio : o.AttestedOtherRatio;
            bool attestedOk = typedCount < AttestedCount
                || (ratio > 0 && t.Count >= ratio * typedCount && !_data.Protected.Contains(w));
            if (shortOk && attestedOk) typo = t;
        }

        // 7. Acento esquecido: a acentuada tem que ganhar com folga da segunda e da própria forma digitada
        //    ("ana", nome, é muito mais escrita que "anã"). Ganha do erro de digitação, a não ser que este seja
        //    uma explicação muito melhor (vaos → vamos, não o raro "vãos").
        var cands = _accents.Lookup(TextUtil.StripAccents(w)).Where(c => c.Word != w).ToList();
        if (cands.Count > 0
            && cands[0].Count >= _typos.Options.AccentMinCount
            && (cands.Count == 1 || cands[0].Count >= AccentDominance * cands[1].Count)
            && cands[0].Count >= AccentDominance * _data.Frequency.Count(w))
        {
            var accent = cands[0];
            if (typo is { } better && better.Word != accent.Word
                && better.Score - Math.Log(accent.Count) >= _typos.Options.AccentYield)
                return new Correction(TextUtil.ApplyCase(word, better.Word), CorrectionKind.Typo);
            return new Correction(TextUtil.ApplyCase(word, accent.Word), CorrectionKind.Accent);
        }

        return typo is { } m ? new Correction(TextUtil.ApplyCase(word, m.Word), CorrectionKind.Typo) : null;
    }
}

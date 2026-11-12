namespace Acerto.Core;

public enum CorrectionKind { Shortcut, Fixed, Accent, Typo }

public sealed record Correction(string Replacement, CorrectionKind Kind);

/// <summary>Decide se uma palavra terminada deve ser trocada. Na dúvida, não troca.</summary>
public sealed class Corrector
{
    /// <summary>A melhor forma acentuada precisa ser pelo menos isso vezes mais usada que a segunda.</summary>
    public const double AccentDominance = 3.0;

    /// <summary>Palavra que existe sem acento só vira a acentuada se esta for pelo menos isso vezes mais usada.</summary>
    public const double RareFormRatio = 50.0;

    private readonly CoreData _data;
    private readonly AccentIndex _accents;
    private readonly TypoRanker _typos;

    public Corrector(CoreData data)
    {
        _data = data;
        _accents = new AccentIndex(data.Frequency, data.Lexicon);
        _typos = new TypoRanker(data.Lexicon, data.Frequency);
    }

    /// <param name="word">Só letras e hífen, sem pontuação.</param>
    /// <param name="atSentenceStart">Se a palavra começa uma frase (maiúscula ali não indica nome próprio).</param>
    public Correction? Correct(string word, bool atSentenceStart)
    {
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

        // 3. Lista embutida
        if (_data.Fixed.TryGet(w, out var fixedRep))
            return new Correction(TextUtil.ApplyCase(word, fixedRep), CorrectionKind.Fixed);

        // 4. Caixa mista ("iPhone") e hífen só passam pelas regras acima
        if (TextUtil.IsMixedCase(word) || w.Contains('-')) return null;

        // 5. Acento esquecido
        bool valid = _data.Lexicon.IsValid(word);
        var cands = _accents.Lookup(TextUtil.StripAccents(w)).Where(c => c.Word != w).ToList();
        if (cands.Count > 0)
        {
            var best = cands[0];
            bool dominates = cands.Count == 1 || best.Count >= AccentDominance * cands[1].Count;
            bool rareTyped = best.Count >= RareFormRatio * Math.Max(_data.Frequency.Count(w), 1);
            if (dominates && (!valid || rareTyped))
                return new Correction(TextUtil.ApplyCase(word, best.Word), CorrectionKind.Accent);
        }

        // 6. Palavra existe
        if (valid) return null;

        // 7. Sigla, palavra curta ou provável nome próprio
        if (TextUtil.IsAllCaps(word) || w.Length < 3) return null;
        if (char.IsUpper(word[0]) && !atSentenceStart) return null;

        // 8. Erro de digitação
        var typo = _typos.Best(w);
        return typo is null ? null : new Correction(TextUtil.ApplyCase(word, typo), CorrectionKind.Typo);
    }
}

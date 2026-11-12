namespace Acerto.Core;

public enum CorrectionKind { Shortcut, Fixed, Accent, Typo }

public sealed record Correction(string Replacement, CorrectionKind Kind);

/// <summary>Decide se uma palavra terminada deve ser trocada. Na dúvida, não troca.</summary>
public sealed class Corrector
{
    /// <summary>A melhor forma acentuada precisa ser pelo menos isso vezes mais usada que a segunda.</summary>
    public const double AccentDominance = 3.0;

    /// <summary>
    /// Palavra que aparece pelo menos isso no corpus é escrita de propósito (curitiba, apps, voice, mail): não é erro
    /// de digitação. Erros que muita gente comete (palvra, geito) também caem aqui; esses vão em correcoes.txt.
    /// Já tentei liberar pela proporção de uso da correção: estragou 286 palavras (voice→você, mail→mais).
    /// </summary>
    public const long AttestedCount = 10;

    /// <summary>Palavras menores que isso (siglas, "obs", "pdf", "job") não passam pela regra de digitação.</summary>
    public const int MinTypoLength = 4;

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
    public Correction? Correct(string word)
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

        // 3. Lista embutida (inclui as formas válidas-mas-raras como agua→água, escolhidas à mão)
        if (_data.Fixed.TryGet(w, out var fixedRep))
            return new Correction(TextUtil.ApplyCase(word, fixedRep), CorrectionKind.Fixed);

        // 4. Caixa mista ("iPhone") e hífen só passam pelas regras acima
        if (TextUtil.IsMixedCase(word) || w.Contains('-')) return null;

        // 5. Palavra que o dicionário aceita nunca é trocada (ficara/ficará, duvida/dúvida, capita/capitã)
        if (_data.Lexicon.IsValid(word)) return null;

        // 6. Acento esquecido: a acentuada tem que ganhar com folga da segunda e da própria forma digitada
        //    ("ana", nome, é muito mais escrita que "anã")
        var cands = _accents.Lookup(TextUtil.StripAccents(w)).Where(c => c.Word != w).ToList();
        if (cands.Count > 0
            && (cands.Count == 1 || cands[0].Count >= AccentDominance * cands[1].Count)
            && cands[0].Count >= AccentDominance * _data.Frequency.Count(w))
            return new Correction(TextUtil.ApplyCase(word, cands[0].Word), CorrectionKind.Accent);

        // 7. Sigla, palavra curta, nome próprio ou palavra que as pessoas escrevem de propósito
        if (TextUtil.IsAllCaps(word) || w.Length < MinTypoLength || char.IsUpper(word[0])) return null;
        if (_data.Frequency.Count(w) >= AttestedCount) return null;

        // 8. Erro de digitação
        var typo = _typos.Best(w);
        return typo is null ? null : new Correction(TextUtil.ApplyCase(word, typo), CorrectionKind.Typo);
    }
}

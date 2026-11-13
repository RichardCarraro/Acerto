namespace Acerto.Core;

/// <summary>
/// Sugestões enquanto a pessoa digita, como a barra do teclado do celular: completa a palavra em andamento
/// ("obrig" → obrigado) e, depois de um espaço, prevê a próxima ("bom" → dia). Usa a palavra anterior para ordenar.
/// </summary>
public sealed class Suggester
{
    private const long MinCount = 30;        // palavras raras demais não são sugeridas
    private const int MinPrefix = 2;

    private readonly (string Stripped, string Word, long Count)[] _sorted; // por forma sem acento
    private readonly TypoRanker _ranker;
    private readonly BigramModel _bigrams;
    private readonly Lexicon _lexicon;

    public Suggester(TypoRanker ranker, BigramModel bigrams, Lexicon lexicon)
    {
        _ranker = ranker;
        _bigrams = bigrams;
        _lexicon = lexicon;
        _sorted = ranker.IndexedWords.Where(w => w.Count >= MinCount).ToArray();
        Array.Sort(_sorted, (a, b) => string.CompareOrdinal(a.Stripped, b.Stripped));
    }

    /// <param name="prev">Palavra anterior em minúsculas, início de frase ou null.</param>
    /// <param name="typed">O que já foi digitado da palavra atual; vazio logo depois de um espaço.</param>
    public IReadOnlyList<string> Suggest(string? prev, string typed, int max = 3)
    {
        if (typed.Length == 0) return NextWords(prev, max);
        if (typed.Length < MinPrefix) return [];

        var lower = typed.ToLower(TextUtil.PtBr);
        var prefix = TextUtil.StripAccents(lower);
        var best = new List<(string Word, double Score)>();
        for (int i = LowerBound(prefix); i < _sorted.Length && _sorted[i].Stripped.StartsWith(prefix, StringComparison.Ordinal); i++)
        {
            var (stripped, word, count) = _sorted[i];
            if (word == lower) continue;                                   // já é o que está digitado
            if (stripped.Length == prefix.Length && prefix != lower) continue; // só mudaria acento que a pessoa já pôs
            best.Add((word, _ranker.EffectiveCount(word, count, prev)));
        }
        return best.OrderByDescending(b => b.Score).Take(max).Select(b => b.Word).ToList();
    }

    private IReadOnlyList<string> NextWords(string? prev, int max)
    {
        // no começo da frase quase qualquer palavra serve: sugerir ali só atrapalha
        if (prev is null || prev == BigramModel.SentenceStart) return [];
        return _bigrams.Successors(prev, max * 4).Where(w => w.Length > 1 && _lexicon.IsValid(w)).Take(max).ToList();
    }

    private int LowerBound(string prefix)
    {
        int lo = 0, hi = _sorted.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (string.CompareOrdinal(_sorted[mid].Stripped, prefix) < 0) lo = mid + 1; else hi = mid;
        }
        return lo;
    }
}

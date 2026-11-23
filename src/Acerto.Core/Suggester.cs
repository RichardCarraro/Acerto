namespace Acerto.Core;

/// <summary>
/// Sugestões enquanto a pessoa digita, como a barra do teclado do celular: completa a palavra em andamento
/// ("obrig" → obrigado, mesmo com erro: "obrgi" → obrigado) e, depois de um espaço, prevê a próxima ("bom" → dia).
/// Usa a palavra anterior e o histórico do usuário para ordenar.
/// </summary>
public sealed class Suggester
{
    private const long MinCount = 30;        // palavras raras demais não são sugeridas
    private const int MinPrefix = 2;
    private const int MinFuzzyPrefix = 4;    // com menos letras, "um erro" casa com metade do dicionário
    private const int MinUserCount = 2;      // palavra do usuário fora do corpus: só depois de escrita duas vezes

    private readonly (string Stripped, string Word, long Count)[] _sorted; // por forma sem acento
    private readonly TypoRanker _ranker;
    private readonly CoreData _data;

    public Suggester(TypoRanker ranker, CoreData data)
    {
        _ranker = ranker;
        _data = data;
        _sorted = ranker.IndexedWords.Where(w => w.Count >= MinCount).ToArray();
        Array.Sort(_sorted, (a, b) => string.CompareOrdinal(a.Stripped, b.Stripped));
        _maxLength = _sorted.Length == 0 ? 0 : _sorted.Max(s => s.Stripped.Length);
    }

    private readonly int _maxLength;

    private bool HasPrefix(string prefix)
    {
        int i = LowerBound(prefix);
        return i < _sorted.Length && _sorted[i].Stripped.StartsWith(prefix, StringComparison.Ordinal);
    }

    /// <param name="prev">Palavra anterior em minúsculas, início de frase ou null.</param>
    /// <param name="typed">O que já foi digitado da palavra atual; vazio logo depois de um espaço.</param>
    public IReadOnlyList<string> Suggest(string? prev, string typed, int max = 3)
    {
        if (typed.Length == 0) return NextWords(prev, max);
        if (typed.Length < MinPrefix) return [];

        var lower = typed.ToLower(TextUtil.PtBr);
        var prefix = TextUtil.StripAccents(lower);
        var best = new Dictionary<string, double>(StringComparer.Ordinal); // palavra → score (ln da contagem − erro)

        void Consider(string stripped, string word, long count, double cost)
        {
            if (word == lower) return;                                     // já é o que está digitado
            if (stripped.Length == prefix.Length && prefix != lower) return; // só mudaria acento que a pessoa já pôs
            double score = Math.Log(Math.Max(_ranker.EffectiveCount(word, count, prev, _ranker.Options.SuggestContextWeight), 0.01))
                - _ranker.Options.CostWeight * cost;
            if (!best.TryGetValue(word, out var s) || score > s) best[word] = score;
        }

        foreach (var (stripped, word, count) in StartingWith(prefix)) Consider(stripped, word, count, 0);
        // uma variação tem no máximo uma letra a menos: além disso nenhuma palavra indexada começa com ela (teclada)
        if (prefix.Length >= MinFuzzyPrefix && prefix.Length <= _maxLength + 1)
            foreach (var (near, cost) in _ranker.NearPrefixes(prefix, HasPrefix))
                foreach (var (stripped, word, count) in StartingWith(near)) Consider(stripped, word, count, cost);

        // palavras do usuário que o corpus não tem (ou quase não tem): nomes de sistema, jargão
        foreach (var (word, n) in _data.History.Words)
        {
            if (n < MinUserCount) continue;
            long count = _data.Frequency.Count(word);
            // comum e do dicionário: já entrou acima (o histórico só tem palavras do dicionário ou das "Minhas palavras")
            if (count >= MinCount && !_data.Personal.Contains(word)) continue;
            var stripped = TextUtil.StripAccents(word);
            if (stripped.StartsWith(prefix, StringComparison.Ordinal)) Consider(stripped, word, count, 0);
        }

        return best.OrderByDescending(b => b.Value).Take(max).Select(b => b.Key).ToList();
    }

    private IReadOnlyList<string> NextWords(string? prev, int max)
    {
        // no começo da frase quase qualquer palavra serve: sugerir ali só atrapalha
        if (prev is null || prev == BigramModel.SentenceStart) return [];

        // probabilidade de vir depois de "prev" no corpus, somada à do usuário (que ganha peso conforme ele usa)
        long total = _data.Bigrams.ContextTotal(prev);
        var user = _data.History.Successors(prev).ToList();
        double userTotal = user.Sum(u => u.Count) + 5; // + 5: um único uso não vira certeza
        var scores = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var w in _data.Bigrams.Successors(prev, max * 4))
            scores[w] = total == 0 ? 0 : (double)_data.Bigrams.Count(prev, w) / total;
        foreach (var (w, n) in user)
            scores[w] = scores.GetValueOrDefault(w) + n / userTotal;

        return scores.Where(s => s.Key.Length > 1 && _data.Lexicon.IsValid(s.Key))
            .OrderByDescending(s => s.Value).Take(max).Select(s => s.Key).ToList();
    }

    /// <summary>Palavras indexadas cuja forma sem acento começa com <paramref name="prefix"/>.</summary>
    private IEnumerable<(string Stripped, string Word, long Count)> StartingWith(string prefix)
    {
        for (int i = LowerBound(prefix); i < _sorted.Length && _sorted[i].Stripped.StartsWith(prefix, StringComparison.Ordinal); i++)
            yield return _sorted[i];
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

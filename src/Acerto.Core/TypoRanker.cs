namespace Acerto.Core;

/// <summary>
/// Acha a palavra que a pessoa quis digitar quando errou letras (troca, falta, sobra, inversão).
/// Só responde quando uma candidata ganha com folga; na dúvida devolve null.
/// </summary>
public sealed class TypoRanker
{
    public const double TypoMargin = 1.5;
    private const int MinIndexCount = 5;
    private const long MinBestCount = 50;
    private const double CostWeight = 4.0;
    private const double MaxCost = 2.0;

    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz";
    private static readonly string[] Abnt2Rows = ["qwertyuiop", "asdfghjklç", "zxcvbnm"];
    private static readonly Dictionary<char, (int Row, int Col)> KeyPos = BuildKeyPos();

    // forma sem acento → palavras reais (com acento) e contagem
    private readonly Dictionary<string, List<(string Word, long Count)>> _byStripped = new(StringComparer.Ordinal);
    // (1ª letra, tamanho) → formas sem acento; usado na busca de distância 2
    private readonly Dictionary<(char, int), List<string>> _buckets = [];

    public TypoRanker(Lexicon lex, FrequencyTable freq)
    {
        foreach (var (word, count) in freq.Entries)
        {
            if (count < MinIndexCount || !word.All(char.IsLetter) || !lex.IsValid(word)) continue;
            var stripped = TextUtil.StripAccents(word);
            if (!_byStripped.TryGetValue(stripped, out var list))
            {
                _byStripped[stripped] = list = [];
                var key = (stripped[0], stripped.Length);
                if (!_buckets.TryGetValue(key, out var b)) _buckets[key] = b = [];
                b.Add(stripped);
            }
            list.Add((word, count));
        }
    }

    public string? Best(string lowerWord)
    {
        var typed = TextUtil.StripAccents(lowerWord);
        if (typed.Length == 0 || !typed.All(c => c is >= 'a' and <= 'z')) return null;

        var cands = new Dictionary<string, double>(StringComparer.Ordinal); // forma sem acento → custo
        foreach (var e in Edits1(typed))
            if (e != typed && _byStripped.ContainsKey(e)) cands.TryAdd(e, Cost(typed, e));

        if (cands.Count == 0 && typed.Length >= 6)
        {
            // distância 2: só palavras com a mesma 1ª letra e tamanho parecido
            for (int len = typed.Length - 2; len <= typed.Length + 2; len++)
                if (_buckets.TryGetValue((typed[0], len), out var bucket))
                    foreach (var s in bucket)
                    {
                        var c = Cost(typed, s);
                        if (c <= MaxCost) cands.TryAdd(s, c);
                    }
        }
        if (cands.Count == 0) return null;

        string? best = null, second = null;
        double bestScore = double.NegativeInfinity, secondScore = double.NegativeInfinity;
        long bestCount = 0;
        foreach (var (stripped, cost) in cands)
            foreach (var (word, count) in _byStripped[stripped])
            {
                var score = Math.Log(count) - CostWeight * cost;
                if (score > bestScore)
                {
                    (second, secondScore) = (best, bestScore);
                    (best, bestScore, bestCount) = (word, score, count);
                }
                else if (score > secondScore)
                {
                    (second, secondScore) = (word, score);
                }
            }

        if (bestCount < MinBestCount) return null;
        if (second is not null && bestScore - secondScore < TypoMargin) return null;
        return best;
    }

    private static IEnumerable<string> Edits1(string w)
    {
        for (int i = 0; i <= w.Length; i++)
        {
            if (i < w.Length) yield return w.Remove(i, 1);                                          // sobra
            if (i < w.Length - 1) yield return string.Concat(w.AsSpan(0, i), [w[i + 1], w[i]], w.AsSpan(i + 2)); // inversão
            foreach (var c in Alphabet)
            {
                if (i < w.Length && c != w[i]) yield return string.Concat(w.AsSpan(0, i), [c], w.AsSpan(i + 1)); // troca
                yield return w.Insert(i, c.ToString());                                            // falta
            }
        }
    }

    /// <summary>Distância Damerau (OSA) com pesos: tecla vizinha 0.6, inversão 0.7, resto 1.0. Para cedo acima de MaxCost.</summary>
    private static double Cost(string a, string b)
    {
        if (Math.Abs(a.Length - b.Length) > MaxCost) return double.MaxValue;
        var d = new double[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            double rowMin = double.MaxValue;
            for (int j = 1; j <= b.Length; j++)
            {
                double sub = a[i - 1] == b[j - 1] ? 0 : Adjacent(a[i - 1], b[j - 1]) ? 0.6 : 1.0;
                double v = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + sub);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    v = Math.Min(v, d[i - 2, j - 2] + 0.7);
                d[i, j] = v;
                rowMin = Math.Min(rowMin, v);
            }
            if (rowMin > MaxCost) return double.MaxValue;
        }
        return d[a.Length, b.Length];
    }

    private static bool Adjacent(char x, char y)
    {
        if (!KeyPos.TryGetValue(x, out var p) || !KeyPos.TryGetValue(y, out var q)) return false;
        if (p.Row == q.Row) return Math.Abs(p.Col - q.Col) == 1;
        return Math.Abs(p.Row - q.Row) == 1 && Math.Abs(p.Col - q.Col) <= 1;
    }

    private static Dictionary<char, (int, int)> BuildKeyPos()
    {
        var map = new Dictionary<char, (int, int)>();
        for (int r = 0; r < Abnt2Rows.Length; r++)
            for (int c = 0; c < Abnt2Rows[r].Length; c++)
                map[Abnt2Rows[r][c]] = (r, c);
        return map;
    }
}

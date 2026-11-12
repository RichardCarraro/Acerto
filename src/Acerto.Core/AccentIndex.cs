namespace Acerto.Core;

/// <summary>"palavra sem acento" → palavras reais com acento/ç, mais usadas primeiro.</summary>
public sealed class AccentIndex
{
    private const int MinCount = 3;
    private readonly Dictionary<string, List<(string Word, long Count)>> _map = new(StringComparer.Ordinal);

    public AccentIndex(FrequencyTable freq, Lexicon lex)
    {
        foreach (var (word, count) in freq.Entries)
        {
            if (count < MinCount) continue;
            var stripped = TextUtil.StripAccents(word);
            if (stripped == word || !lex.IsValid(word)) continue;
            if (!_map.TryGetValue(stripped, out var list)) _map[stripped] = list = [];
            list.Add((word, count));
        }
        foreach (var list in _map.Values) list.Sort((a, b) => b.Count.CompareTo(a.Count));
    }

    public IReadOnlyList<(string Word, long Count)> Lookup(string strippedLower) =>
        _map.TryGetValue(strippedLower, out var list) ? list : [];
}

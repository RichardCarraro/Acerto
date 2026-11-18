namespace Acerto.Core;

/// <summary>"palavra sem acento" → palavras reais com acento/ç, mais usadas primeiro.</summary>
public sealed class AccentIndex
{
    private const int MinCount = 3;
    private readonly Dictionary<string, List<(string Word, long Count)>> _map = new(StringComparer.Ordinal);

    public AccentIndex(FrequencyTable freq, Lexicon lex)
    {
        var accented = freq.Entries.Where(e => e.Count >= MinCount && TextUtil.StripAccents(e.Word) != e.Word).ToArray();
        var valid = lex.AreValid(accented.Select(e => e.Word).ToArray());
        for (int i = 0; i < accented.Length; i++)
        {
            if (!valid[i]) continue;
            var (word, count) = accented[i];
            var stripped = TextUtil.StripAccents(word);
            if (!_map.TryGetValue(stripped, out var list)) _map[stripped] = list = [];
            list.Add((word, count));
        }
        foreach (var list in _map.Values) list.Sort((a, b) => b.Count.CompareTo(a.Count));
    }

    public IReadOnlyList<(string Word, long Count)> Lookup(string strippedLower) =>
        _map.TryGetValue(strippedLower, out var list) ? list : [];
}

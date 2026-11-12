namespace Acerto.Core;

/// <summary>Contagem de uso de cada palavra no português ("palavra contagem" por linha, minúsculas).</summary>
public sealed class FrequencyTable
{
    private readonly Dictionary<string, long> _counts = new(StringComparer.Ordinal);

    public FrequencyTable(string path)
    {
        foreach (var line in File.ReadLines(path))
        {
            int sp = line.LastIndexOf(' ');
            if (sp <= 0 || !long.TryParse(line.AsSpan(sp + 1), out var n)) continue;
            var word = line[..sp].ToLowerInvariant();
            _counts[word] = _counts.GetValueOrDefault(word) + n;
        }
    }

    public long Count(string lowerWord) => _counts.GetValueOrDefault(lowerWord);

    public IEnumerable<(string Word, long Count)> Entries => _counts.Select(kv => (kv.Key, kv.Value));
}

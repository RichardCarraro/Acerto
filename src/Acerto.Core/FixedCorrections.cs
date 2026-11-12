namespace Acerto.Core;

/// <summary>Lista curada que vem com o programa: "errado&lt;TAB&gt;certo" por linha.</summary>
public sealed class FixedCorrections
{
    private readonly Dictionary<string, string> _map = new(StringComparer.Ordinal);

    public FixedCorrections(string path)
    {
        foreach (var line in File.ReadLines(path))
        {
            var parts = line.Split('\t', 2);
            if (parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0)
                _map[parts[0].ToLowerInvariant()] = parts[1];
        }
    }

    public int Count => _map.Count;

    public bool TryGet(string lowerWord, out string replacement) => _map.TryGetValue(lowerWord, out replacement!);
}

namespace Acerto.Core;

/// <summary>Atalhos do usuário ("gatilho=texto" por linha). Na primeira vez, copia os atalhos padrão.</summary>
public sealed class ShortcutTable
{
    private readonly string _userPath;
    private Dictionary<string, string> _map = new(StringComparer.Ordinal);

    public ShortcutTable(string userPath, string defaultsPath)
    {
        _userPath = userPath;
        if (!File.Exists(userPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(userPath)!);
            if (File.Exists(defaultsPath)) File.Copy(defaultsPath, userPath);
            else File.WriteAllText(userPath, "");
        }
        foreach (var line in File.ReadLines(userPath))
        {
            int eq = line.IndexOf('=');
            if (eq <= 0 || eq == line.Length - 1) continue;
            _map[line[..eq].Trim().ToLowerInvariant()] = line[(eq + 1)..];
        }
    }

    public IReadOnlyDictionary<string, string> All => _map;

    public bool TryGet(string lowerTrigger, out string expansion) => _map.TryGetValue(lowerTrigger, out expansion!);

    public void Save(IDictionary<string, string> shortcuts)
    {
        _map = shortcuts.ToDictionary(kv => kv.Key.Trim().ToLowerInvariant(), kv => kv.Value, StringComparer.Ordinal);
        File.WriteAllLines(_userPath, _map.Select(kv => $"{kv.Key}={kv.Value}"));
    }
}

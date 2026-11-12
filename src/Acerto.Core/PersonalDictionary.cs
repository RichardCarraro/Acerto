namespace Acerto.Core;

/// <summary>Palavras que o usuário ensinou (uma por linha, minúsculas). Salva a cada mudança.</summary>
public sealed class PersonalDictionary
{
    private readonly string _path;
    private readonly HashSet<string> _words = new(StringComparer.Ordinal);

    public PersonalDictionary(string path)
    {
        _path = path;
        if (!File.Exists(path))
        {
            Save();
            return;
        }
        foreach (var line in File.ReadLines(path))
            if (line.Trim() is { Length: > 0 } w) _words.Add(w.ToLowerInvariant());
    }

    public IReadOnlyCollection<string> Words => _words;

    public bool Contains(string lowerWord) => _words.Contains(lowerWord);

    public void Add(string word)
    {
        if (_words.Add(word.Trim().ToLowerInvariant())) Save();
    }

    public void Remove(string word)
    {
        if (_words.Remove(word.Trim().ToLowerInvariant())) Save();
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllLines(_path, _words.Order(StringComparer.Ordinal));
    }
}

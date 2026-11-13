namespace Acerto.Core;

/// <summary>
/// O que o usuário escreve, contado só neste computador (historico.txt): quantas vezes usou cada palavra e cada par
/// de palavras vizinhas. Deixa as sugestões e correções com o jeito dele, como o teclado do celular.
/// Quem grava decide o que entra (só palavras do dicionário ou das "Minhas palavras"; nada de campo de senha).
/// </summary>
public sealed class UserHistory
{
    /// <summary>shortcut: ao passar disso apaga os pares vistos uma vez só; trocar por "menos recentes" se crescer demais.</summary>
    public const int MaxPairs = 50_000;

    private readonly string? _path;
    private readonly Dictionary<string, int> _words = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, int>> _next = new(StringComparer.Ordinal); // anterior → seguintes
    private readonly Dictionary<string, int> _contextTotal = new(StringComparer.Ordinal);            // anterior → soma
    private int _pairCount;
    private int _pruneAt = MaxPairs;

    public static UserHistory Empty => new(null);

    /// <param name="path">Arquivo "palavra\tcontagem" e "anterior\tpalavra\tcontagem" por linha; null = só em memória.</param>
    public UserHistory(string? path)
    {
        _path = path;
        if (path is null || !File.Exists(path)) return;
        foreach (var line in File.ReadLines(path))
        {
            var f = line.Split('\t');
            if (!int.TryParse(f[^1], out int n) || n <= 0) continue;
            if (f.Length == 2)
            {
                _words[f[0]] = n;
                Total += n;
            }
            else if (f.Length == 3) Pair(f[0], f[1], n);
        }
    }

    /// <summary>Mudou desde a última gravação.</summary>
    public bool Dirty { get; private set; }

    public bool IsEmpty => _words.Count == 0;

    /// <summary>Quantas palavras o usuário escreveu (soma das contagens).</summary>
    public long Total { get; private set; }

    /// <summary>Quantas vezes escreveu alguma palavra logo depois de <paramref name="prev"/>.</summary>
    public int ContextTotal(string prev) => _contextTotal.GetValueOrDefault(prev);

    public IEnumerable<(string Word, int Count)> Words => _words.Select(kv => (kv.Key, kv.Value));

    public int Count(string word) => _words.GetValueOrDefault(word);

    public int PairCount(string prev, string word) =>
        _next.TryGetValue(prev, out var next) ? next.GetValueOrDefault(word) : 0;

    /// <summary>Palavras que o usuário já escreveu depois de <paramref name="prev"/>, com a contagem.</summary>
    public IEnumerable<(string Word, int Count)> Successors(string prev) =>
        _next.TryGetValue(prev, out var next) ? next.Select(kv => (kv.Key, kv.Value)) : [];

    /// <param name="prev">Palavra anterior em minúsculas, <see cref="BigramModel.SentenceStart"/> ou null.</param>
    /// <param name="word">Palavra em minúsculas.</param>
    public void Record(string? prev, string word)
    {
        _words[word] = _words.GetValueOrDefault(word) + 1;
        Total++;
        if (prev is not null) Pair(prev, word, 1);
        Dirty = true;
        if (_pairCount > _pruneAt) Prune();
    }

    public void Clear()
    {
        _words.Clear();
        _next.Clear();
        _contextTotal.Clear();
        _pairCount = 0;
        Total = 0;
        Dirty = true;
    }

    public void Save()
    {
        if (_path is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var lines = _words.Select(kv => $"{kv.Key}\t{kv.Value}")
            .Concat(_next.SelectMany(p => p.Value.Select(kv => $"{p.Key}\t{kv.Key}\t{kv.Value}")));
        var tmp = _path + ".tmp";
        File.WriteAllLines(tmp, lines); // arquivo inteiro de uma vez: queda de energia não deixa metade gravada
        File.Move(tmp, _path, overwrite: true);
        Dirty = false;
    }

    private void Pair(string prev, string word, int n)
    {
        if (!_next.TryGetValue(prev, out var next)) _next[prev] = next = new(StringComparer.Ordinal);
        if (!next.ContainsKey(word)) _pairCount++;
        next[word] = next.GetValueOrDefault(word) + n;
        _contextTotal[prev] = _contextTotal.GetValueOrDefault(prev) + n;
    }

    private void Prune()
    {
        foreach (var (prev, next) in _next.ToList())
        {
            foreach (var (word, n) in next.ToList())
                if (n == 1)
                {
                    next.Remove(word);
                    _pairCount--;
                    _contextTotal[prev]--;
                }
            if (next.Count == 0)
            {
                _next.Remove(prev);
                _contextTotal.Remove(prev);
            }
        }
        _pruneAt = Math.Max(MaxPairs, _pairCount + MaxPairs / 10); // quase tudo repetido: não varrer a cada palavra
    }
}

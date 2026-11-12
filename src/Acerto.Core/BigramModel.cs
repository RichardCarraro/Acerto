using System.Globalization;

namespace Acerto.Core;

/// <summary>
/// Pares de palavras vizinhas ("minha casa", "esse caso") com contagem: dado o que veio antes, quão provável é cada
/// palavra seguinte. É o que o teclado do celular usa para escolher a correção certa pelo contexto.
/// Arquivo gerado por "AcertoEval data bigramas"; sem o arquivo o modelo fica vazio e nada muda.
/// </summary>
public sealed class BigramModel
{
    /// <summary>Marca de início de frase, usada como "palavra anterior" da primeira palavra.</summary>
    public const string SentenceStart = "<s>";

    private readonly Dictionary<string, int> _ids = new(StringComparer.Ordinal);
    private readonly List<long> _contextTotal = [];
    private readonly Dictionary<long, int> _pairs = [];
    private readonly List<string> _words = [];
    private readonly Dictionary<int, List<int>> _successors = []; // na ordem do arquivo: da mais comum para a menos

    public static BigramModel Empty { get; } = new();

    private BigramModel() { }

    public BigramModel(string path)
    {
        int context = -1;
        foreach (var line in File.ReadLines(path))
        {
            int sp = line.LastIndexOf(' ');
            if (sp <= 0 || !long.TryParse(line.AsSpan(sp + 1), NumberStyles.None, CultureInfo.InvariantCulture, out long n)) continue;
            if (line[0] == '=')
            {
                context = Id(line[1..sp]);
                _contextTotal[context] = n;
            }
            else if (context >= 0)
            {
                int next = Id(line[..sp]);
                _pairs[Key(context, next)] = (int)Math.Min(n, int.MaxValue);
                if (!_successors.TryGetValue(context, out var list)) _successors[context] = list = [];
                list.Add(next);
            }
        }
    }

    public bool IsEmpty => _pairs.Count == 0;

    /// <summary>Quantas vezes <paramref name="prev"/> apareceu no corpus (0 se desconhecida).</summary>
    public long ContextTotal(string prev) => _ids.TryGetValue(prev, out int id) ? _contextTotal[id] : 0;

    /// <summary>Quantas vezes <paramref name="word"/> veio logo depois de <paramref name="prev"/>.</summary>
    public int Count(string prev, string word) =>
        _ids.TryGetValue(prev, out int a) && _ids.TryGetValue(word, out int b) ? _pairs.GetValueOrDefault(Key(a, b)) : 0;

    /// <summary>As palavras que mais vêm depois de <paramref name="prev"/>, da mais comum para a menos.</summary>
    public IEnumerable<string> Successors(string prev, int max)
    {
        if (!_ids.TryGetValue(prev, out int id) || !_successors.TryGetValue(id, out var list)) yield break;
        for (int i = 0; i < list.Count && i < max; i++) yield return _words[list[i]];
    }

    /// <summary>
    /// Quebra um texto em palavras minúsculas, do mesmo jeito para gerar a tabela e para medir.
    /// Devolve null onde a sequência se quebra (pontuação, número) e <see cref="SentenceStart"/> depois de . ! ?
    /// </summary>
    public static IEnumerable<string?> Tokenize(string line)
    {
        int tab = line.IndexOf('\t');
        var text = tab >= 0 ? line.AsMemory(tab + 1) : line.AsMemory();
        var sb = new System.Text.StringBuilder();
        var pt = TextUtil.PtBr;
        for (int i = 0; i <= text.Length; i++)
        {
            char c = i < text.Length ? text.Span[i] : ' ';
            bool inner = (c is '-' or '\'') && sb.Length > 0 && i + 1 < text.Length && char.IsLetter(text.Span[i + 1]);
            if (char.IsLetter(c) || inner)
            {
                sb.Append(char.ToLower(c, pt));
                continue;
            }
            if (sb.Length > 0)
            {
                yield return sb.ToString();
                sb.Clear();
            }
            if (char.IsWhiteSpace(c)) continue;
            yield return c is '.' or '!' or '?' ? SentenceStart : null;
        }
    }

    private int Id(string word)
    {
        if (!_ids.TryGetValue(word, out int id))
        {
            id = _contextTotal.Count;
            _ids[word] = id;
            _contextTotal.Add(0);
            _words.Add(word);
        }
        return id;
    }

    private static long Key(int a, int b) => ((long)a << 32) | (uint)b;
}

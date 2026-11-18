using WeCantSpell.Hunspell;

namespace Acerto.Core;

/// <summary>Dicionário pt-BR (Hunspell/VERO): sabe todas as flexões e conjugações.</summary>
public sealed class Lexicon
{
    private readonly WordList _words;

    public Lexicon(string affPath, string dicPath) => _words = WordList.CreateFromFiles(dicPath, affPath);

    /// <summary>
    /// As palavras da tabela de frequência que o Hunspell aceita, já conferidas (data/validas.txt, gerado por
    /// "AcertoEval data validas"). Com ela, <see cref="AreValid"/> não pergunta ao Hunspell. Null = pergunta.
    /// </summary>
    public IReadOnlySet<string>? CorpusValid { get; init; }

    /// <summary>Forma exata; o Hunspell já aceita palavra comum com 1ª maiúscula ("Casa") e rejeita nome próprio em minúsculo ("curitiba").</summary>
    public bool IsValid(string word) => word.Length > 0 && _words.Check(word);

    /// <summary>
    /// Muitas palavras da tabela de frequência de uma vez (só elas: é o que <see cref="CorpusValid"/> cobre); resposta
    /// na mesma ordem. Sem a lista, pergunta ao Hunspell em paralelo (a WordList só é lida depois de carregada).
    /// </summary>
    public bool[] AreValid(string[] corpusWords)
    {
        var valid = new bool[corpusWords.Length];
        if (CorpusValid is { } known)
            for (int i = 0; i < corpusWords.Length; i++) valid[i] = known.Contains(corpusWords[i]);
        else
            Parallel.For(0, corpusWords.Length, i => valid[i] = IsValid(corpusWords[i]));
        return valid;
    }

    /// <summary>O conteúdo de data/validas.txt: as palavras da frequência que <paramref name="lex"/> aceita, em ordem.</summary>
    public static IEnumerable<string> ValidasDoCorpus(FrequencyTable freq, Lexicon lex)
    {
        var words = freq.Entries.Select(e => e.Word).ToArray();
        var ok = lex.AreValid(words);
        return words.Where((_, i) => ok[i]).Order(StringComparer.Ordinal);
    }
}

using WeCantSpell.Hunspell;

namespace Acerto.Core;

/// <summary>Dicionário pt-BR (Hunspell/VERO): sabe todas as flexões e conjugações.</summary>
public sealed class Lexicon
{
    private readonly WordList _words;

    public Lexicon(string affPath, string dicPath) => _words = WordList.CreateFromFiles(dicPath, affPath);

    /// <summary>Forma exata; o Hunspell já aceita palavra comum com 1ª maiúscula ("Casa") e rejeita nome próprio em minúsculo ("curitiba").</summary>
    public bool IsValid(string word) => word.Length > 0 && _words.Check(word);
}

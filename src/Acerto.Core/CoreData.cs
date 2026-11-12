namespace Acerto.Core;

/// <summary>Tudo que o corretor precisa: dados do programa (somente-leitura) + dados do usuário.</summary>
public sealed class CoreData
{
    public required Lexicon Lexicon { get; init; }
    public required FrequencyTable Frequency { get; init; }
    public required FixedCorrections Fixed { get; init; }
    public required PersonalDictionary Personal { get; init; }
    public required ShortcutTable Shortcuts { get; init; }

    public static CoreData Load(string dataDir, string userDir) => new()
    {
        Lexicon = new Lexicon(Path.Combine(dataDir, "pt_BR.aff"), Path.Combine(dataDir, "pt_BR.dic")),
        Frequency = new FrequencyTable(Path.Combine(dataDir, "frequencia.txt")),
        Fixed = new FixedCorrections(Path.Combine(dataDir, "correcoes.txt")),
        Personal = new PersonalDictionary(Path.Combine(userDir, "palavras.txt")),
        Shortcuts = new ShortcutTable(Path.Combine(userDir, "atalhos.txt"), Path.Combine(dataDir, "atalhos-padrao.txt")),
    };
}

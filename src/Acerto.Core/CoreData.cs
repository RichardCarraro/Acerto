namespace Acerto.Core;

/// <summary>Tudo que o corretor precisa: dados do programa (somente-leitura) + dados do usuário.</summary>
public sealed class CoreData
{
    public required Lexicon Lexicon { get; init; }
    public required FrequencyTable Frequency { get; init; }
    public required FixedCorrections Fixed { get; init; }
    public required PersonalDictionary Personal { get; init; }
    public required ShortcutTable Shortcuts { get; init; }

    /// <summary>
    /// Locuções escritas à mão, "errada&lt;TAB&gt;certa", duas palavras cada: "isso ai" → "isso aí", "a vontade" →
    /// "à vontade", "a viajem" → "a viagem". Para o que a estatística de pares não decide (crase, homófonos válidos).
    /// </summary>
    public FixedCorrections Pairs { get; init; } = FixedCorrections.Empty;

    /// <summary>Nomes e lugares brasileiros, "minúscula sem acento&lt;TAB&gt;forma certa" (joao → João, parana → Paraná).</summary>
    public FixedCorrections Names { get; init; } = FixedCorrections.Empty;

    /// <summary>
    /// Estrangeirismos e nomes que o dicionário pt-BR não tem mas que são palavras de verdade (mail, voice, office).
    /// Nunca passam pela regra de digitação. Gerada com "AcertoEval data protegidas"; as do fim (termos de trabalho e
    /// tecnologia: deploy, commit, teams) foram acrescentadas à mão e precisam ser mantidas ao gerar de novo.
    /// </summary>
    public IReadOnlySet<string> Protected { get; init; } = new HashSet<string>();

    /// <summary>Pares de palavras vizinhas para a correção por contexto. Vazio se data/bigramas.txt não existir.</summary>
    public BigramModel Bigrams { get; init; } = BigramModel.Empty;

    /// <summary>Palavras e pares que o usuário escreve, para as sugestões e correções ficarem com o jeito dele.</summary>
    public UserHistory History { get; init; } = UserHistory.Empty;

    public static CoreData Load(string dataDir, string userDir) => new()
    {
        History = new UserHistory(Path.Combine(userDir, "historico.txt")),
        Lexicon = new Lexicon(Path.Combine(dataDir, "pt_BR.aff"), Path.Combine(dataDir, "pt_BR.dic")),
        Frequency = new FrequencyTable(Path.Combine(dataDir, "frequencia.txt")),
        Fixed = new FixedCorrections(Path.Combine(dataDir, "correcoes.txt")),
        Pairs = File.Exists(Path.Combine(dataDir, "locucoes.txt")) ? new FixedCorrections(Path.Combine(dataDir, "locucoes.txt")) : FixedCorrections.Empty,
        Names = File.Exists(Path.Combine(dataDir, "nomes.txt")) ? new FixedCorrections(Path.Combine(dataDir, "nomes.txt")) : FixedCorrections.Empty,
        Personal = new PersonalDictionary(Path.Combine(userDir, "palavras.txt")),
        Shortcuts = new ShortcutTable(Path.Combine(userDir, "atalhos.txt"), Path.Combine(dataDir, "atalhos-padrao.txt")),
        Protected = File.Exists(Path.Combine(dataDir, "protegidas.txt"))
            ? File.ReadLines(Path.Combine(dataDir, "protegidas.txt")).Select(l => l.Trim()).Where(l => l.Length > 0).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(),
        Bigrams = File.Exists(Path.Combine(dataDir, "bigramas.txt")) ? new BigramModel(Path.Combine(dataDir, "bigramas.txt")) : BigramModel.Empty,
    };
}

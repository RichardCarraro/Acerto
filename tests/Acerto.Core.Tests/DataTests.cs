namespace Acerto.Core.Tests;

/// <summary>Carrega os dados reais uma vez só para todos os testes (o dicionário é pesado).</summary>
public sealed class SharedData
{
    public static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    public Lexicon Lexicon { get; } = new(Path.Combine(DataDir, "pt_BR.aff"), Path.Combine(DataDir, "pt_BR.dic"));
    public FrequencyTable Frequency { get; } = new(Path.Combine(DataDir, "frequencia.txt"));
    public FixedCorrections Fixed { get; } = new(Path.Combine(DataDir, "correcoes.txt"));

    public static string NewUserDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "acerto-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}

public class DataTests(SharedData data) : IClassFixture<SharedData>
{
    [Theory]
    [InlineData("informação", true)]
    [InlineData("informacao", false)]
    [InlineData("correríamos", true)]
    [InlineData("Curitiba", true)]
    [InlineData("curitiba", false)]
    public void Lexicon_IsValid(string word, bool expected) => Assert.Equal(expected, data.Lexicon.IsValid(word));

    [Fact]
    public void Lexicon_AceitaPrimeiraMaiusculaDePalavraComum() => Assert.True(data.Lexicon.IsValid("Casa"));

    [Fact]
    public void Frequency_AcentuadaMaisComum() => Assert.True(data.Frequency.Count("não") > data.Frequency.Count("nao"));

    [Fact]
    public void Frequency_Ausente_Zero() => Assert.Equal(0, data.Frequency.Count("xkzqwv"));

    [Fact] // palavras quase nunca usadas não entram: só gastam memória
    public void Frequency_SoPalavrasComUsoMinimo() => Assert.All(data.Frequency.Entries, e => Assert.True(e.Count >= FrequencyTable.MinCount));

    [Fact]
    public void Fixed_Carrega()
    {
        Assert.True(data.Fixed.Count >= 160);
        Assert.True(data.Fixed.TryGet("oque", out var r));
        Assert.Equal("o que", r);
    }

    [Fact]
    public void Personal_Persiste()
    {
        var path = Path.Combine(SharedData.NewUserDir(), "palavras.txt");
        new PersonalDictionary(path).Add("CRCPR");
        Assert.True(new PersonalDictionary(path).Contains("crcpr"));
    }

    [Fact]
    public void Personal_CriaArquivoSeNaoExiste()
    {
        var path = Path.Combine(SharedData.NewUserDir(), "palavras.txt");
        _ = new PersonalDictionary(path);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Personal_Remove_Persiste()
    {
        var path = Path.Combine(SharedData.NewUserDir(), "palavras.txt");
        var p = new PersonalDictionary(path);
        p.Add("crcpr");
        p.Remove("CRCPR");
        Assert.False(new PersonalDictionary(path).Contains("crcpr"));
    }

    [Fact]
    public void Shortcuts_CopiaPadrao_E_Salva()
    {
        var user = Path.Combine(SharedData.NewUserDir(), "atalhos.txt");
        var s = new ShortcutTable(user, Path.Combine(SharedData.DataDir, "atalhos-padrao.txt"));
        Assert.True(s.TryGet("vlw", out var e));
        Assert.Equal("valeu", e);
        Assert.True(s.TryGet("att", out var att));
        Assert.Equal("Atenciosamente,", att);

        s.Save(new Dictionary<string, string> { ["fds"] = "fim de semana" });
        var reloaded = new ShortcutTable(user, Path.Combine(SharedData.DataDir, "atalhos-padrao.txt"));
        Assert.False(reloaded.TryGet("vlw", out _));
        Assert.True(reloaded.TryGet("fds", out var f));
        Assert.Equal("fim de semana", f);
    }

    [Fact]
    public void CoreData_Load()
    {
        var core = CoreData.Load(SharedData.DataDir, SharedData.NewUserDir());
        Assert.True(core.Lexicon.IsValid("casa"));
        Assert.True(core.Shortcuts.TryGet("tbm", out _));
    }
}

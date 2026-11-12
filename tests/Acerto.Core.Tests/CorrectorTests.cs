namespace Acerto.Core.Tests;

/// <summary>Um CoreData real (dicionário pesado) compartilhado; dados do usuário num diretório temporário.</summary>
public sealed class SharedCorrector
{
    public CoreData Data { get; } = CoreData.Load(SharedData.DataDir, SharedData.NewUserDir());
    public Corrector Corrector { get; }
    public SharedCorrector() => Corrector = new Corrector(Data);

    public static IEnumerable<string[]> ReadPairs(string path) =>
        File.ReadLines(path).Select(l => l.Split('\t')).Where(p => p.Length == 2).Select(p => new[] { p[0], p[1] });

    /// <summary>correcoes.txt (lista embutida) + deve-corrigir.txt (casos extras).</summary>
    public static IEnumerable<object[]> DeveCorrigir() =>
        ReadPairs(Path.Combine(SharedData.DataDir, "correcoes.txt"))
            .Concat(ReadPairs(Path.Combine(AppContext.BaseDirectory, "Data", "deve-corrigir.txt")))
            .Select(p => new object[] { p[0], p[1] });

    public static IEnumerable<object[]> NaoMexer() =>
        File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Data", "nao-mexer.txt"))
            .Where(l => l.Trim().Length > 0).Select(l => new object[] { l.Trim() });
}

public class CorrectorTests(SharedCorrector s) : IClassFixture<SharedCorrector>
{
    private string? C(string w, bool start = false) => s.Corrector.Correct(w, start)?.Replacement;

    public static IEnumerable<object[]> DeveCorrigir() => SharedCorrector.DeveCorrigir();
    public static IEnumerable<object[]> NaoMexer() => SharedCorrector.NaoMexer();

    [Theory, MemberData(nameof(DeveCorrigir))]
    public void Corrige(string errado, string certo) => Assert.Equal(certo, C(errado));

    [Theory, MemberData(nameof(NaoMexer))]
    public void NaoMexe(string palavra) => Assert.Null(s.Corrector.Correct(palavra, false));

    [Fact]
    public void Maiusculas()
    {
        Assert.Equal("Não", C("Nao"));
        Assert.Equal("NÃO", C("NAO"));
        Assert.Equal("O que", C("Oque"));
    }

    [Fact]
    public void TipoDaCorrecao()
    {
        Assert.Equal(CorrectionKind.Fixed, s.Corrector.Correct("oque", false)!.Kind);
        Assert.Equal(CorrectionKind.Accent, s.Corrector.Correct("aviao", false)!.Kind);
        Assert.Equal(CorrectionKind.Shortcut, s.Corrector.Correct("vlw", false)!.Kind);
    }

    [Fact]
    public void AprendidaGanhaDaListaFixa()
    {
        var data = CoreData.Load(SharedData.DataDir, SharedData.NewUserDir());
        data.Personal.Add("oque");
        Assert.Null(new Corrector(data).Correct("oque", false));
    }

    [Fact]
    public void AtalhoGanhaDoDicionario()
    {
        var data = CoreData.Load(SharedData.DataDir, SharedData.NewUserDir());
        data.Shortcuts.Save(new Dictionary<string, string> { ["casa"] = "Casa Branca" });
        Assert.Equal("Casa Branca", new Corrector(data).Correct("casa", false)?.Replacement);
    }

    [Fact]
    public void Atalho_PrimeiraMaiuscula() => Assert.Equal("Valeu", C("Vlw"));
}

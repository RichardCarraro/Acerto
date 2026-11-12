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
    private string? C(string w) => s.Corrector.Correct(w)?.Replacement;

    public static IEnumerable<object[]> DeveCorrigir() => SharedCorrector.DeveCorrigir();
    public static IEnumerable<object[]> NaoMexer() => SharedCorrector.NaoMexer();

    [Theory, MemberData(nameof(DeveCorrigir))]
    public void Corrige(string errado, string certo) => Assert.Equal(certo, C(errado));

    [Theory, MemberData(nameof(NaoMexer))]
    public void NaoMexe(string palavra) => Assert.Null(s.Corrector.Correct(palavra));

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
        Assert.Equal(CorrectionKind.Fixed, s.Corrector.Correct("oque")!.Kind);
        Assert.Equal(CorrectionKind.Accent, s.Corrector.Correct("aviao")!.Kind);
        Assert.Equal(CorrectionKind.Shortcut, s.Corrector.Correct("vlw")!.Kind);
    }

    [Fact]
    public void AprendidaGanhaDaListaFixa()
    {
        var data = CoreData.Load(SharedData.DataDir, SharedData.NewUserDir());
        data.Personal.Add("oque");
        Assert.Null(new Corrector(data).Correct("oque"));
    }

    [Fact]
    public void AtalhoGanhaDoDicionario()
    {
        var data = CoreData.Load(SharedData.DataDir, SharedData.NewUserDir());
        data.Shortcuts.Save(new Dictionary<string, string> { ["casa"] = "Casa Branca" });
        Assert.Equal("Casa Branca", new Corrector(data).Correct("casa")?.Replacement);
    }

    [Fact]
    public void Atalho_PrimeiraMaiuscula() => Assert.Equal("Valeu", C("Vlw"));
}

public class CorrectorCorpusTests(SharedCorrector s) : IClassFixture<SharedCorrector>
{
    /// <summary>Palavra que as pessoas de fato escrevem não é erro de digitação (C1 da revisão final).</summary>
    [Fact]
    public void PalavrasFrequentes_NuncaViramTypo()
    {
        var wrong = s.Data.Frequency.Entries
            .Where(e => e.Word.All(char.IsLetter))
            .OrderByDescending(e => e.Count).Take(60000)
            .Select(e => (e.Word, C: s.Corrector.Correct(e.Word)))
            .Where(x => x.C?.Kind == CorrectionKind.Typo)
            .Select(x => $"{x.Word}→{x.C!.Replacement}")
            .ToList();
        Assert.True(wrong.Count == 0, $"{wrong.Count} trocadas: {string.Join(", ", wrong.Take(60))}");
    }

    /// <summary>A regra de acento nunca troca uma palavra que o dicionário aceita (C3 da revisão final).</summary>
    [Fact]
    public void PalavrasValidas_NuncaGanhamAcento()
    {
        var wrong = s.Data.Frequency.Entries
            .Where(e => e.Word.All(char.IsLetter))
            .OrderByDescending(e => e.Count).Take(60000)
            .Where(e => s.Data.Lexicon.IsValid(e.Word))
            .Select(e => (e.Word, C: s.Corrector.Correct(e.Word)))
            .Where(x => x.C?.Kind == CorrectionKind.Accent)
            .Select(x => $"{x.Word}→{x.C!.Replacement}")
            .ToList();
        Assert.True(wrong.Count == 0, $"{wrong.Count} trocadas: {string.Join(", ", wrong.Take(60))}");
    }
}

public class FixedListGuardTests(SharedCorrector s) : IClassFixture<SharedCorrector>
{
    // Entradas da lista embutida cuja forma "errada" o dicionário aceita. São escolhas feitas à mão
    // (agua→água, pagina→página...). Qualquer nova precisa ser decidida de propósito e adicionada aqui.
    private static readonly HashSet<string> KnownValid = new(StringComparer.Ordinal)
    {
        "ate", "numero", "numeros", "pagina", "paginas", "ultimo", "ultima", "historia", "agua", "area", "areas",
        "encima", "secretario", "musica", "tao", "acoes", "apos", "porem", "salario", "experiencia", "conferencia",
        "faco", "video", "oque", "emfim", "anida", "intendi", "intender", "intendeu", "amanha", "medico", "titulo",
        "transito", "policia", "noticia", "agencia", "irma", "dai",
    };

    [Fact]
    public void ListaEmbutida_NaoTrocaPalavraValida_SemDecisao()
    {
        var path = Path.Combine(SharedData.DataDir, "correcoes.txt");
        var surprising = SharedCorrector.ReadPairs(path)
            .Select(p => p[0])
            .Where(k => s.Data.Lexicon.IsValid(k) && !KnownValid.Contains(k))
            .ToList();
        Assert.True(surprising.Count == 0, "válidas no dicionário: " + string.Join(", ", surprising));
    }
}

/// <summary>Perfil padrão do app ("corrigir mais"): tenta sempre, mas não toca no que é claramente palavra.</summary>
public class AggressiveCorrectorTests : IClassFixture<SharedData>
{
    private readonly CoreData _data;
    private readonly Corrector _c;

    public AggressiveCorrectorTests(SharedData shared)
    {
        _data = CoreData.Load(SharedData.DataDir, SharedData.NewUserDir());
        _c = new Corrector(_data, TypoOptions.Aggressive);
    }

    [Theory]
    [InlineData("corrigndo")] // empate entre corrigindo e corrigido: no perfil cuidadoso desiste, aqui escolhe uma
    [InlineData("dizr")]
    [InlineData("precso")]
    public void Sempre_Tenta(string errado) => Assert.NotNull(_c.Correct(errado));

    [Theory]
    [InlineData("voice")] [InlineData("mail")] [InlineData("office")] [InlineData("apps")] [InlineData("build")]
    [InlineData("curitiba")] [InlineData("richard")]
    public void Protegidas_Ficam(string palavra) => Assert.Null(_c.Correct(palavra));

    [Theory]
    [InlineData("Carraro")] [InlineData("CRCPR")] [InlineData("iPhone")] [InlineData("cpf")] [InlineData("pdf")]
    public void Nomes_Siglas_Ficam(string palavra) => Assert.Null(_c.Correct(palavra));

    [Fact]
    public void PalavrasValidasFrequentes_NuncaMudam()
    {
        var wrong = _data.Frequency.Entries
            .Where(e => e.Word.All(char.IsLetter) && _data.Lexicon.IsValid(e.Word))
            .OrderByDescending(e => e.Count).Take(30000)
            .Where(e => _c.Correct(e.Word) is { Kind: not CorrectionKind.Fixed })
            .Select(e => e.Word).ToList();
        Assert.True(wrong.Count == 0, string.Join(", ", wrong.Take(40)));
    }

    /// <summary>O preço conhecido do perfil: algumas palavras estrangeiras do corpus são trocadas. Se passar muito disso, algo quebrou.</summary>
    [Fact]
    public void TrocasIndevidas_FicamAbaixoDoLimite()
    {
        int changed = _data.Frequency.Entries
            .Where(e => e.Word.All(char.IsLetter))
            .OrderByDescending(e => e.Count).Take(60000)
            .Count(e => _c.Correct(e.Word) is { Kind: CorrectionKind.Typo });
        Assert.True(changed <= 300, $"{changed} palavras frequentes trocadas (medido em 2026-11-12: 227)");
    }
}

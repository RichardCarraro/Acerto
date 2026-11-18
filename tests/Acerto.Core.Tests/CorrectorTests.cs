namespace Acerto.Core.Tests;

/// <summary>Um CoreData real (dicionário pesado) compartilhado; dados do usuário num diretório temporário.</summary>
public sealed class SharedCorrector
{
    public CoreData Data { get; } = CoreData.Load(SharedData.DataDir, SharedData.NewUserDir());
    public Corrector Corrector { get; }
    // sem histórico: os testes não podem depender da ordem em que rodam
    public SharedCorrector() => Corrector = new Corrector(Data) { LearnFromTyping = false };

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
        "negocio", // 1401 × no corpus contra 62899 × "negócio"; "eu negocio" é raro
        "deslise", "estrupo", // raras/arcaicas no dicionário; na prática, sempre erro de deslize/estupro
        "exite", "exitar", "exitou", "exitei", "exitando", // 77 × "exite" no corpus contra 1075 × "hesite"
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
    [InlineData("corrie")]    // aparece no corpus (nome em inglês): o cuidadoso não mexe, este tenta
    [InlineData("dizr")]
    [InlineData("precso")]
    public void Sempre_Tenta(string errado) => Assert.NotNull(_c.Correct(errado));

    [Theory]
    [InlineData("voice")] [InlineData("mail")] [InlineData("office")] [InlineData("apps")] [InlineData("build")]
    [InlineData("curitiba")] [InlineData("richard")]
    [InlineData("deploy")] [InlineData("commit")] [InlineData("teams")] [InlineData("jira")] // pouco usadas, à mão
    public void Protegidas_Ficam(string palavra) => Assert.True(_c.Correct(palavra) is null or { Kind: CorrectionKind.Case }); // só maiúscula de nome

    // Nome próprio que o dicionário só tem com maiúscula ganha a maiúscula; palavra comum e começo de frase não
    [Theory]
    [InlineData("curitiba", "Curitiba")] [InlineData("londres", "Londres")] [InlineData("alemanha", "Alemanha")] // joão, maria, brasil: o VERO aceita em minúscula
    public void Nome_Em_Minuscula_Ganha_Maiuscula(string typed, string want) => Assert.Equal(want, _c.Correct(typed)?.Replacement);
    [Fact] public void Palavra_Comum_Nao_Ganha_Maiuscula() => Assert.Null(_c.Correct("casa"));

    // Forma válida rara cuja acentuada é muito mais usada ganha o acento; as comuns (esta, sabia, pais) ficam
    [Theory]
    [InlineData("chegara", "chegará")] [InlineData("tera", "terá")] [InlineData("fara", "fará")] [InlineData("podera", "poderá")]
    public void Valida_Rara_Ganha_Acento(string typed, string want) => Assert.Equal(want, _c.Correct(typed)?.Replacement);
    // Acima do piso da regra acima (1401 × "negocio", de "eu negocio" e de quem esquece o acento): vem da lista
    [Theory]
    [InlineData("negocio", "negócio")] [InlineData("negocios", "negócios")]
    public void Valida_Da_Lista_Ganha_Acento(string typed, string want) => Assert.Equal(want, _c.Correct(typed)?.Replacement);
    [Theory]
    [InlineData("esta")] [InlineData("sabia")] [InlineData("pais")] [InlineData("critica")] [InlineData("secretaria")]
    public void Valida_Comum_Fica(string typed) => Assert.Null(_c.Correct(typed));

    // data/nomes.txt: antes das outras regras, senão "parana" vira "parada" e "maringa" vira "marinha"
    [Theory]
    [InlineData("parana", "Paraná")] [InlineData("maringa", "Maringá")] [InlineData("joao", "João")] [InlineData("Joao", "João")]
    [InlineData("maria", "Maria")] [InlineData("brasil", "Brasil")] [InlineData("belem", "Belém")]
    public void Nome_Da_Lista(string typed, string want) => Assert.Equal(want, _c.Correct(typed)?.Replacement);
    [Fact] public void Nome_Da_Lista_Ja_Certo_Nao_Mexe() => Assert.Null(_c.Correct("Paraná"));
    [Fact] public void Nome_No_Comeco_Da_Frase_Fica_Para_A_Maiuscula_Automatica() => Assert.Null(_c.Correct("londres", atSentenceStart: true));
    [Fact] public void Nome_Em_Minuscula_No_Texto() => Assert.Equal(" fui para Curitiba ", TypeAll(" fui para curitiba "));

    private string TypeAll(string keys, TextEngine? engine = null)
    {
        engine ??= new TextEngine(_c, _ => { });
        var doc = new System.Text.StringBuilder();
        foreach (var k in keys)
        {
            var a = k == '\b' ? engine.OnBackspace() : engine.OnChar(k);
            if (a.Swallow) { doc.Length -= a.Backspaces; doc.Append(a.Insert); }
            else if (k == '\b') { if (doc.Length > 0) doc.Length--; }
            else doc.Append(k);
        }
        return doc.ToString();
    }

    // A palavra seguinte mostra que faltou o acento na anterior
    [Theory]
    [InlineData(" isso e muito bom ", " isso é muito bom ")]
    [InlineData(" o almoço esta pronto ", " o almoço está pronto ")]
    [InlineData(" a situacao e dificil ", " a situação é difícil ")]
    [InlineData(" a situação econômica esta complicada ", " a situação econômica está complicada ")] // par "está complicada" = 3 no corpus
    [InlineData(" a casa e a mesa ", " a casa e a mesa ")]          // conjunção fica
    [InlineData(" pão, e uma laranja ", " pão, e uma laranja ")]    // depois de vírgula não se sabe: fica
    [InlineData(" isso e muito\t", " isso e muito\t")]              // Tab não corrige
    public void Acento_Pela_Palavra_Seguinte(string typed, string want) => Assert.Equal(want, TypeAll(typed));

    // Erro de escrita, não de digitação: x/ch e ss/ç contam como uma troca só; e a forma errada pode estar no corpus
    [Theory]
    [InlineData("enxente", "enchente")]
    [InlineData("xinelo", "chinelo")]
    [InlineData("caxorro", "cachorro")]
    [InlineData("mecher", "mexer")]
    [InlineData("paralização", "paralisação")] // 20 no corpus, contra 177
    public void Troca_Fonetica(string typed, string want) => Assert.Equal(want, _c.Correct(typed)?.Replacement);

    // Letra repetida para dar ênfase fica como foi digitada ("naoooo" virava "namoro"); tripla no meio é tecla presa
    [Theory]
    [InlineData("naoooo")] [InlineData("muitooo")] [InlineData("simmm")] [InlineData("simmmm")] [InlineData("lindooo")] [InlineData("Naoooo")]
    public void Enfase_Fica(string typed) => Assert.Null(_c.Correct(typed));
    [Theory]
    [InlineData("carrro", "carro")] [InlineData("issso", "isso")] [InlineData("compreeendo", "compreendo")] [InlineData("álcoool", "álcool")]
    public void Tecla_Presa_Corrige(string typed, string want) => Assert.Equal(want, _c.Correct(typed)?.Replacement);

    // Locuções da lista à mão (data/locucoes.txt): a vizinha decide o que a estatística não decide
    [Theory]
    [InlineData(" isso ai ", " isso aí ")]
    [InlineData(" e ai, tudo bem? ", " e aí, tudo bem? ")]
    [InlineData(" fica a vontade ", " fica à vontade ")]
    [InlineData(" vou as vezes ", " vou às vezes ")]
    [InlineData(" todas as vezes ", " todas as vezes ")] // exceção de três palavras
    [InlineData(" a viajem foi boa ", " a viagem foi boa ")]
    [InlineData(" disse ai ", " disse ai ")]            // sem locução, "ai" fica
    [InlineData(" moro em sao paulo ", " moro em São Paulo ")] // "sao" já virou "são": a seguinte ainda decide a maiúscula
    [InlineData(" fui a belo horizonte ", " fui a Belo Horizonte ")]
    [InlineData(" mora em salvador ", " mora em Salvador ")]
    [InlineData(" o salvador da pátria ", " o salvador da pátria ")]
    public void Locucoes(string typed, string want) => Assert.Equal(want, TypeAll(typed));

    // A lista embutida manda dai → daí; depois de "bom", é dia. No começo da frase a lista vale ("pao" não vira "Não").
    [Theory]
    [InlineData("bom", "dia")]
    [InlineData("e", "daí")]
    [InlineData(BigramModel.SentenceStart, "daí")]
    public void Lista_Embutida_Cede_Ao_Contexto_Forte(string prev, string want) => Assert.Equal(want, _c.Correct("dai", false, prev)?.Replacement);

    [Fact] public void Lista_Embutida_No_Comeco_Da_Frase() => Assert.Equal("pão", _c.Correct("pao", false, BigramModel.SentenceStart)?.Replacement);

    [Fact]
    public void Acento_Pela_Seguinte_Tab_Desfaz_As_Duas()
    {
        var learned = new List<string>();
        var engine = new TextEngine(_c, learned.Add);
        var doc = new System.Text.StringBuilder(TypeAll(" isso e muito ", engine));
        Assert.Equal(["e"], engine.Suggestions());        // só desfazer; "de"/"se" no lugar não fazem sentido
        var a = engine.AcceptSuggestion("e");
        doc.Length -= a.Backspaces;
        doc.Append(a.Insert);
        Assert.Equal(" isso e muito ", doc.ToString());
        Assert.Empty(learned);                            // "e" não vira palavra pessoal: na próxima frase pode ser "é"
        Assert.Equal("bom ", TypeAll("bom ", engine));    // segue normal, sem corrigir de novo
    }

    // O host anima Insert[..WordLength]: a troca pela seguinte cobre as duas palavras quando a atual também mudou
    [Theory]
    [InlineData(" isso e mutio ", "é muito")]
    [InlineData(" isso e muito ", "é")]
    public void Acento_Pela_Seguinte_Anima_O_Que_Mudou(string typed, string animated)
    {
        var engine = new TextEngine(_c, _ => { });
        var last = EngineAction.None;
        foreach (var ch in typed) if (engine.OnChar(ch) is { Swallow: true } a) last = a;
        Assert.Equal(animated, last.Insert[..last.WordLength]);
    }

    [Fact]
    public void Acento_Pela_Seguinte_Apagado_Nao_Insiste()
    {
        var engine = new TextEngine(_c, _ => { });
        Assert.Equal(" isso é muito ", TypeAll(" isso e muito ", engine));
        TypeAll(new string('\b', 8), engine);           // apaga "é muito ": o motor perde o fio
        engine.ConfirmContext(engine.Generation, " isso "); // o host diz o que ficou antes do cursor
        Assert.Equal("e muito ", TypeAll("e muito ", engine)); // digitou de novo sem acento: fica
        Assert.Equal("e muito ", TypeAll("e muito ", engine)); // e não aprende "e" para sempre: só aquela vez
    }

    [Theory]
    [InlineData("Carraro")] [InlineData("CRCPR")] [InlineData("iPhone")] [InlineData("cpf")] [InlineData("pdf")]
    public void Nomes_Siglas_Ficam(string palavra) => Assert.Null(_c.Correct(palavra));

    // ---- contexto: a palavra anterior decide ----

    [Theory]
    [InlineData("minha", "cas", "casa")]
    [InlineData("esse", "cas", "caso")]
    [InlineData("vou", "corrig", "corrigir")]   // completar pela palavra anterior
    [InlineData("ele", "esta", "está")]          // duas palavras que existem: só o contexto decide
    [InlineData("essa", "e", "é")]               // "e" é tão comum que a frequência geral escondia o contexto
    public void Contexto_Decide(string anterior, string digitada, string certa) =>
        Assert.Equal(certa, _c.Correct(digitada, false, anterior)?.Replacement);

    [Theory]
    [InlineData("que", "esta")]   // "que esta casa" e "que está aqui" são comuns: não mexe
    [InlineData("a", "casa")]
    [InlineData("o", "caso")]
    [InlineData("eu", "e")]
    [InlineData("pai", "e")]
    public void Contexto_NaoEstragaPalavraCerta(string anterior, string digitada) =>
        Assert.Null(_c.Correct(digitada, false, anterior));

    [Fact]
    public void BarraDeEndereco_NaoTrocaNomeDeSite()
    {
        try
        {
            _c.CarefulField = true;
            Assert.Null(_c.Correct("reddit"));
            Assert.Null(_c.Correct("docs"));
            Assert.Equal("errado", _c.Correct("errido")?.Replacement);
            Assert.Equal("não", _c.Correct("nao")?.Replacement);
        }
        finally { _c.CarefulField = false; }
        Assert.Equal("redor", _c.Correct("reddit")?.Replacement); // fora dela, o "corrigir mais" tenta
    }

    [Fact]
    public void SemContexto_EstaFica() => Assert.Null(_c.Correct("esta"));

    [Fact]
    public void Motor_UsaAPalavraAnterior()
    {
        var engine = new TextEngine(_c, _ => { });
        var doc = new System.Text.StringBuilder();
        foreach (var k in " ele esta feliz com minha cas e esse cas ")
        {
            var a = engine.OnChar(k);
            if (a.Swallow) { doc.Length -= a.Backspaces; doc.Append(a.Insert); } else doc.Append(k);
        }
        Assert.Equal(" ele está feliz com minha casa e esse caso ", doc.ToString());
    }

    // ---- sugestões (barra do celular) ----

    [Fact]
    public void Sugere_CompletarPalavra() => Assert.Equal("obrigado", _c.Suggester.Suggest(null, "obrig")[0]);

    [Fact]
    public void Sugere_ComContexto() => Assert.Contains("corrigir", _c.Suggester.Suggest("vou", "corrig"));

    [Fact]
    public void Sugere_ProximaPalavra() => Assert.Contains("dia", _c.Suggester.Suggest("bom", ""));

    [Fact]
    public void Sugere_ComErroNoComeco() => Assert.Contains("obrigado", _c.Suggester.Suggest(null, "obrgi"));

    [Fact]
    public void Sugere_NadaComUmaLetraOuNoComecoDaFrase()
    {
        Assert.Empty(_c.Suggester.Suggest(null, "o"));
        Assert.Empty(_c.Suggester.Suggest(BigramModel.SentenceStart, ""));
        Assert.Empty(_c.Suggester.Suggest(null, ""));
    }

    [Fact]
    public void Motor_AceitaSugestao()
    {
        var engine = new TextEngine(_c, _ => { });
        var doc = new System.Text.StringBuilder();
        void Type(string keys)
        {
            foreach (var k in keys)
            {
                var a = engine.OnChar(k);
                if (a.Swallow) { doc.Length -= a.Backspaces; doc.Append(a.Insert); } else doc.Append(k);
            }
        }
        Type(" muito obrig");
        Assert.Equal("obrigado", engine.Suggestions()[0]);
        var accept = engine.AcceptSuggestion(engine.Suggestions()[0]);
        doc.Length -= accept.Backspaces;
        doc.Append(accept.Insert);
        Assert.Equal(" muito obrigado ", doc.ToString());
        Type("pela ajuda ");
        Assert.Equal(" muito obrigado pela ajuda ", doc.ToString());
    }

    [Fact]
    public void Motor_SugestaoComMaiuscula()
    {
        var engine = new TextEngine(_c, _ => { });
        foreach (var k in " Ok. Obrig") engine.OnChar(k);
        Assert.Equal("Obrigado", engine.Suggestions()[0]);
    }

    [Fact]
    public void Motor_SemContexto_NaoSugere()
    {
        var engine = new TextEngine(_c, _ => { });
        foreach (var k in "obrig") engine.OnChar(k); // começo desconhecido (sem espaço antes)
        Assert.Empty(engine.Suggestions());
    }

    [Fact]
    public void PalavrasValidasFrequentes_NuncaMudam()
    {
        var wrong = _data.Frequency.Entries
            .Where(e => e.Word.All(char.IsLetter) && _data.Lexicon.IsValid(e.Word))
            .OrderByDescending(e => e.Count).Take(30000)
            // maiúscula de nome é de propósito; forma válida rara ganhar acento (chegara → chegará) também, abaixo do piso da regra
            .Where(e => _c.Correct(e.Word) is { Kind: not (CorrectionKind.Fixed or CorrectionKind.Case) } and not { Kind: CorrectionKind.Accent } || (_c.Correct(e.Word) is { Kind: CorrectionKind.Accent } && e.Count > TypoOptions.Aggressive.RareAccentMaxCount))
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
        Assert.True(changed <= 2000, $"{changed} palavras frequentes trocadas (medido em 2026-11-12: 1636)");
    }
}

public class BigramModelTests
{
    [Fact]
    public void Carrega_E_Consulta()
    {
        var path = Path.Combine(SharedData.NewUserDir(), "bigramas.txt");
        File.WriteAllText(path, "=minha 100\ncasa 40\nvida 30\n=<s> 50\no 20\n");
        var m = new BigramModel(path);
        Assert.Equal(100, m.ContextTotal("minha"));
        Assert.Equal(40, m.Count("minha", "casa"));
        Assert.Equal(0, m.Count("minha", "caso"));
        Assert.Equal(0, m.ContextTotal("desconhecida"));
        Assert.Equal(20, m.Count(BigramModel.SentenceStart, "o"));
        Assert.Equal(["casa", "vida"], m.Successors("minha", 10));
    }

    [Fact]
    public void Tokenize_QuebraEmPontuacao()
    {
        var tokens = BigramModel.Tokenize("12\tOlá, mundo. Tudo bem?").ToList();
        Assert.Equal(["olá", null, "mundo", BigramModel.SentenceStart, "tudo", "bem", BigramModel.SentenceStart], tokens);
    }
}

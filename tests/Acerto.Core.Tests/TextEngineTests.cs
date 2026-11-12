using System.Text;

namespace Acerto.Core.Tests;

public class TextEngineTests : IClassFixture<SharedCorrector>
{
    private readonly SharedCorrector _s;
    private readonly TextEngine _engine;
    private readonly StringBuilder _doc = new();
    private readonly List<string> _learned = [];

    public TextEngineTests(SharedCorrector s)
    {
        _s = s;
        _engine = new TextEngine(s.Corrector, _learned.Add);
    }

    /// <summary>Simula o app de destino: tecla não engolida entra no texto; engolida aplica Backspaces + Insert.</summary>
    private string Type(string keys)
    {
        foreach (var k in keys)
        {
            var a = k == '\b' ? _engine.OnBackspace() : _engine.OnChar(k);
            if (a.Swallow)
            {
                _doc.Length -= a.Backspaces;
                _doc.Append(a.Insert);
            }
            else if (k == '\b') { if (_doc.Length > 0) _doc.Length--; }
            else _doc.Append(k);
        }
        return _doc.ToString();
    }

    [Fact] public void Corrige_No_Espaco() => Assert.Equal(" não ", Type(" nao "));
    [Fact] public void Corrige_Na_Pontuacao() => Assert.Equal(" você?", Type(" voce?"));
    [Fact] public void Corrige_Entre_Parenteses() => Assert.Equal(" (não)", Type(" (nao)"));

    [Fact]
    public void Backspace_Desfaz_E_Aprende_Ao_Continuar()
    {
        Assert.Equal(" oque ", Type(" oque \b"));
        Assert.Empty(_learned); // ainda pode ser Backspace segurado
        Type("x");
        Assert.Equal(["oque"], _learned);
    }

    [Fact]
    public void Backspace_Segurado_Nao_Aprende() // I4 da revisão final
    {
        Assert.Equal(" oqu", Type(" oque \b\b\b"));
        Type("x ");
        Assert.Empty(_learned);
    }

    [Fact]
    public void Desfazer_Atalho_Nao_Aprende()
    {
        Assert.Equal(" vlw x", Type(" vlw \bx"));
        Assert.Empty(_learned);
    }

    [Fact] public void Desfazer_So_Logo_Depois() => Assert.Equal(" não ", Type(" nao x\b"));
    [Fact] public void Enter_Nao_Gera_Undo() => Assert.Equal(" não", Type(" nao\n\b"));
    [Fact] public void Maiuscula_Apos_Ponto() => Assert.Equal(" Ok. Não ", Type(" Ok. nao "));
    [Fact] public void Maiuscula_Apos_Enter() => Assert.Equal(" ok\nCasa ", Type(" ok\ncasa "));
    [Fact] public void Espaco_Duplo() => Assert.Equal(" Fim. ", Type(" Fim  "));
    [Fact] public void Espaco_Duplo_Desfaz() => Assert.Equal(" Fim  ", Type(" Fim  \b"));
    [Fact] public void Espaco_Triplo_Nao_Repete() => Assert.Equal(" Fim.  ", Type(" Fim   "));

    [Fact]
    public void Engine_ResetMidWord_NoCorrection()
    {
        Type(" informa");
        _engine.Reset();
        Assert.Equal(" informacao ", Type("cao "));
    }

    [Theory]
    [InlineData(" www.site.com ")]
    [InlineData(" joao@crc.org.br ")]
    [InlineData(@" C:\pasta\nao ")]
    [InlineData(" abc123 ")]
    public void Engine_ImpureToken_NoCorrection(string s) => Assert.Equal(s, Type(s));

    [Fact] public void Sem_Maiuscula_No_Inicio_Desconhecido() => Assert.Equal(" casa ", Type(" casa "));
    [Fact] public void Inicio_Desconhecido_Nao_Corrige() => Assert.Equal("nao ", Type("nao "));
    [Fact] public void Backspace_No_Meio_Da_Palavra() => Assert.Equal(" não ", Type(" nai\bo "));
}

public class TextEngineEnderecoTests(SharedCorrector s) : IClassFixture<SharedCorrector>
{
    private readonly TextEngine _engine = new(s.Corrector, _ => { });
    private readonly StringBuilder _doc = new();

    private string Type(string keys)
    {
        foreach (var k in keys)
        {
            var a = k == '\b' ? _engine.OnBackspace() : _engine.OnChar(k);
            if (a.Swallow) { _doc.Length -= a.Backspaces; _doc.Append(a.Insert); }
            else if (k == '\b') { if (_doc.Length > 0) _doc.Length--; }
            else _doc.Append(k);
        }
        return _doc.ToString();
    }

    // C2 da revisão final: o que vem antes de "." ou ":" só é corrigido se depois vier espaço/Enter
    [Theory]
    [InlineData(" joao.silva@crc.org.br ")]
    [InlineData(" informacao.gov.br ")]
    [InlineData(" relatorio.pdf ")]
    [InlineData(" nao:x ")]
    public void Endereco_Nao_Corrige(string s) => Assert.Equal(s, Type(s));

    [Fact] public void Ponto_Depois_Espaco_Corrige() => Assert.Equal(" não. ", Type(" nao. "));
    [Fact] public void Ponto_Depois_Enter_Corrige() => Assert.Equal(" não.\n", Type(" nao.\n"));
    [Fact] public void DoisPontos_Depois_Espaco_Corrige() => Assert.Equal(" não: ", Type(" nao: "));
    [Fact] public void Ponto_Corrigido_Desfaz() => Assert.Equal(" nao. ", Type(" nao. \b"));
    [Fact] public void Ponto_Apagado_Nao_Corrige() => Assert.Equal(" nao ", Type(" nao.\b "));
    [Fact] public void Ponto_Depois_Maiuscula() => Assert.Equal(" Ok. Não. ", Type(" Ok. nao. "));

    // I5 da revisão final: Tab costuma aceitar sugestão do app; não corrigir
    [Fact] public void Tab_Nao_Corrige() => Assert.Equal(" nao\t", Type(" nao\t"));
}

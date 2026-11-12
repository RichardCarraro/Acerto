using System.Text;

namespace Acerto.Core.Tests;

public class TextEngineTests : IClassFixture<SharedCorrector>
{
    private readonly SharedCorrector _s;
    private readonly TextEngine _engine;
    private readonly StringBuilder _doc = new();

    public TextEngineTests(SharedCorrector s)
    {
        _s = s;
        _engine = new TextEngine(s.Corrector, s.Data.Personal);
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
    public void Backspace_Desfaz_E_Aprende()
    {
        Assert.Equal(" oque ", Type(" oque \b"));
        Assert.True(_s.Data.Personal.Contains("oque"));
        _s.Data.Personal.Remove("oque");
    }

    [Fact]
    public void Desfazer_Atalho_Nao_Aprende()
    {
        Assert.Equal(" vlw ", Type(" vlw \b"));
        Assert.False(_s.Data.Personal.Contains("vlw"));
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

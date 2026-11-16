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

    // Backspace depois de uma correção só apaga, como em qualquer texto (pedido do usuário: sem "desfazer").
    [Fact] public void Backspace_Depois_De_Correcao_So_Apaga() => Assert.Equal(" não", Type(" nao \b"));

    private string Accept(string suggestion)
    {
        var a = _engine.AcceptSuggestion(suggestion);
        _doc.Length -= a.Backspaces;
        _doc.Append(a.Insert);
        return _doc.ToString();
    }

    [Fact]
    public void Depois_Da_Correcao_Oferece_O_Digitado_E_Aprende()
    {
        Type(" nao ");
        Assert.Equal("nao", _engine.Suggestions()[0]);
        Assert.Equal(" nao ", Accept("nao"));
        Assert.Contains("nao", _learned);
        Assert.Equal(" nao casa ", Type("casa "));
    }

    [Fact]
    public void Depois_Da_Correcao_Troca_Por_Alternativa()
    {
        Assert.Equal(" Ok. Não ", Type(" Ok. nao "));
        var alts = _engine.Suggestions();
        Assert.True(alts.Count > 1, string.Join(",", alts));
        Assert.Equal("nao", alts[0]);
        Assert.True(char.IsUpper(alts[1][0]), alts[1]); // começo de frase, como a correção
        Assert.Equal(" Ok. " + alts[1] + " ", Accept(alts[1]));
        Assert.Empty(_learned);
    }

    [Fact]
    public void Alternativas_Somem_Ao_Digitar()
    {
        Type(" nao c");
        Assert.DoesNotContain("nao", _engine.Suggestions());
    }
    [Fact] public void Backspace_Depois_De_Atalho_So_Apaga() => Assert.Equal(" valeu", Type(" vlw \b"));
    [Fact] public void Backspace_Depois_De_Enter_So_Apaga() => Assert.Equal(" não", Type(" nao\n\b"));

    [Fact]
    public void Redigitar_A_Mesma_Palavra_Mantem_E_Aprende() // apagou a correção e escreveu de novo: é de propósito
    {
        Assert.Equal(" ", Type(" oque \b\b\b\b\b\b"));
        _engine.ConfirmContext(_engine.Generation, "oi "); // o host confirma: há um espaço antes do cursor
        Assert.Equal(" oque ", Type("oque "));
        Assert.Equal(["oque"], _learned);
    }

    [Fact]
    public void Palavra_Diferente_Depois_De_Apagar_Corrige_Normal()
    {
        Type(" oque \b\b\b\b\b\b");
        _engine.ConfirmContext(_engine.Generation, "oi ");
        Assert.Equal(" não ", Type("nao "));
        Assert.Empty(_learned);
    }

    [Fact]
    public void Clique_Esquece_A_Palavra_Rejeitada()
    {
        Type(" oque ");
        _engine.Reset(); // clicou em outro lugar
        _engine.ConfirmContext(_engine.Generation, "oi ");
        Assert.Equal(" o que o que ", Type("oque "));
    }
    [Fact] public void Maiuscula_Apos_Ponto() => Assert.Equal(" Ok. Não ", Type(" Ok. nao "));
    [Fact] public void Maiuscula_Nao_Estraga_CaixaMista() => Assert.Equal(" Ok. iPhone ", Type(" Ok. iPhone ")); // m1 da revisão
    // Quem digita a maiúscula do começo da frase também erra: ali não é nome, pode corrigir.
    [Fact] public void ErroComMaiuscula_NoComecoDaFrase_Corrige() => Assert.Equal(" Ok. Corrigir ", Type(" Ok. Corigir "));
    [Fact] public void ErroComMaiuscula_NoMeioDaFrase_Fica() => Assert.Equal(" o Corigir ", Type(" o Corigir "));
    [Fact] public void Maiuscula_Apos_Enter() => Assert.Equal(" ok\nCasa ", Type(" ok\ncasa "));
    [Fact] public void Espaco_Duplo() => Assert.Equal(" Fim. ", Type(" Fim  "));
    [Fact] public void Espaco_Duplo_Backspace_So_Apaga() => Assert.Equal(" Fim.", Type(" Fim  \b"));
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
    [Fact] public void Ponto_Corrigido_Backspace_So_Apaga() => Assert.Equal(" não.", Type(" nao. \b"));
    [Fact] public void Ponto_Apagado_Nao_Corrige() => Assert.Equal(" nao ", Type(" nao.\b "));
    [Fact] public void Ponto_Depois_Maiuscula() => Assert.Equal(" Ok. Não. ", Type(" Ok. nao. "));
    [Fact] public void Reticencias_Nao_Dao_Maiuscula() => Assert.Equal(" certo... não pensei ", Type(" certo... nao pensei "));

    // I5 da revisão final: Tab costuma aceitar sugestão do app; não corrigir
    [Fact] public void Tab_Nao_Corrige() => Assert.Equal(" nao\t", Type(" nao\t"));
}

/// <summary>Depois de um Reset, o host pode confirmar o que havia antes do cursor (UI Automation).</summary>
public class TextEngineContextTests(SharedCorrector s) : IClassFixture<SharedCorrector>
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

    [Fact]
    public void CampoVazio_CorrigePrimeiraPalavra_ComMaiuscula()
    {
        _engine.ConfirmContext(_engine.Generation, "");
        Assert.Equal("Teste ", Type("testi "));
    }

    [Fact]
    public void EspacoAntes_CorrigePrimeiraPalavra()
    {
        _engine.ConfirmContext(_engine.Generation, "bom dia ");
        Assert.Equal("não ", Type("nao "));
    }

    [Fact]
    public void ConfirmacaoDepoisDeDigitar_Funciona() // a resposta da UIA chega com letras já digitadas
    {
        Type("na");
        _engine.ConfirmContext(_engine.Generation, "oi na");
        Assert.Equal("não ", Type("o "));
    }

    [Fact]
    public void LetraAntes_NaoCorrige() // cursor no meio de "informa|"
    {
        _engine.ConfirmContext(_engine.Generation, "informa");
        Assert.Equal("cao ", Type("cao "));
    }

    [Fact]
    public void TextoNaoBate_NaoCorrige() // a UIA viu outra coisa: não confiar
    {
        Type("na");
        _engine.ConfirmContext(_engine.Generation, "oi xy");
        Assert.Equal("nao ", Type("o "));
    }

    [Fact]
    public void GeracaoVelha_Ignorada() // resposta de antes de outro clique
    {
        var old = _engine.Generation;
        _engine.Reset();
        _engine.ConfirmContext(old, "");
        Assert.Equal("nao ", Type("nao "));
    }

    [Fact]
    public void DepoisDePonto_Maiuscula()
    {
        _engine.ConfirmContext(_engine.Generation, "Tudo certo. ");
        Assert.Equal("Não ", Type("nao "));
    }
}

/// <summary>Opções que o usuário pode desligar nas configurações.</summary>
public class TextEngineOptionsTests(SharedCorrector s) : IClassFixture<SharedCorrector>
{
    private string Type(TextEngine engine, string keys)
    {
        var doc = new StringBuilder();
        foreach (var k in keys)
        {
            var a = engine.OnChar(k);
            if (a.Swallow) { doc.Length -= a.Backspaces; doc.Append(a.Insert); }
            else doc.Append(k);
        }
        return doc.ToString();
    }

    [Fact]
    public void SemMaiusculaAutomatica() =>
        Assert.Equal(" Ok. não ", Type(new TextEngine(s.Corrector, _ => { }) { AutoCapitalize = false }, " Ok. nao "));

    [Fact]
    public void SemEspacoDuplo() =>
        Assert.Equal(" Fim  ", Type(new TextEngine(s.Corrector, _ => { }) { DoubleSpacePeriod = false }, " Fim  "));
}

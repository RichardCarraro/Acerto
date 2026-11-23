namespace Acerto.Core.Tests;

public class UserHistoryTests(SharedCorrector s) : IClassFixture<SharedCorrector>
{
    /// <summary>Corretor com os dados pesados compartilhados e um histórico só dele, em memória.</summary>
    private Corrector NewCorrector(UserHistory history) => new(new CoreData
    {
        Lexicon = s.Data.Lexicon, Frequency = s.Data.Frequency, Fixed = s.Data.Fixed, Personal = s.Data.Personal,
        Shortcuts = s.Data.Shortcuts, Protected = s.Data.Protected, Bigrams = s.Data.Bigrams, History = history,
    });

    [Fact]
    public void GravaELe()
    {
        var path = Path.Combine(SharedData.NewUserDir(), "historico.txt");
        var h = new UserHistory(path);
        h.Record(null, "casa");
        h.Record("minha", "casa");
        h.Save();
        Assert.False(h.Dirty);
        var back = new UserHistory(path);
        Assert.Equal(2, back.Count("casa"));
        Assert.Equal(1, back.PairCount("minha", "casa"));
    }

    [Fact]
    public void GravaDepois_ComACopiaDaHora()
    {
        var path = Path.Combine(SharedData.NewUserDir(), "historico.txt");
        var h = new UserHistory(path);
        h.Record("minha", "casa");
        var write = h.SaveLater();
        Assert.False(h.Dirty);
        h.Clear(); // "Apagar histórico" depois da cópia: a gravação leva o que havia na hora
        h.Record(null, "senha");
        write();
        var back = new UserHistory(path);
        Assert.Equal(1, back.Count("casa"));
        Assert.Equal(0, back.Count("senha"));
        Assert.True(h.Dirty); // o que mudou depois ainda vai para o disco na próxima
    }

    [Fact]
    public void PalavraUsada_SobeNaSugestao()
    {
        var history = new UserHistory(null);
        var c = NewCorrector(history);
        var before = c.Suggester.Suggest(null, "conv", 10);
        var target = before[^1]; // a menos provável da lista
        for (int i = 0; i < 5; i++) c.Record(null, target);
        Assert.Equal(target, c.Suggester.Suggest(null, "conv")[0]);
    }

    [Fact]
    public void ProximaPalavra_UsaOHistorico()
    {
        var c = NewCorrector(new UserHistory(null));
        for (int i = 0; i < 5; i++) c.Record("bom", "trabalho");
        Assert.Equal("trabalho", c.Suggester.Suggest("bom", "")[0]);
    }

    [Fact]
    public void NaoGuarda_ForaDoDicionario_NemDesligado()
    {
        var history = new UserHistory(null);
        var c = NewCorrector(history);
        c.Record(null, "senha123");
        c.Record(null, "xptoqwz");
        Assert.True(history.IsEmpty);
        c.LearnFromTyping = false;
        c.Record(null, "casa");
        Assert.True(history.IsEmpty);
    }

    [Fact]
    public void Motor_GuardaOQueFicouNoTexto()
    {
        var history = new UserHistory(null);
        var engine = new TextEngine(NewCorrector(history), _ => { });
        foreach (var k in " minha casa nao ") engine.OnChar(k);
        Assert.Equal(1, history.PairCount("minha", "casa"));
        Assert.Equal(1, history.Count("não"));
        Assert.Equal(0, history.Count("nao"));
    }
}

using System.Diagnostics;

namespace Acerto.Core.Tests;

public sealed class SharedRanker
{
    public TypoRanker Ranker { get; }
    public SharedRanker()
    {
        var d = new SharedData();
        Ranker = new TypoRanker(d.Lexicon, d.Frequency);
    }
}

/// <summary>Medem tempo: rodam sozinhas, depois das outras (montar o corretor usa todos os núcleos e falseia o p99).</summary>
[CollectionDefinition("Tempo", DisableParallelization = true)]
public sealed class TempoCollection;

[Collection("Tempo")]
public class TypoRankerTests(SharedRanker s) : IClassFixture<SharedRanker>
{
    [Theory]
    [InlineData("qeu", "que")]
    [InlineData("poblema", "problema")]
    [InlineData("porbelma", "problema")]
    [InlineData("trabalo", "trabalho")]
    [InlineData("obrigaod", "obrigado")]
    public void Corrige(string i, string o) => Assert.Equal(o, s.Ranker.Best(i)?.Word);

    [Theory]
    [InlineData("xkzq")]
    [InlineData("www")]
    [InlineData("cpf")]
    public void Desiste(string i) => Assert.Null(s.Ranker.Best(i));

    [Fact]
    public void Rapido()
    {
        string[] words = ["informacaoo", "probelma", "trabalhandoo", "desenvolvimentoo", "obrigaod", "xkzqwvbnmasd",
                          "responsabilidadee", "configuracaoo", "excepcionalmentee", "qeu", "dinheior", "anticonstitucionalmentx"];
        foreach (var w in words) s.Ranker.Best(w); // aquece
        var times = new List<double>();
        for (int i = 0; i < 1000; i++)
        {
            var sw = Stopwatch.StartNew();
            s.Ranker.Best(words[i % words.Length]);
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        var p99 = times[(int)(times.Count * 0.99)];
        Assert.True(p99 <= 15, $"p99 = {p99:F2} ms");
    }
}

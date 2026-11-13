using Acerto.Core;

namespace Acerto.Core.Tests;

public class TextUtilTests
{
    [Theory]
    [InlineData("informação", "informacao")]
    [InlineData("Ção", "Cao")]
    [InlineData("pé", "pe")]
    public void StripAccents(string i, string o) => Assert.Equal(o, TextUtil.StripAccents(i));

    [Theory]
    [InlineData("nao", "não", "não")]
    [InlineData("Nao", "não", "Não")]
    [InlineData("NAO", "não", "NÃO")]
    [InlineData("Oque", "o que", "O que")]
    public void ApplyCase(string typed, string rep, string expected) => Assert.Equal(expected, TextUtil.ApplyCase(typed, rep));

    [Theory]
    [InlineData("NAO", true)]
    [InlineData("Nao", false)]
    [InlineData("A", false)]
    public void IsAllCaps(string s, bool expected) => Assert.Equal(expected, TextUtil.IsAllCaps(s));

    [Theory]
    [InlineData("iPhone", true)]
    [InlineData("McDonald", true)]
    [InlineData("NAO", false)]
    [InlineData("Nao", false)]
    public void IsMixedCase(string s, bool expected) => Assert.Equal(expected, TextUtil.IsMixedCase(s));
}

public class DeadKeysTests
{
    [Theory]
    [InlineData('´', 'a', "á")]
    [InlineData('~', 'a', "ã")]
    [InlineData('^', 'e', "ê")]
    [InlineData('`', 'a', "à")]
    [InlineData('¨', 'u', "ü")]
    [InlineData('´', 'C', "Ć")]
    [InlineData('´', ' ', "´")]
    [InlineData('~', 'x', "~x")]
    // O app (Chrome) já aplicou a tecla morta antes de o hook traduzir a letra: vem pronta, não compor de novo.
    [InlineData('~', 'ã', "ã")]
    [InlineData('´', 'É', "É")]
    [InlineData('^', 'ô', "ô")]
    public void Compose(char d, char b, string o) => Assert.Equal(o, DeadKeys.Compose(d, b));
}

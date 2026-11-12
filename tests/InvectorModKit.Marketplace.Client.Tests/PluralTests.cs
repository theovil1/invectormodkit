using InvectorModKit.Marketplace.Client;

namespace InvectorModKit.Marketplace.Client.Tests;

/// <summary>Which form a counted text takes ("1 install" / "2 installs").</summary>
public class PluralTests
{
    [Theory]
    [InlineData("en", 1, true)]
    [InlineData("en", 0, false)]
    [InlineData("en", 2, false)]
    [InlineData("de", 1, true)]
    [InlineData("pt-BR", 0, false)]
    public void Plural_OneIsSingular(string language, int n, bool one) => Assert.Equal(one, Plural.IsOne(n, language));

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void Plural_FrenchZeroIsSingular(int n, bool one) => Assert.Equal(one, Plural.IsOne(n, "fr"));
}

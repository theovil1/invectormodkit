using InvectorModKit.Marketplace.Client;

namespace InvectorModKit.Marketplace.Client.Tests;

/// <summary>A chart's content, blind to the blocks that say where it comes from (the server drops them too).</summary>
public class ChartContentTests
{
    private const string Chart = """{"format":1,"bpm":120,"notes":{"Casual":[{"t":6},{"t":6.5}]}}""";

    [Fact]
    public void Hash_IgnoresSourceAndGenerator()
    {
        string tagged = """{"format":1,"generator":{"name":"ChartGen","version":3},"bpm":120,"notes":{"Casual":[{"t":6},{"t":6.5}]},"source":{"marketplaceId":"x","version":2}}""";
        Assert.Equal(ChartContent.Hash(Chart), ChartContent.Hash(tagged));
    }

    [Fact]
    public void Hash_ChangesWithANote() =>
        Assert.NotEqual(ChartContent.Hash(Chart), ChartContent.Hash(Chart.Replace("6.5", "6.75")));

    [Fact]
    public void Hash_IsLowercaseHex() =>
        Assert.Matches("^[0-9a-f]{64}$", ChartContent.Hash(Chart));

    [Fact]
    public void Hash_NullForNonObject()
    {
        Assert.Null(ChartContent.Hash("[]"));
        Assert.Null(ChartContent.Hash("not json"));
        Assert.Null(ChartContent.Hash(null));
    }
}

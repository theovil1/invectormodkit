using InvectorModKit.Marketplace.Client;

namespace InvectorModKit.Marketplace.Client.Tests;

/// <summary>Where a chart's excerpt starts and ends in its song.</summary>
public class ExcerptWindowTests
{
    [Fact]
    public void Window_StartsAtThePreviewTime()
    {
        var w = ExcerptWindow.For(61.5, 240);
        Assert.Equal(61.5, w.Start);
        Assert.Equal(81.5, w.End);
    }

    [Fact]
    public void Window_MissingPreviewIsThirtyPercent()
    {
        Assert.Equal(60, ExcerptWindow.For(null, 200).Start);
        Assert.Equal(60, ExcerptWindow.For(-3, 200).Start);
        Assert.Equal(60, ExcerptWindow.For(250, 200).Start);
    }

    [Fact]
    public void Window_NearTheEndStaysInsideTheSong()
    {
        var w = ExcerptWindow.For(190, 200);
        Assert.Equal(180, w.Start);
        Assert.Equal(200, w.End);
    }

    [Fact]
    public void Window_ShortSongIsWholeSong()
    {
        var w = ExcerptWindow.For(5, 12);
        Assert.Equal(0, w.Start);
        Assert.Equal(12, w.End);
    }
}

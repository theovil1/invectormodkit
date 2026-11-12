using System.Linq;
using InvectorModKit.Charting;

namespace InvectorModKit.Charting.Tests;

/// <summary>The cover drawn for a song without one: a white pixel note on blocks in the song's colours.</summary>
public class CoverArtTests
{
    /// <summary>The pixel at (x, y), y counted from the top.</summary>
    private static (byte r, byte g, byte b, byte a) At(byte[] rgba, int x, int y)
    {
        int i = ((CoverArt.Size - 1 - y) * CoverArt.Size + x) * 4;
        return (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]);
    }

    [Fact]
    public void Cover_IsSquareAndOpaque()
    {
        var rgba = CoverArt.Rgba32("Hope");
        Assert.Equal(CoverArt.Size * CoverArt.Size * 4, rgba.Length);
        Assert.All(Enumerable.Range(0, CoverArt.Size * CoverArt.Size), i => Assert.Equal(255, rgba[i * 4 + 3]));
    }

    [Fact]
    public void Cover_SameSongSameCover() => Assert.Equal(CoverArt.Rgba32("Hope"), CoverArt.Rgba32("Hope"));

    [Fact]
    public void Cover_OtherSongOtherColours() => Assert.NotEqual(At(CoverArt.Rgba32("Hope"), 0, 0), At(CoverArt.Rgba32("Enter Sandman"), 0, 0));

    [Fact]
    public void Cover_WhiteNoteOnTheBlocks()
    {
        var rgba = CoverArt.Rgba32("Hope");
        Assert.Equal((255, 255, 255, 255), At(rgba, 12, 4)); // the beam's left end, on top
        Assert.NotEqual((255, 255, 255, 255), At(rgba, 0, 0)); // a corner: the blocks
        Assert.NotEqual((255, 255, 255, 255), At(rgba, 31, 31));
    }

    [Fact]
    public void Cover_NoKeyStillDraws() => Assert.Equal(CoverArt.Size * CoverArt.Size * 4, CoverArt.Rgba32(null).Length);
}

using Newtonsoft.Json.Linq;

namespace InvectorModKit.ChartEditor.Tests;

public class CacheFilesTests
{
    [Theory]
    [InlineData("Pickup Strum, List/Bounce OPTIMIZED", "Pickup_Strum__List_Bounce_OPTIMIZED")]
    [InlineData("coaster@RollRightLong2Turns", "coaster_RollRightLong2Turns")]
    [InlineData("void", "void")]
    [InlineData("assets_shared--3209297936361821700", "assets_shared--3209297936361821700")]
    public void FileSafe(string name, string expected) => Assert.Equal(expected, CacheFiles.FileSafe(name));

    [Theory]
    [InlineData("a")]
    [InlineData("ab")]
    [InlineData("abc")]
    [InlineData("abcd")]
    public void HeaderLineIsPaddedToFourBytes(string key)
    {
        var header = new JObject { [key] = 1 };
        using var stream = new MemoryStream();
        CacheFiles.WriteHeaderLine(stream, header);
        stream.WriteByte(0xAB); // what follows the header
        byte[] bytes = stream.ToArray();
        int headerLength = bytes.Length - 1;

        Assert.Equal(0, headerLength % 4);
        Assert.Equal((byte)'\n', bytes[headerLength - 1]);

        stream.Position = 0;
        var read = CacheFiles.ReadHeaderLine(stream);
        Assert.True(JToken.DeepEquals(header, read));
        Assert.Equal(headerLength, stream.Position);
        Assert.Equal(0xAB, stream.ReadByte());
    }
}

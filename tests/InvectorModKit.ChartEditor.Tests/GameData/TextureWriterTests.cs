using Newtonsoft.Json.Linq;

namespace InvectorModKit.ChartEditor.Tests;

public class TextureWriterTests
{
    [Fact]
    public void TextureHeaderAndBytes()
    {
        var bytes = Enumerable.Range(0, 40).Select(i => (byte)i).ToArray();
        var texture = new TextureData { Format = 10, Width = 8, Height = 4, MipCount = 2, ColorSpace = 1, Filter = 1, WrapU = 0, WrapV = 1, Bytes = bytes };
        using var stream = new MemoryStream();
        TextureWriter.Write(texture, stream);

        stream.Position = 0;
        var header = CacheFiles.ReadHeaderLine(stream);
        Assert.Equal(10, (int)header["format"]);
        Assert.Equal(8, (int)header["width"]);
        Assert.Equal(4, (int)header["height"]);
        Assert.Equal(2, (int)header["mips"]);
        Assert.Equal(1, (int)header["colorSpace"]);
        Assert.Equal(1, (int)header["filter"]);
        Assert.True(JToken.DeepEquals(new JArray(0, 1), header["wrap"]));

        var rest = new byte[stream.Length - stream.Position];
        stream.Read(rest, 0, rest.Length);
        Assert.Equal(bytes, rest);
    }

    [Fact]
    public void SupportedAndCrunchedFormats()
    {
        Assert.Equal(new[] { 1, 3, 4, 10, 12, 17, 24, 25 }, TextureWriter.Supported.OrderBy(f => f)); // 17: RGBAHalf
        foreach (int f in new[] { 28, 29, 64, 65 }) Assert.True(TextureWriter.IsCrunched(f), f.ToString());
        Assert.False(TextureWriter.IsCrunched(10));
    }
}

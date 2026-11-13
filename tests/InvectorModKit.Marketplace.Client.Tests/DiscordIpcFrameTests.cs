using System.Text;
using InvectorModKit.Marketplace.Client.Discord;

namespace InvectorModKit.Marketplace.Client.Tests;

/// <summary>The frames exchanged with the Discord client over its local pipe.</summary>
public class DiscordIpcFrameTests
{
    [Fact]
    public void Encode_WritesOpcodeLengthAndUtf8()
    {
        var bytes = IpcFrame.Encode(IpcOpcode.Frame, "{\"a\":\"é\"}");
        Assert.Equal(new byte[] { 1, 0, 0, 0, 10, 0, 0, 0 }, bytes.Take(8));
        Assert.Equal("{\"a\":\"é\"}", Encoding.UTF8.GetString(bytes, 8, 10));
    }

    [Fact]
    public void TryDecode_RoundTrips()
    {
        var bytes = IpcFrame.Encode(IpcOpcode.Handshake, "{\"v\":1}");
        Assert.True(IpcFrame.TryDecode(bytes, bytes.Length, out var op, out var json, out int consumed));
        Assert.Equal(IpcOpcode.Handshake, op);
        Assert.Equal("{\"v\":1}", json);
        Assert.Equal(bytes.Length, consumed);
    }

    [Fact]
    public void TryDecode_PartialFrame_ReturnsFalse()
    {
        var bytes = IpcFrame.Encode(IpcOpcode.Frame, "0123456789");
        Assert.False(IpcFrame.TryDecode(bytes, 11, out _, out _, out int consumed));
        Assert.Equal(0, consumed);
    }

    [Fact]
    public void TryDecode_TwoFrames_ConsumesFirstOnly()
    {
        var first = IpcFrame.Encode(IpcOpcode.Ping, "{\"x\":1}");
        var both = first.Concat(IpcFrame.Encode(IpcOpcode.Frame, "{}")).ToArray();
        Assert.True(IpcFrame.TryDecode(both, both.Length, out var op, out var json, out int consumed));
        Assert.Equal(IpcOpcode.Ping, op);
        Assert.Equal("{\"x\":1}", json);
        Assert.Equal(first.Length, consumed);
    }
}

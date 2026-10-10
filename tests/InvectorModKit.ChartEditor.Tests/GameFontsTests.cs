using System.Text;
using InvectorModKit.ChartEditor;

namespace InvectorModKit.ChartEditor.Tests;

public class GameFontsTests
{
    [Fact]
    public void Lz4_LiteralsAndOverlappingMatch()
    {
        // "abcabcabcabcX": literals "abc", then a match of 9 bytes at offset 3 (overlapping), then literal "X".
        byte[] block = { 0x35, (byte)'a', (byte)'b', (byte)'c', 3, 0, 0x10, (byte)'X' };
        var output = new byte[13];
        Assert.Equal(13, GameFonts.Lz4Decode(block, 0, block.Length, output, 0, 13));
        Assert.Equal("abcabcabcabcX", Encoding.ASCII.GetString(output));
    }

    [Fact]
    public void CarvesAnOpenTypeFontWithItsPostScriptName()
    {
        byte[] font = FakeFont("Exo2-BoldCondensed");
        var data = new byte[300].Concat(font).Concat(new byte[50]).ToArray();
        var carved = Assert.Single(GameFonts.CarveFonts(data));
        Assert.Equal("Exo2-BoldCondensed", carved.Name);
        Assert.True(carved.OpenType);
        Assert.Equal(font, carved.Bytes);
    }

    /// <summary>An "OTTO" file with 4 tables (name + 3 padding tables), big enough to pass the size check.</summary>
    private static byte[] FakeFont(string postscript)
    {
        var name = new List<byte>();
        byte[] text = Encoding.BigEndianUnicode.GetBytes(postscript);
        void U16(List<byte> l, int v) { l.Add((byte)(v >> 8)); l.Add((byte)v); }
        U16(name, 0); U16(name, 1); U16(name, 6 + 12); // format, count, string offset
        U16(name, 3); U16(name, 1); U16(name, 0x409); U16(name, 6); U16(name, text.Length); U16(name, 0);
        name.AddRange(text);
        var tables = new (string Tag, byte[] Data)[] { ("CFF ", new byte[600]), ("head", new byte[54]), ("name", name.ToArray()), ("post", new byte[400]) };
        var font = new List<byte>(Encoding.ASCII.GetBytes("OTTO"));
        U16(font, tables.Length); U16(font, 0); U16(font, 0); U16(font, 0);
        int offset = 12 + 16 * tables.Length;
        foreach (var (tag, bytes) in tables)
        {
            font.AddRange(Encoding.ASCII.GetBytes(tag));
            font.AddRange(new byte[4]);
            foreach (int v in new[] { offset, bytes.Length }) { font.Add((byte)(v >> 24)); font.Add((byte)(v >> 16)); font.Add((byte)(v >> 8)); font.Add((byte)v); }
            offset += bytes.Length;
        }
        foreach (var (_, bytes) in tables) font.AddRange(bytes);
        return font.ToArray();
    }
}

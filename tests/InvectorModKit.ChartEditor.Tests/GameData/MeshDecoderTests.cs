using Newtonsoft.Json.Linq;

namespace InvectorModKit.ChartEditor.Tests;

public class MeshDecoderTests
{
    [Fact]
    public void Crc32OfTheCheckString() => Assert.Equal(0xCBF43926u, Crc32.Compute("123456789"));

    /// <summary>14 Unity channels, all absent except the ones given as (index, stream, offset, format, dimension).</summary>
    static List<VertexChannel> Channels(params (int index, int stream, int offset, int format, int dimension)[] present)
    {
        var list = Enumerable.Range(0, 14).Select(_ => new VertexChannel()).ToList();
        foreach (var c in present) list[c.index] = new VertexChannel { Stream = c.stream, Offset = c.offset, Format = c.format, Dimension = c.dimension };
        return list;
    }

    static byte[] U16(params ushort[] values) => values.SelectMany(BitConverter.GetBytes).ToArray();
    static byte[] U32(params uint[] values) => values.SelectMany(BitConverter.GetBytes).ToArray();

    /// <summary>
    /// 3 vertices in two streams. Stream 0: position Float32 x3, normal Float16 x3 (stride 18, 54 bytes).
    /// Stream 1, from byte 64 (16-byte aligned): color UNorm8 x4, uv0 Float32 x2 (stride 12).
    /// </summary>
    static MeshData TwoStreamMesh()
    {
        var data = new byte[100];
        float[][] positions = { new[] { 1f, 2f, 3f }, new[] { 4f, 5f, 6f }, new[] { 7f, 8f, 9f } };
        for (int v = 0; v < 3; v++)
        {
            int s0 = v * 18;
            for (int k = 0; k < 3; k++) BitConverter.GetBytes(positions[v][k]).CopyTo(data, s0 + k * 4);
            U16(0x3C00, 0x3800, 0xC000).CopyTo(data, s0 + 12); // (1, 0.5, -2)
            int s1 = 64 + v * 12;
            new byte[] { 255, 0, 51, 255 }.CopyTo(data, s1);
            BitConverter.GetBytes(0.25f).CopyTo(data, s1 + 4);
            BitConverter.GetBytes(0.75f).CopyTo(data, s1 + 8);
        }
        return new MeshData
        {
            VertexCount = 3,
            Channels = Channels((0, 0, 0, 0, 3), (1, 0, 12, 1, 3), (3, 1, 0, 2, 4), (4, 1, 4, 0, 2)),
            VertexBytes = data,
            IndexBytes = U16(0, 1, 2, 2, 1, 0),
            Index32 = false,
            SubMeshes = new List<SubMeshData>
            {
                new SubMeshData { FirstByte = 0, IndexCount = 3 },
                new SubMeshData { FirstByte = 6, IndexCount = 3 },
            },
            BoundsCenter = new[] { 4f, 5f, 6f },
            BoundsExtent = new[] { 3f, 3f, 3f },
        };
    }

    static float[] Attribute(DecodedMesh mesh, string name) => mesh.Attributes.Single(a => a.Key == name).Value;

    [Fact]
    public void TwoStreamsWithFloatHalfAndUnorm8()
    {
        var mesh = MeshDecoder.Decode(TwoStreamMesh());

        Assert.Equal(3, mesh.VertexCount);
        Assert.Equal(new[] { "position", "normal", "color", "uv0" }, mesh.Attributes.Select(a => a.Key));
        Assert.Equal(new[] { 3, 3, 4, 2 }, mesh.Attributes.Select(a => mesh.Sizes[a.Key]));
        Assert.Equal(new[] { 1f, 2, 3, 4, 5, 6, 7, 8, 9 }, Attribute(mesh, "position"));
        Assert.Equal(new[] { 1f, 0.5f, -2, 1, 0.5f, -2, 1, 0.5f, -2 }, Attribute(mesh, "normal"));
        var color = Attribute(mesh, "color");
        var expected = new[] { 1f, 0, 0.2f, 1, 1, 0, 0.2f, 1, 1, 0, 0.2f, 1 };
        Assert.Equal(expected.Length, color.Length);
        for (int i = 0; i < expected.Length; i++) Assert.Equal(expected[i], color[i], 6);
        Assert.Equal(new[] { 0.25f, 0.75f, 0.25f, 0.75f, 0.25f, 0.75f }, Attribute(mesh, "uv0"));
    }

    [Fact]
    public void SixteenBitIndicesAndTwoSubmeshes()
    {
        var mesh = MeshDecoder.Decode(TwoStreamMesh());

        Assert.Equal(new uint[] { 0, 1, 2, 2, 1, 0 }, mesh.Indices);
        Assert.Equal(new[] { new[] { 0, 3 }, new[] { 3, 3 } }, mesh.SubMeshes);
        Assert.False(mesh.Index32);
    }

    [Fact]
    public void ThirtyTwoBitIndicesWithBaseVertex()
    {
        var data = new byte[4 * 12];
        var source = new MeshData
        {
            VertexCount = 4,
            Channels = Channels((0, 0, 0, 0, 3)),
            VertexBytes = data,
            IndexBytes = U32(0, 1, 2),
            Index32 = true,
            SubMeshes = new List<SubMeshData> { new SubMeshData { FirstByte = 0, IndexCount = 3, BaseVertex = 1 } },
            BoundsCenter = new float[3],
            BoundsExtent = new float[3],
        };

        var mesh = MeshDecoder.Decode(source);

        Assert.Equal(new uint[] { 1, 2, 3 }, mesh.Indices);
        Assert.True(mesh.Index32);
    }

    [Fact]
    public void NonTriangleTopologyIsNotSupported()
    {
        var source = TwoStreamMesh();
        source.SubMeshes[1].Topology = 3; // lines
        Assert.Throws<NotSupportedException>(() => MeshDecoder.Decode(source));
    }

    [Fact]
    public void NormalizedFormats()
    {
        Assert.Equal(-1f, MeshDecoder.ReadComponent(3, new byte[] { 0x80 }, 0));
        Assert.Equal(1f, MeshDecoder.ReadComponent(3, new byte[] { 0x7F }, 0));
        Assert.Equal(1f, MeshDecoder.ReadComponent(4, new byte[] { 0xFF, 0xFF }, 0));
        Assert.Equal(-1f, MeshDecoder.ReadComponent(5, new byte[] { 0x00, 0x80 }, 0));
    }

    [Fact]
    public void WritesHeaderThenFloatsThenIndices()
    {
        var mesh = MeshDecoder.Decode(TwoStreamMesh());
        using var stream = new MemoryStream();
        MeshDecoder.Write(mesh, stream);

        stream.Position = 0;
        var header = CacheFiles.ReadHeaderLine(stream);
        Assert.Equal(0, stream.Position % 4);
        Assert.Equal(3, (int)header["vertexCount"]);
        Assert.Equal("{\"position\":3,\"normal\":3,\"color\":4,\"uv0\":2}", header["attributes"].ToString(Newtonsoft.Json.Formatting.None));
        Assert.Equal("u16", (string)header["index"]);
        Assert.True(JToken.DeepEquals(JArray.Parse("[[0,3],[3,3]]"), header["submeshes"]));
        Assert.Equal(new[] { 4f, 5f, 6f }, header["bounds"]["center"].Select(t => (float)t));
        Assert.Equal(3 * 12 * 4 + 6 * 2, stream.Length - stream.Position);

        var rest = new byte[stream.Length - stream.Position];
        stream.Read(rest, 0, rest.Length);
        Assert.Equal(1f, BitConverter.ToSingle(rest, 0)); // first position x
        Assert.Equal(2, BitConverter.ToUInt16(rest, 144 + 3 * 2)); // fourth index
    }
}

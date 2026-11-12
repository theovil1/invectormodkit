using Newtonsoft.Json.Linq;

namespace InvectorModKit.ChartEditor.Tests;

public class ClipDecoderTests
{
    /// <summary>Streamed clip words: per frame a float time, a key count, then per key a curve index and 4 coefficients.</summary>
    sealed class Streamed
    {
        readonly List<byte> bytes = new();
        public Streamed Frame(float time, params (uint curve, float c0, float c1, float c2, float c3)[] keys)
        {
            bytes.AddRange(BitConverter.GetBytes(time));
            bytes.AddRange(BitConverter.GetBytes((uint)keys.Length));
            foreach (var k in keys)
            {
                bytes.AddRange(BitConverter.GetBytes(k.curve));
                foreach (float c in new[] { k.c0, k.c1, k.c2, k.c3 }) bytes.AddRange(BitConverter.GetBytes(c));
            }
            return this;
        }
        public uint[] Words()
        {
            var words = new uint[bytes.Count / 4];
            Buffer.BlockCopy(bytes.ToArray(), 0, words, 0, bytes.Count);
            return words;
        }
    }

    /// <summary>
    /// Streamed curves 0 and 1, dense curve 2 (samples 0, 10, 20 at 2 per second), constant curve 3 (7).
    /// Binding 0: Transform scale of "Root/Ring" (curves 0, 1, 2); binding 1: "material._TintColor.a" on "Root" (curve 3).
    /// </summary>
    static MuscleClipData TestClip(float tintAlpha = 7) => new()
    {
        StartTime = 0,
        StopTime = 1.5f,
        Streamed = new Streamed()
            .Frame(0, (0, 0, 0, 2, 1), (1, 0, 0, 0, 5))
            .Frame(1, (0, 1, 0, 0, 3))
            .Frame(float.PositiveInfinity)
            .Words(),
        StreamedCurveCount = 2,
        Dense = new[] { 0f, 10, 20 },
        DenseCurveCount = 1,
        DenseFrameCount = 3,
        DenseSampleRate = 2,
        DenseBeginTime = 0,
        Constant = new[] { tintAlpha },
        Bindings = new List<ClipBinding>
        {
            new ClipBinding { Path = Crc32.Compute("Root/Ring"), TypeId = 4, Attribute = 3 },
            new ClipBinding { Path = Crc32.Compute("Root"), TypeId = 23, Attribute = Crc32.Compute("material._TintColor.a") },
        },
    };

    [Fact]
    public void SamplesStreamedDenseAndConstant()
    {
        var clip = TestClip();
        Assert.Equal(new[] { 2f, 5, 10, 7 }, ClipDecoder.Sample(clip, 0.5f));
        Assert.Equal(new[] { 3.125f, 5, 20, 7 }, ClipDecoder.Sample(clip, 1.5f));
        Assert.Equal(5f, ClipDecoder.Sample(clip, 0.25f)[2]);
    }

    [Fact]
    public void BeforeItsFirstKeyACurveTakesThatKeysValue()
    {
        var clip = TestClip();
        clip.Streamed = new Streamed().Frame(1, (0, 0, 0, 4, 2), (1, 0, 0, 0, 5)).Words();
        Assert.Equal(2f, ClipDecoder.Sample(clip, 0.5f)[0]);
    }

    [Fact]
    public void TransformAttributesTakeSeveralCurves()
    {
        Assert.Equal(new[] { 3, 4, 3, 3 }, Enumerable.Range(1, 4).Select(a => ClipDecoder.CurveCount(new ClipBinding { TypeId = 4, Attribute = (uint)a })));
        Assert.Equal(1, ClipDecoder.CurveCount(new ClipBinding { TypeId = 23, Attribute = 12345 }));
    }

    [Fact]
    public void ToJsonResolvesPathsAndAttributes()
    {
        var json = ClipDecoder.ToJson("Test", TestClip(), new[] { "", "Root", "Root/Ring" }, new[] { "material._TintColor.a" });

        Assert.Equal("Test", (string)json["name"]);
        Assert.Equal(1.5f, (float)json["length"]);
        Assert.Equal(60, (int)json["fps"]);
        var scale = json["tracks"][0];
        Assert.Equal("Root/Ring", (string)scale["path"]);
        Assert.Equal(4, (int)scale["type"]);
        Assert.Equal("scale", (string)scale["attribute"]);
        Assert.Equal(3, (int)scale["size"]);
        var values = scale["values"].Select(v => (float)v).ToArray();
        Assert.Equal(91 * 3, values.Length);
        Assert.Equal(new[] { 2f, 5, 10 }, values.Skip(90).Take(3));
        var tint = json["tracks"][1];
        Assert.Equal("Root", (string)tint["path"]);
        Assert.Equal("material._TintColor.a", (string)tint["attribute"]);
        Assert.Equal(1, (int)tint["size"]);
        Assert.All(tint["values"], v => Assert.Equal(7f, (float)v));
    }

    [Fact]
    public void UnknownHashesStayNumbers()
    {
        var json = ClipDecoder.ToJson("Test", TestClip(), Array.Empty<string>(), Array.Empty<string>());

        Assert.Equal(JTokenType.Integer, json["tracks"][0]["path"].Type);
        Assert.Equal(Crc32.Compute("Root/Ring"), (uint)json["tracks"][0]["path"]);
        Assert.Equal(Crc32.Compute("material._TintColor.a"), (uint)json["tracks"][1]["attribute"]);
    }

    [Fact]
    public void NonFiniteValuesBecomeNull()
    {
        var json = ClipDecoder.ToJson("Test", TestClip(float.NaN), Array.Empty<string>(), Array.Empty<string>());
        Assert.All(json["tracks"][1]["values"], v => Assert.Equal(JTokenType.Null, v.Type));
    }
}

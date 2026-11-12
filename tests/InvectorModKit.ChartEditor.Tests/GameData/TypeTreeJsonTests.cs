using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static InvectorModKit.ChartEditor.Tests.Fields;

namespace InvectorModKit.ChartEditor.Tests;

public class TypeTreeJsonTests
{
    static JToken NoRefs(int fileId, long pathId) => throw new InvalidOperationException("unexpected reference");

    static string Json(JToken token) => token.ToString(Formatting.None);

    [Fact]
    public void NestedFieldsKeepUnityNames()
    {
        var field = Obj("Base", Str("m_Name", "Ring"), UInt8("m_Enabled", 1), Float("speed", 2.5f),
            Obj("pos", Float("x", 1), Float("y", 2), Float("z", 3)), Bool("loop", true));

        Assert.Equal("{\"m_Name\":\"Ring\",\"m_Enabled\":1,\"speed\":2.5,\"pos\":{\"x\":1.0,\"y\":2.0,\"z\":3.0},\"loop\":true}",
            Json(TypeTreeJson.Convert(field, NoRefs)));
    }

    [Fact]
    public void ArraysBecomeLists()
    {
        var field = Obj("Base", Array("list", Int("data", 1), Int("data", 2), Int("data", 3)), Array("empty"));

        Assert.Equal("{\"list\":[1,2,3],\"empty\":[]}", Json(TypeTreeJson.Convert(field, NoRefs)));
    }

    [Fact]
    public void ReferencesGoThroughTheCallback()
    {
        var calls = new List<(int, long)>();
        JToken Link(int fileId, long pathId)
        {
            calls.Add((fileId, pathId));
            return new JObject { ["ref"] = "assets_shared-" + pathId };
        }
        var field = Obj("Base", PPtr("m_Material", "Material", 1, -3209297936361821700), PPtr("m_Mesh", "Mesh", 0, 0));

        var json = TypeTreeJson.Convert(field, Link);

        Assert.Equal("{\"m_Material\":{\"ref\":\"assets_shared--3209297936361821700\"},\"m_Mesh\":null}", Json(json));
        Assert.Equal(new[] { (1, -3209297936361821700L) }, calls);
    }

    [Fact]
    public void LargeByteArraysAreOmitted()
    {
        var field = Obj("Base", Bytes("m_Data", new byte[5000]), Bytes("small", new byte[] { 1, 2, 3 }));

        Assert.Equal("{\"m_Data\":{\"omitted\":5000},\"small\":[1,2,3]}", Json(TypeTreeJson.Convert(field, NoRefs)));
    }

    [Fact]
    public void LargeIntegersBecomeStrings()
    {
        var field = Obj("Base", Long("id", 2577891501218760700), Long("n", 42), ULong("u", ulong.MaxValue), Long("edge", 9007199254740991));

        Assert.Equal("{\"id\":\"2577891501218760700\",\"n\":42,\"u\":\"18446744073709551615\",\"edge\":9007199254740991}",
            Json(TypeTreeJson.Convert(field, NoRefs)));
    }

    [Fact]
    public void NonFiniteFloatsBecomeNull()
    {
        var field = Obj("Base", Float("a", float.NaN), Float("b", float.PositiveInfinity), Double("c", double.NegativeInfinity));

        string json = JsonConvert.SerializeObject(TypeTreeJson.Convert(field, NoRefs));

        Assert.Equal("{\"a\":null,\"b\":null,\"c\":null}", json);
        Assert.DoesNotContain("NaN", json);
        Assert.DoesNotContain("Infinity", json);
    }
}

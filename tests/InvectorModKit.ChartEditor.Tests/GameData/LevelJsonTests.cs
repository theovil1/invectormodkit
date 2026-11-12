using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InvectorModKit.ChartEditor.Tests;

public class LevelJsonTests
{
    static readonly JObject Spline = JObject.Parse(
        "{\"StartPosition\":210,\"totalLength\":2562.75,\"pos\":[{\"x\":1,\"y\":2,\"z\":3},{\"x\":4,\"y\":5,\"z\":6}]," +
        "\"rot\":[{\"x\":0,\"y\":0,\"z\":0,\"w\":1}],\"accumulatedLengthsPrecision\":[1,2]}");
    static readonly JObject Editor = JObject.Parse("{\"CircleY\":0,\"CircleRadius\":8,\"BottomLine\":-25.16,\"FlyAreas\":[{\"z\":0}]}");

    static string Json(JToken t) => t.ToString(Formatting.None);

    [Fact]
    public void MapsTheSplineAndFlyAreas()
    {
        var level = LevelJson.Build(Spline, Editor, null, null, null, null);

        Assert.Equal(210f, (float)level["start"]);
        Assert.Equal(2562.75f, (float)level["length"]);
        Assert.Equal("[[1,2,3],[4,5,6]]", Json(level["pos"]));
        Assert.Equal("[[0,0,0,1]]", Json(level["rot"]));
        Assert.Equal("[1,2]", Json(level["acc"]));
        Assert.Equal("[{\"z\":0}]", Json(level["fly"]));
        Assert.Equal("{\"y\":0,\"radius\":8,\"bottom\":-25.16}", Json(level["circle"]));
    }

    [Fact]
    public void RoadTunnelAndWalls()
    {
        var mesh = JObject.Parse("{\"m_GameObject\":{\"ref\":\"x-9\"},\"Prefabs\":[{\"ref\":\"x-1\"}],\"scale\":{\"x\":13,\"y\":13,\"z\":13},\"splineOffsetY\":-3.33}");
        var toblerone = JObject.Parse("{\"m_Name\":\"\",\"feed\":{\"ref\":\"x-2\"},\"size\":1790,\"interval\":2}");
        var walls = JObject.Parse("{\"m_Script\":\"TobleroneWalls\",\"m_Enabled\":1,\"m_GameObject\":{\"ref\":\"x-3\"},\"size\":1900,\"generateThreeWalls\":1}");

        var level = LevelJson.Build(Spline, Editor, mesh, JObject.Parse("{\"name\":\"Road\"}"), toblerone, walls);

        Assert.Equal("{\"mesh\":{\"ref\":\"x-1\"},\"material\":{\"name\":\"Road\"},\"scale\":{\"x\":13,\"y\":13,\"z\":13},\"offsetY\":-3.33}", Json(level["road"]));
        Assert.Equal("{\"size\":1790,\"interval\":2}", Json(level["tunnel"]));
        Assert.Equal("{\"size\":1900,\"generateThreeWalls\":1}", Json(level["holdWalls"]));
    }

    [Fact]
    public void MissingPartsAreNull()
    {
        var level = LevelJson.Build(Spline, Editor, null, null, null, null);

        Assert.Equal(JTokenType.Null, level["road"].Type);
        Assert.Equal(JTokenType.Null, level["tunnel"].Type);
        Assert.Equal(JTokenType.Null, level["holdWalls"].Type);
    }
}

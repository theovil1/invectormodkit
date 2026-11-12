using Newtonsoft.Json.Linq;
using Xunit.Abstractions;

namespace InvectorModKit.ChartEditor.Tests;

/// <summary>The whole cache built from the installed game, checked against values measured on the game itself.</summary>
public class GameDataIntegrationTests : IClassFixture<GameDataBuildFixture>
{
    readonly GameDataBuildFixture build;
    readonly ITestOutputHelper output;

    public GameDataIntegrationTests(GameDataBuildFixture build, ITestOutputHelper output)
    {
        this.build = build;
        this.output = output;
    }

    static JToken FindNode(JToken node, string name)
    {
        if ((string)node["name"] == name) return node;
        foreach (var child in node["children"])
        {
            var found = FindNode(child, name);
            if (found != null) return found;
        }
        return null;
    }

    static IEnumerable<JToken> Nodes(JToken node) => new[] { node }.Concat(node["children"].SelectMany(Nodes));

    static JToken Component(JToken node, string type) => node["components"].FirstOrDefault(c => (string)c["type"] == type);

    static (float head, float tail) Widths(JToken trail)
    {
        var p = trail["fields"]["m_Parameters"];
        float multiplier = (float)p["widthMultiplier"];
        var keys = p["widthCurve"]["m_Curve"];
        return (multiplier * (float)keys.First()["value"], multiplier * (float)keys.Last()["value"]);
    }

    [GameFact]
    public void EveryPrefabIsWritten()
    {
        var items = build.Index["items"].ToDictionary(i => (string)i["key"], i => (string)i["file"]);
        foreach (var item in GameDataRecipe.Items.Where(i => i.Kind == RecipeKind.Prefab))
        {
            Assert.True(items.ContainsKey(item.Key), item.Key);
            Assert.True(File.Exists(Path.Combine(build.Dir, items[item.Key])), items[item.Key]);
        }
        Assert.DoesNotContain(build.Report.Warnings, w => w.StartsWith("not found: Prefab:"));
    }

    [GameFact]
    public void BeginnerTier4Trails()
    {
        var ship = build.Json("objects/Ship_Beginner.json")["root"];

        var big = Component(FindNode(ship, "Beginner_TrailBigL_tier4"), "TrailRenderer");
        Assert.Equal(0.1f, (float)big["fields"]["m_Time"], 3);
        var (bigHead, bigTail) = Widths(big);
        Assert.Equal(0.2f, bigHead, 3);
        Assert.Equal(0.1f, bigTail, 3);

        var wideNode = FindNode(FindNode(ship, "Beginner_TrailWideR_tier4"), "Wide Right");
        var wide = Component(wideNode, "TrailRenderer");
        Assert.Equal(0.06f, (float)wide["fields"]["m_Time"], 3);
        var (wideHead, wideTail) = Widths(wide);
        Assert.Equal(0.3f, wideHead, 3);
        Assert.Equal(0.3f, wideTail, 3);
        Assert.Equal(1, (int)wide["fields"]["m_Parameters"]["alignment"]); // TransformZ: upright once turned 90 degrees
        var rotation = wideNode["rotation"].Select(v => (float)v).ToArray();
        float[] expected = { 0, 0.7071f, 0, 0.7071f };
        for (int i = 0; i < 4; i++) Assert.Equal(expected[i], rotation[i], 3);
    }

    [GameFact]
    public void PickupMeshIsWritten()
    {
        var pickup = build.Json("objects/XBOX_Pickup_Left_X_OPTIMIZED.json")["root"];
        var meshRef = Nodes(pickup).Select(n => Component(n, "MeshFilter")).Where(c => c != null)
            .Select(c => c["fields"]["m_Mesh"]).First(m => m != null && m.Type == JTokenType.Object && m["missing"] == null);
        string path = Path.Combine(build.Dir, "meshes", (string)meshRef["ref"] + ".bin");
        using var stream = File.OpenRead(path);
        var header = CacheFiles.ReadHeaderLine(stream);
        int vertexCount = (int)header["vertexCount"];
        Assert.True(vertexCount > 0);
        int floatsPerVertex = header["attributes"].Children<JProperty>().Sum(p => (int)p.Value);
        Assert.True(stream.Length >= stream.Position + (long)vertexCount * floatsPerVertex * 4);
    }

    [GameFact]
    public void ReferencesResolve()
    {
        Assert.DoesNotContain(build.Report.Warnings, w => w.Contains("references not found"));
        // particle systems use Unity's built-in meshes and materials: named, not missing
        Assert.Contains("\"builtin\":\"unity default resources\"", File.ReadAllText(Path.Combine(build.Dir, "objects", "PlayerWalkerGlobal.json")));
    }

    [GameFact]
    public void PickupHaloSpriteHasItsTexture()
    {
        var pickup = build.Json("objects/XBOX_Pickup_Left_X_OPTIMIZED.json")["root"];
        var sprite = Component(FindNode(pickup, "Level Pickup Effect"), "SpriteRenderer")["fields"]["m_Sprite"];
        Assert.Null(sprite["texture"]["missing"]);
        Assert.True(File.Exists(Path.Combine(build.Dir, "textures", (string)sprite["texture"]["ref"] + ".tex")));
        var rect = sprite["rect"].Select(v => (float)v).ToArray();
        Assert.True(rect[2] > 0 && rect[3] > 0);
    }

    [GameFact]
    public void TexturesAreSupported()
    {
        // Crunched textures are a non-goal of the spec (the preview falls back for them): reported, not failed.
        var unsupported = new List<string>();
        foreach (string file in Directory.GetFiles(Path.Combine(build.Dir, "textures"), "*.tex"))
        {
            using var stream = File.OpenRead(file);
            int format = (int)CacheFiles.ReadHeaderLine(stream)["format"];
            if (TextureWriter.IsCrunched(format)) output.WriteLine($"crunched texture: {Path.GetFileName(file)} (format {format})");
            else if (!TextureWriter.Supported.Contains(format)) unsupported.Add($"{Path.GetFileName(file)}: {format}");
        }
        Assert.True(unsupported.Count == 0, string.Join(", ", unsupported));
    }

    [GameFact]
    public void JsonIsStrict()
    {
        // A browser's JSON.parse rejects NaN and Infinity numbers (Newtonsoft would read them back without a word).
        var bad = new List<string>();
        foreach (string file in Directory.GetFiles(build.Dir, "*.json", SearchOption.AllDirectories))
        {
            var json = JToken.Parse(File.ReadAllText(file));
            var tokens = json is JContainer container ? container.DescendantsAndSelf() : new[] { json };
            foreach (var v in tokens.OfType<JValue>())
                if (v.Type == JTokenType.Float && !double.IsFinite(Convert.ToDouble(v.Value)))
                    bad.Add($"{Path.GetFileName(file)}: {v.Path}");
        }
        Assert.True(bad.Count == 0, string.Join(", ", bad.Take(20)));
    }

    [GameFact]
    public void LogsTheBuild()
    {
        var files = Directory.GetFiles(build.Dir, "*", SearchOption.AllDirectories);
        long bytes = files.Sum(f => new FileInfo(f).Length);
        output.WriteLine($"game data build: {build.Report.Elapsed.TotalSeconds:F1} s, {files.Length} files, {bytes / 1048576.0:F1} MB, {build.Report.Warnings.Count} warnings");
        foreach (var w in build.Report.Warnings) output.WriteLine("warning: " + w);
        Assert.True(build.Report.Elapsed > TimeSpan.Zero);
        Assert.NotEmpty(files);
        Assert.True(bytes > 0);
    }
}

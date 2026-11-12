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
    public void VoidAndSpaceLevels()
    {
        foreach (string world in GameDataRecipe.Worlds) Assert.True(File.Exists(Path.Combine(build.Dir, "levels", world + ".json")), world);
        foreach (var (world, start, length, fly) in new[] { ("void", 210f, 2562.75f, 17), ("space", 1400f, 4391.99f, 27) })
        {
            var level = build.Json($"levels/{world}.json");
            Assert.Equal(start, (float)level["start"], 2);
            Assert.Equal(length, (float)level["length"], 2);
            Assert.Equal(fly, level["fly"].Count());
            int curves = level["rot"].Count();
            Assert.Equal(20 * curves, level["acc"].Count());
            // 3 points a curve, plus the last one unless the path loops back to its first point (space does)
            Assert.Contains(level["pos"].Count(), new[] { 3 * curves, 3 * curves + 1 });
        }
    }

    [GameFact]
    public void VoidRoadPiece()
    {
        var road = build.Json("levels/void.json")["road"];
        Assert.True(File.Exists(Path.Combine(build.Dir, "meshes", (string)road["mesh"]["ref"] + ".bin")), road.ToString());
        Assert.False(string.IsNullOrEmpty((string)road["material"]["shader"]), road.ToString());
    }

    [GameFact]
    public void RollClips()
    {
        foreach (var (file, length) in new[] { ("RollRightLong", 1.567f), ("coaster_RollRightLong2Turns", 1.933f), ("coaster_RollRightLong3Turns", 2.6f) })
        {
            var clip = build.Json($"clips/{file}.json");
            Assert.Equal(length, (float)clip["length"], 3);
            Assert.Contains(clip["tracks"], t => (string)t["attribute"] == "rotation");
        }
        foreach (string file in new[] { "Airpickup_Spawn_Legacy", "Airpickup_ActivateEffect_Legacy" })
            Assert.NotEmpty(build.Json($"clips/{file}.json")["tracks"]);
    }

    [GameFact]
    public void CoasterAnimator() => Assert.NotEmpty(build.Json("objects/Coaster_Animator.json")["fields"]["m_TOS"]);

    [GameFact]
    public void ControlLookMaterials()
    {
        var entries = build.Json("settings.json")["scriptables"]["MaterialsDatabase"][0]["fields"]["materialPerControlType"];
        float[] LeftColor(int controlType)
        {
            var material = entries.Single(e => (int)e["controlType"] == controlType)["materialPickupLeft"];
            var color = material["colors"]["_TintColor"] ?? material["colors"]["_Color"];
            return color.Select(v => (float)v).ToArray();
        }
        var xbox = LeftColor(2); // ControlType.XBOX: X is blue
        Assert.True(xbox[2] > xbox[0] && xbox[2] > xbox[1], string.Join(", ", xbox));
        var playstation = LeftColor(1); // ControlType.Playstation: square is pink
        Assert.True(playstation[0] > playstation[1] && playstation[2] > playstation[1], string.Join(", ", playstation));
    }

    [GameFact]
    public void WalkerComponents()
    {
        // These names come from the spec: if the game names one differently, tell the owner rather than editing them here.
        var walker = build.Json("settings.json")["walker"];
        foreach (string cls in GameDataRecipe.WalkerClasses) Assert.True(walker[cls] != null && walker[cls].Any(), cls);
        var tree = build.Json("objects/PlayerWalkerGlobal.json")["root"];
        foreach (string name in new[] { "PickupParticles", "StrumParticles", "PickupParticlesLand" })
        {
            var node = FindNode(tree, name);
            Assert.True(node != null, name);
            // PickupParticles holds one system per level (Level0 to Level3) in its children
            Assert.True(Nodes(node).Any(n => Component(n, "ParticleSystem") != null), name);
        }
    }

    [GameFact]
    public void NoWarnings()
    {
        // crunched textures are a non-goal of the spec: they are expected warnings
        var warnings = build.Report.Warnings.Where(w => !w.Contains("crunched")).ToList();
        Assert.True(warnings.Count == 0, string.Join("\n", warnings));
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

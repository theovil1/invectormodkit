using InvectorModKit.Charting;
using InvectorModKit.ChartEditor;
using InvectorModKit.Marketplace.Client;
using Newtonsoft.Json.Linq;

namespace InvectorModKit.ChartEditor.Tests;

/// <summary>A fake game install (exe + CustomSongs) and a song outside it, in a temp folder.</summary>
public sealed class Sandbox : IDisposable
{
    public readonly string Root = Path.Combine(Path.GetTempPath(), "imk-editor-" + Guid.NewGuid().ToString("N"));
    public string GameDir => Path.Combine(Root, "Steam", "steamapps", "common", GameLocator.GameFolder);
    public string CustomSongs => Path.Combine(GameDir, "CustomSongs");
    public readonly List<string> Opened = new();

    public Sandbox()
    {
        Directory.CreateDirectory(CustomSongs);
        File.WriteAllText(Path.Combine(GameDir, GameLocator.GameFolder + ".exe"), "");
    }

    public EditorBackend Backend(IHttpTransport http = null) =>
        new(new EditorSettings { GameDir = GameDir, FilePath = Path.Combine(Root, "settings.json") }, Opened.Add, http);

    /// <summary>A short WAV (decodable by AudioFile).</summary>
    public string Wav(string path, double seconds = 3)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        int rate = 22050, n = (int)(rate * seconds);
        using var w = new BinaryWriter(File.Create(path));
        w.Write("RIFF"u8.ToArray()); w.Write(36 + n * 2); w.Write("WAVEfmt "u8.ToArray());
        w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(n * 2);
        var rng = new Random(1);
        for (int i = 0; i < n; i++) w.Write((short)((i % 11025 < 800 ? rng.Next(-20000, 20000) : 0)));
        return path;
    }

    public static JObject SampleChart(bool stamps = false)
    {
        var o = JObject.Parse("""
            { "format": 1, "bpm": 120, "firstBeat": 0.5, "start": 0.2, "end": 3, "preview": 1,
              "sections": [ { "t": 0, "mode": "List" } ],
              "notes": { "Casual": [ { "t": 1.0 } ], "Normal": [], "Hard": [ { "t": 1.0 }, { "t": 1.03 } ], "Ultra": [ { "t": 2.0, "strum": true, "len": 0.5 } ] } }
            """);
        if (stamps)
        {
            o["generator"] = new JObject { ["version"] = 7, ["audioHash"] = "abc" };
            o["source"] = new JObject { ["marketplace"] = "x1", ["version"] = 2, ["author"] = "Alice" };
        }
        return o;
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, true); } catch { }
    }
}

public class EditorBackendTests
{
    [Fact]
    public async Task Open_WithoutChart_GivesNull_AndSongInfo()
    {
        using var box = new Sandbox();
        string audio = box.Wav(Path.Combine(box.CustomSongs, "Daft Punk - One More Time", "track.wav"));
        var result = (JObject)await box.Backend().Handle("open", new JObject { ["audioPath"] = audio });
        Assert.Equal(JTokenType.Null, result["chart"].Type);
        Assert.True((bool)result["inCustomSongs"]);
        Assert.EndsWith("track.chart.json", (string)result["chartPath"]);
    }

    [Fact]
    public async Task Save_MakesTheChartHandmade_AndOpenReadsItBack()
    {
        using var box = new Sandbox();
        string audio = box.Wav(Path.Combine(box.Root, "elsewhere", "song.wav"));
        var backend = box.Backend();
        await backend.Handle("save", new JObject { ["audioPath"] = audio, ["chart"] = Sandbox.SampleChart(stamps: true) });
        var saved = ChartJson.Deserialize(File.ReadAllText(SongFiles.ChartPathFor(audio)));
        Assert.Null(saved.Generator);
        Assert.Null(saved.Source);
        Assert.Equal(2, saved[Difficulty.Hard].Count);
        Assert.False(File.Exists(Path.Combine(box.Root, "elsewhere", "song.json"))); // outside CustomSongs: no song.json

        var opened = (JObject)await backend.Handle("open", new JObject { ["audioPath"] = audio });
        Assert.Equal(0.5, (double)opened["chart"]["notes"]["Ultra"][0]["len"]);
    }

    [Fact]
    public async Task Validate_GivesIssuesWithTheirTime()
    {
        using var box = new Sandbox();
        var result = await box.Backend().Handle("validate", new JObject { ["chart"] = Sandbox.SampleChart() });
        var hard = (JArray)result["Hard"];
        Assert.Equal(("gap", 1.03), ((string)hard[0]["code"], (double)hard[0]["time"]));
        Assert.Empty((JArray)result["Casual"]);
    }

    [Fact]
    public async Task SendToGame_CopiesIntoANewFolder_ThenSavesInPlace()
    {
        using var box = new Sandbox();
        string audio = box.Wav(Path.Combine(box.Root, "downloads", "track.wav"));
        Directory.CreateDirectory(Path.Combine(box.CustomSongs, "Coldplay - Viva_ La Vida")); // taken: the copy goes to "(2)"
        var backend = box.Backend();
        var sent = (JObject)await backend.Handle("sendToGame", new JObject { ["audioPath"] = audio, ["chart"] = Sandbox.SampleChart(), ["title"] = "Viva: La Vida", ["artist"] = "Coldplay" });
        string copy = (string)sent["audioPath"];
        Assert.True((bool)sent["copied"]);
        Assert.Equal(Path.Combine(box.CustomSongs, "Coldplay - Viva_ La Vida (2)", "track.wav"), copy);
        Assert.True(File.Exists(SongFiles.ChartPathFor(copy)));
        Assert.Equal("Viva: La Vida", SongFiles.Read(copy).Title); // song.json

        var again = (JObject)await backend.Handle("sendToGame", new JObject { ["audioPath"] = copy, ["chart"] = Sandbox.SampleChart(), ["title"] = "Viva La Vida", ["artist"] = "Coldplay" });
        Assert.False((bool)again["copied"]);
        Assert.Equal(copy, (string)again["audioPath"]);
        Assert.Equal("Viva La Vida", SongFiles.Read(copy).Title);
        Assert.Equal(2, Directory.GetDirectories(box.CustomSongs).Length);
    }

    [Fact]
    public async Task Songs_ListsLikeTheMod()
    {
        using var box = new Sandbox();
        box.Wav(Path.Combine(box.CustomSongs, "Artist - Loose.wav"));
        box.Wav(Path.Combine(box.CustomSongs, "Folder", "b.wav"));
        box.Wav(Path.Combine(box.CustomSongs, "Folder", "a.wav"));
        box.Wav(Path.Combine(box.CustomSongs, ".cache", "x.wav"));
        var songs = (JArray)await box.Backend().Handle("songs", null);
        Assert.Equal(new[] { "Artist - Loose.wav", "a.wav" }, songs.Select(s => Path.GetFileName((string)s["audioPath"])));
        Assert.Equal(("Artist", "Loose"), ((string)songs[0]["artist"], (string)songs[0]["title"]));
    }

    [Fact]
    public async Task NewChart_Blank_HasTheDetectedGridAndNoNotes()
    {
        using var box = new Sandbox();
        string audio = box.Wav(Path.Combine(box.Root, "a.wav"), seconds: 8);
        var chart = (JObject)await box.Backend().Handle("newChart", new JObject { ["audioPath"] = audio, ["mode"] = "blank" });
        Assert.True((double)chart["bpm"] > 0);
        Assert.Equal(8.0, (double)chart["end"], 1);
        Assert.Empty((JArray)chart["notes"]["Hard"]);
        Assert.Equal("List", (string)chart["sections"][0]["mode"]);
    }

    [Fact]
    public async Task Publish_AsksForTheRules_ThenPinsTheSongInCustomSongs()
    {
        using var box = new Sandbox();
        string audio = box.Wav(Path.Combine(box.CustomSongs, "Me - Song", "song.wav"));
        var http = new FakeTransport()
            .Reply(428, """{"error":"rules"}""")
            .Reply(200, """{"id":"c1","title":"Song","artist":"Me","author":"Teyko","youtubeId":"dvgZkm1xWPE","duration":3,"bpm":120,"notes":{"casual":1,"normal":0,"hard":2,"ultra":1},"version":3,"updatedAt":0,"downloads":0}""");
        var backend = box.Backend(http);
        backend.Settings.Token = "t";
        await Assert.ThrowsAsync<InvalidOperationException>(() => box.Backend().Handle("publish", new JObject { ["audioPath"] = audio, ["chart"] = Sandbox.SampleChart(), ["title"] = "Song" }));
        var request = new JObject { ["audioPath"] = audio, ["chart"] = Sandbox.SampleChart(), ["title"] = "Song", ["artist"] = "Me", ["youtubeUrl"] = "https://youtu.be/dvgZkm1xWPE" };
        var backendWithToken = new EditorBackend(new EditorSettings { GameDir = box.GameDir, FilePath = Path.Combine(box.Root, "s.json"), Token = "t" }, _ => { }, http);
        Assert.True((bool)(await backendWithToken.Handle("publish", request))["needRules"]);
        request["acceptRules"] = true;
        var published = (JObject)await backendWithToken.Handle("publish", request);
        Assert.Equal(("c1", 3), ((string)published["id"], (int)published["version"]));
        Assert.True((bool)JObject.Parse(http.Sent[1].Body)["acceptRules"]);
        var pinned = ChartJson.Deserialize(File.ReadAllText(SongFiles.ChartPathFor(audio)));
        Assert.Equal(("c1", 3, "Teyko"), (pinned.Source.MarketplaceId, pinned.Source.Version, pinned.Source.Author));
    }

    [Fact]
    public async Task UnknownMethod_AndNonHttpsLinks_AreRefused()
    {
        using var box = new Sandbox();
        await Assert.ThrowsAsync<ArgumentException>(() => box.Backend().Handle("nope", null));
        await Assert.ThrowsAsync<ArgumentException>(() => box.Backend().Handle("openUrl", new JObject { ["url"] = "file:///c:/windows" }));
        await box.Backend().Handle("openUrl", new JObject { ["url"] = "https://youtu.be/x" });
        Assert.Equal(new[] { "https://youtu.be/x" }, box.Opened);
    }
}

public class GameLocatorTests
{
    [Fact]
    public void LibraryFolders_ReadsVdfPaths()
    {
        const string vdf = """
            "libraryfolders"
            {
              "0" { "path"   "C:\\Program Files (x86)\\Steam"   "label" "" }
              "1" { "path"   "D:\\SteamLibrary" }
            }
            """;
        Assert.Equal(new[] { @"C:\Program Files (x86)\Steam", @"D:\SteamLibrary" }, GameLocator.LibraryFolders(vdf));
    }

    [Fact]
    public void Find_LooksInEveryLibrary()
    {
        using var box = new Sandbox();
        string steam = Path.Combine(box.Root, "SteamMain");
        Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
        File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"),
            "\"libraryfolders\" { \"1\" { \"path\" \"" + Path.Combine(box.Root, "Steam").Replace("\\", "\\\\") + "\" } }");
        Assert.Equal(Path.GetFullPath(box.GameDir), GameLocator.Find(steam));
        Assert.Null(GameLocator.Find(Path.Combine(box.Root, "nothing")));
    }

    [Fact]
    public void FolderName_IsWindowsSafe()
    {
        Assert.Equal("AC_DC - Back In Black_", SongFiles.FolderName("AC/DC", "Back In Black?"));
        Assert.Equal("Solo", SongFiles.FolderName("", "Solo"));
    }
}

internal sealed class FakeTransport : IHttpTransport
{
    private readonly Queue<HttpResult> _replies = new();
    public readonly List<(string Method, string Url, string Body)> Sent = new();

    public FakeTransport Reply(int status, string body)
    {
        _replies.Enqueue(new HttpResult { Status = status, Body = body });
        return this;
    }

    public Task<HttpResult> SendAsync(string method, string url, string jsonBody, string bearer)
    {
        Sent.Add((method, url, jsonBody));
        return Task.FromResult(_replies.Dequeue());
    }
}

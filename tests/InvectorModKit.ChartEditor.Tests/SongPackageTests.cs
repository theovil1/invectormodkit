using System.IO.Compression;
using System.Text;
using InvectorModKit.Charting;
using InvectorModKit.ChartEditor;
using Newtonsoft.Json.Linq;

namespace InvectorModKit.ChartEditor.Tests;

public class SongPackageTests
{
    /// <summary>The zip's entries by name, with their bytes.</summary>
    private static Dictionary<string, byte[]> Entries(string zip)
    {
        using var archive = ZipFile.OpenRead(zip);
        return archive.Entries.ToDictionary(e => e.FullName, e =>
        {
            using var s = e.Open();
            using var m = new MemoryStream();
            s.CopyTo(m);
            return m.ToArray();
        });
    }

    private static Task<JToken> Export(EditorBackend backend, string audio, string zip, string title = "Viva: La Vida", string artist = "Coldplay") =>
        backend.Handle("export", new JObject
        {
            ["audioPath"] = audio, ["chart"] = Sandbox.SampleChart(stamps: true), ["title"] = title, ["artist"] = artist, ["zipPath"] = zip,
        });

    [Fact]
    public async Task Export_HoldsAudioChartCoverAndSongInfo()
    {
        using var box = new Sandbox();
        string audio = box.Wav(Path.Combine(box.CustomSongs, "Folder", "track.wav"));
        File.WriteAllBytes(Path.Combine(box.CustomSongs, "Folder", "cover.jpg"), new byte[] { 1, 2, 3 });
        File.WriteAllBytes(Path.Combine(box.CustomSongs, "Folder", "track.png"), new byte[] { 9 });
        string zip = Path.Combine(box.Root, "out", "song.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zip));

        await Export(box.Backend(), audio, zip);

        var entries = Entries(zip);
        Assert.Equal(new[] { "song.json", "track.chart.json", "track.jpg", "track.wav" }, entries.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(File.ReadAllBytes(audio), entries["track.wav"]);
        Assert.Equal(new byte[] { 1, 2, 3 }, entries["track.jpg"]); // the folder's cover wins, as in the game
        var chart = ChartJson.Deserialize(Encoding.UTF8.GetString(entries["track.chart.json"]));
        Assert.Null(chart.Generator);
        Assert.Null(chart.Source);
        Assert.Equal(2, chart[Difficulty.Hard].Count);
        var info = JObject.Parse(Encoding.UTF8.GetString(entries["song.json"]));
        Assert.Equal(("Viva: La Vida", "Coldplay"), ((string)info["title"], (string)info["artist"]));
    }

    [Fact]
    public async Task Export_LooseSong_TakesTheImageNamedLikeTheAudio()
    {
        using var box = new Sandbox();
        string audio = box.Wav(Path.Combine(box.CustomSongs, "Artist - Loose.wav"));
        File.WriteAllBytes(Path.Combine(box.CustomSongs, "Artist - Loose.PNG"), new byte[] { 7 });
        File.WriteAllBytes(Path.Combine(box.CustomSongs, "cover.jpg"), new byte[] { 1 }); // the shared root has no cover
        string zip = Path.Combine(box.Root, "loose.zip");

        await Export(box.Backend(), audio, zip);

        var entries = Entries(zip);
        Assert.Equal(new byte[] { 7 }, entries["Artist - Loose.png"]);
        Assert.Single(entries.Keys, k => k.EndsWith(".png") || k.EndsWith(".jpg") || k.EndsWith(".jpeg"));
    }

    [Fact]
    public async Task Export_WithoutCover_HasNoImage()
    {
        using var box = new Sandbox();
        string audio = box.Wav(Path.Combine(box.Root, "downloads", "track.wav"));
        File.WriteAllBytes(Path.Combine(box.Root, "downloads", "cover.jpg"), new byte[] { 1 }); // folder covers count only in CustomSongs
        string zip = Path.Combine(box.Root, "out.zip");

        await Export(box.Backend(), audio, zip);

        Assert.Equal(new[] { "song.json", "track.chart.json", "track.wav" }, Entries(zip).Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Export_ReplacesAnExistingZip()
    {
        using var box = new Sandbox();
        string audio = box.Wav(Path.Combine(box.Root, "downloads", "track.wav"));
        string zip = Path.Combine(box.Root, "out.zip");
        File.WriteAllBytes(zip, new byte[1024 * 1024]);

        await Export(box.Backend(), audio, zip);

        Assert.Equal(3, Entries(zip).Count);
        Assert.Equal(new[] { zip }, Directory.GetFiles(box.Root));
    }

    // ----- import

    private static string MakeZip(string path, params (string Name, byte[] Data)[] entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using var zip = new ZipArchive(File.Create(path), ZipArchiveMode.Create);
        foreach (var (name, data) in entries)
        {
            using var s = zip.CreateEntry(name).Open();
            s.Write(data);
        }
        return path;
    }

    private static byte[] WavBytes(Sandbox box)
    {
        string wav = box.Wav(Path.Combine(box.Root, "tmp-" + Guid.NewGuid().ToString("N") + ".wav"));
        var bytes = File.ReadAllBytes(wav);
        File.Delete(wav);
        return bytes;
    }

    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    private static async Task<string> ExportedZip(Sandbox from, string audioName, string title = "Viva: La Vida", string artist = "Coldplay")
    {
        string audio = from.Wav(Path.Combine(from.Root, "downloads", audioName));
        File.WriteAllBytes(Path.Combine(from.Root, "downloads", Path.GetFileNameWithoutExtension(audioName) + ".jpg"), new byte[] { 5, 6 });
        string zip = Path.Combine(from.Root, "shared.zip");
        await Export(from.Backend(), audio, zip, title, artist);
        return zip;
    }

    private static async Task<JObject> Import(Sandbox box, string zip) =>
        (JObject)await box.Backend().Handle("import", new JObject { ["zipPath"] = zip });

    /// <summary>Folders the import left in CustomSongs (its temporary ones included).</summary>
    private static string[] Folders(Sandbox box) => Directory.GetDirectories(box.CustomSongs).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    [Fact]
    public async Task Import_ExportedZip_LandsInItsOwnFolder()
    {
        using var from = new Sandbox();
        using var to = new Sandbox();
        string zip = await ExportedZip(from, "track.wav");

        var result = await Import(to, zip);

        string audio = (string)result["audioPath"];
        string folder = Path.Combine(to.CustomSongs, "Coldplay - Viva_ La Vida");
        Assert.Equal(Path.Combine(folder, "track.wav"), audio);
        Assert.Equal(File.ReadAllBytes(Path.Combine(from.Root, "downloads", "track.wav")), File.ReadAllBytes(audio));
        Assert.Equal(2, ChartJson.Deserialize(File.ReadAllText(Path.Combine(folder, "track.chart.json")))[Difficulty.Hard].Count);
        Assert.Equal(new byte[] { 5, 6 }, File.ReadAllBytes(Path.Combine(folder, "track.jpg")));
        Assert.Equal(("Viva: La Vida", "Coldplay"), (SongFiles.Read(audio, to.CustomSongs).Title, SongFiles.Read(audio, to.CustomSongs).Artist));
        Assert.Equal(new[] { "Coldplay - Viva_ La Vida" }, Folders(to));
    }

    [Fact]
    public async Task Import_Twice_GoesToANumberedFolder()
    {
        using var from = new Sandbox();
        using var to = new Sandbox();
        string zip = await ExportedZip(from, "track.wav");

        await Import(to, zip);
        var second = await Import(to, zip);

        Assert.Equal(Path.Combine(to.CustomSongs, "Coldplay - Viva_ La Vida (2)", "track.wav"), (string)second["audioPath"]);
        Assert.Equal(new[] { "Coldplay - Viva_ La Vida", "Coldplay - Viva_ La Vida (2)" }, Folders(to));
    }

    [Theory]
    [InlineData("John Summit, Inéz - crystallized.wav", "crystallized (feat. Inéz)", "John Summit, Inéz")]
    [InlineData("曲.wav", "夜に駆ける", "YOASOBI")]
    public async Task Import_KeepsNonAsciiNames(string audioName, string title, string artist)
    {
        using var from = new Sandbox();
        using var to = new Sandbox();
        string zip = await ExportedZip(from, audioName, title, artist);

        string audio = (string)(await Import(to, zip))["audioPath"];

        Assert.Equal(audioName, Path.GetFileName(audio));
        Assert.Equal(SongFiles.FolderName(artist, title), Path.GetFileName(Path.GetDirectoryName(audio)));
        Assert.True(File.Exists(SongFiles.ChartPathFor(audio)));
        Assert.Equal(title, SongFiles.Read(audio, to.CustomSongs).Title);
    }

    [Fact]
    public async Task Import_HandMadeZip_RenamesTheChartAndTakesTheFolderInside()
    {
        using var box = new Sandbox();
        string zip = MakeZip(Path.Combine(box.Root, "in", "Hand made.zip"),
            ("My Song/a.wav", WavBytes(box)),
            ("My Song/chart.chart.json", Utf8(Sandbox.SampleChart().ToString())),
            ("My Song/cover.jpg", new byte[] { 3 }),
            ("__MACOSX/My Song/._a.wav", new byte[] { 0, 5, 22 }),
            ("notes.txt", Utf8("hello")));

        string audio = (string)(await Import(box, zip))["audioPath"];

        string folder = Path.Combine(box.CustomSongs, "Hand made");
        Assert.Equal(Path.Combine(folder, "a.wav"), audio);
        Assert.Equal(new[] { "a.chart.json", "a.jpg", "a.wav" }, Directory.GetFiles(folder).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Single(ChartJson.Deserialize(File.ReadAllText(Path.Combine(folder, "a.chart.json")))[Difficulty.Casual]);
    }

    [Theory]
    [InlineData("parent", "unsafe")]
    [InlineData("drive", "unsafe")]
    [InlineData("rooted", "unsafe")]
    [InlineData("none", "noSong")]
    [InlineData("two", "severalSongs")]
    [InlineData("text", "notZip")]
    [InlineData("crowded", "tooBig")]
    public async Task Import_RefusesUnsafeZips(string kind, string refusal)
    {
        using var box = new Sandbox();
        string zip = Path.Combine(box.Root, "in", kind + ".zip");
        byte[] wav = WavBytes(box);
        switch (kind)
        {
            case "parent": MakeZip(zip, ("song/../../evil.wav", wav)); break;
            case "drive": MakeZip(zip, ("C:/evil.wav", wav)); break;
            case "rooted": MakeZip(zip, ("/evil.wav", wav)); break;
            case "none": MakeZip(zip, ("readme.txt", Utf8("no song"))); break;
            case "two": MakeZip(zip, ("a.wav", wav), ("b.wav", wav)); break;
            case "text": Directory.CreateDirectory(Path.GetDirectoryName(zip)); File.WriteAllText(zip, "not a zip"); break;
            case "crowded": MakeZip(zip, new[] { ("a.wav", wav) }.Concat(Enumerable.Range(0, 50).Select(i => ($"n{i}.txt", Utf8("x")))).ToArray()); break;
        }

        var result = await Import(box, zip);

        Assert.Equal(refusal, (string)result["refused"]);
        Assert.Empty(Folders(box));
        Assert.Empty(Directory.GetFiles(box.Root, "evil.wav", SearchOption.AllDirectories));
    }

    [Fact]
    public void Import_StopsAtTheByteLimit()
    {
        using var box = new Sandbox();
        string zip = MakeZip(Path.Combine(box.Root, "big.zip"), ("a.wav", WavBytes(box)));

        var e = Assert.Throws<SongPackageException>(() => SongPackage.Import(zip, box.CustomSongs, maxBytes: 1000));

        Assert.Equal(ImportRefusal.TooBig, e.Reason);
        Assert.Empty(Folders(box));
    }

    [Fact]
    public void Import_NeverUnpacksMoreThanTheLimit_EvenWhenTheSizesLie()
    {
        // .NET Framework (the editor's runtime) unpacks such an entry in full: the import's own byte count stops it.
        // .NET 8 (these tests) cuts it at the declared size by itself. Either way nothing past the limit reaches the disk.
        using var box = new Sandbox();
        string zip = Path.Combine(box.Root, "bomb.zip");
        MakeZip(zip, ("a.wav", new byte[200_000])); // zeros: a few hundred bytes once compressed
        // The zip claims the audio is 100 bytes, in its local header (offset 22) and its central directory (offset 24).
        var bytes = File.ReadAllBytes(zip);
        for (int i = 0; i + 4 <= bytes.Length; i++)
        {
            uint sig = BitConverter.ToUInt32(bytes, i);
            if (sig == 0x04034b50) BitConverter.GetBytes(100u).CopyTo(bytes, i + 22);
            if (sig == 0x02014b50) BitConverter.GetBytes(100u).CopyTo(bytes, i + 24);
        }
        File.WriteAllBytes(zip, bytes);

        try
        {
            string audio = SongPackage.Import(zip, box.CustomSongs, maxBytes: 50_000);
            Assert.True(new FileInfo(audio).Length <= 50_000);
        }
        catch (SongPackageException e)
        {
            Assert.Contains(e.Reason, new[] { ImportRefusal.TooBig, ImportRefusal.NotZip });
            Assert.Empty(Folders(box));
        }
        Assert.DoesNotContain(Folders(box), f => f.StartsWith("."));
    }

    [Fact]
    public async Task Import_WithoutGame_IsRefused()
    {
        using var box = new Sandbox();
        string zip = MakeZip(Path.Combine(box.Root, "a.zip"), ("a.wav", WavBytes(box)));
        var backend = new EditorBackend(new EditorSettings { GameDir = "", FilePath = Path.Combine(box.Root, "settings.json") }, _ => { });

        await Assert.ThrowsAsync<InvalidOperationException>(() => backend.Handle("import", new JObject { ["zipPath"] = zip }));
    }
}

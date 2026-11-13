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
}

using System;
using System.IO;
using System.Linq;
using InvectorModKit.Charting.Audio;
using InvectorModKit.Charting.Marketplace;

namespace InvectorModKit.Charting.Tests;

public class PendingInstallTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "imk-pending-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private void Write(string id, bool installed = false, bool complete = true)
    {
        string d = Path.Combine(_dir, id);
        Directory.CreateDirectory(d);
        File.WriteAllText(Path.Combine(d, PendingInstall.EntryFile),
            $"{{\"id\":\"{id}\",\"title\":\"Song {id}\",\"artist\":\"Artist\",\"youtubeId\":\"dvgZkm1xWPE\",\"author\":\"Alice\",\"version\":2}}");
        var chart = new Chart { Bpm = 120, EndTime = 60, Generator = new GeneratorStamp { Version = 7, AudioHash = "x" } };
        chart.Sections.Add(new Section(0, SectionMode.List));
        chart[Difficulty.Ultra].Add(new Note(10, NoteKind.Pickup, 1, NoteColor.Down));
        File.WriteAllText(Path.Combine(d, PendingInstall.ChartFile), ChartJson.Serialize(chart));
        if (complete)
            File.WriteAllText(Path.Combine(d, PendingInstall.FingerprintFile), new Fingerprint { Duration = 60, Envelope = new byte[6000] }.ToJson());
        if (installed) File.WriteAllText(Path.Combine(d, PendingInstall.InstalledFile), "{}");
    }

    [Fact]
    public void Load_SkipsInstalled()
    {
        Write("a");
        Write("b", installed: true);
        Write("c", complete: false);
        var pending = PendingInstall.Load(_dir).ToList();
        Assert.Equal(new[] { "a" }, pending.Select(p => p.Id));
        Assert.Equal("Song a", pending[0].Title);
        Assert.Equal(2, pending[0].Version);
        Assert.Equal(6000, pending[0].Fingerprint.Envelope.Length);
        Assert.Empty(PendingInstall.Load(Path.Combine(_dir, "missing")));
    }

    [Fact]
    public void ChartFor_ShiftsAndStamps()
    {
        Write("a");
        var install = PendingInstall.Load(_dir).Single();
        var chart = install.ChartFor(new FingerprintMatch { IsMatch = true, Offset = 2.5 });
        Assert.Equal(12.5, chart[Difficulty.Ultra][0].Time, 9);
        Assert.Null(chart.Generator);
        Assert.Equal("a", chart.Source.MarketplaceId);
        Assert.Equal(2, chart.Source.Version);
        Assert.Equal("Alice", chart.Source.Author);

        install.MarkInstalled(@"C:\songs\viva.mp3", 2.5);
        Assert.Empty(PendingInstall.Load(_dir));
        Assert.Contains("viva.mp3", File.ReadAllText(Path.Combine(_dir, "a", PendingInstall.InstalledFile)));
    }
}

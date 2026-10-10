using System.Collections.Generic;
using System.Linq;
using InvectorModKit.Charting;
using InvectorModKit.Charting.Generation;

namespace InvectorModKit.Charting.Tests;

public class ChartSourceTests
{
    private static Chart Sample()
    {
        var chart = new Chart
        {
            Bpm = 120, FirstBeat = 0.5, StartTime = 0.2, EndTime = 100, PreviewStart = 30, Gain = 1.5,
            Sections = new List<Section> { new(0, SectionMode.List), new(20, SectionMode.Bounce), new(60, SectionMode.List) },
        };
        foreach (var d in new[] { Difficulty.Casual, Difficulty.Normal, Difficulty.Hard, Difficulty.Ultra })
            chart[d].AddRange(new[] { 1.0, 6.0, 30.0, 90.0 }.Select(t => new Note(t, NoteKind.Pickup, 1, NoteColor.Down)));
        return chart;
    }

    [Fact]
    public void Source_RoundTrips()
    {
        var chart = Sample();
        chart.Source = new ChartSource { MarketplaceId = "abc123", Version = 3, Author = "Théo" };
        var back = ChartJson.Deserialize(ChartJson.Serialize(chart));
        Assert.Equal("abc123", back.Source.MarketplaceId);
        Assert.Equal(3, back.Source.Version);
        Assert.Equal("Théo", back.Source.Author);
        Assert.Null(back.Generator);
        Assert.Contains("\"marketplace\": \"abc123\"", ChartJson.Serialize(chart));
    }

    [Fact]
    public void Source_ChartIsNeverStale()
    {
        var generated = Sample();
        generated.Generator = new GeneratorStamp { Version = ChartGenerator.Version - 1, AudioHash = "h" };
        Assert.True(ChartGenerator.IsStale(generated, "h"));
        generated.Generator.Version = ChartGenerator.Version;
        Assert.False(ChartGenerator.IsStale(generated, "h"));
        Assert.True(ChartGenerator.IsStale(generated, "other audio"));

        var installed = Sample();
        installed.Source = new ChartSource { MarketplaceId = "x", Version = 1, Author = "a" };
        Assert.False(ChartGenerator.IsStale(installed, "any hash"));
        Assert.False(ChartGenerator.IsStale(Sample(), "hand-made"));
    }

    [Fact]
    public void Shift_MovesEverything()
    {
        var shifted = Sample().Shifted(7.25);
        Assert.Equal(new[] { 8.25, 13.25, 37.25, 97.25 }, shifted[Difficulty.Ultra].Select(n => n.Time));
        Assert.Equal(7.75, shifted.FirstBeat, 9);
        Assert.Equal(7.45, shifted.StartTime, 9);
        Assert.Equal(107.25, shifted.EndTime, 9);
        Assert.Equal(37.25, shifted.PreviewStart, 9);
        Assert.Equal(new[] { 0.0, 27.25, 67.25 }, shifted.Sections.Select(s => s.Start));
        Assert.Equal(1.5, shifted.Gain);
    }

    [Fact]
    public void Shift_DropsNotesBeforeZero()
    {
        var shifted = Sample().Shifted(-25);
        Assert.Equal(new[] { 5.0, 65.0 }, shifted[Difficulty.Casual].Select(n => n.Time));
        Assert.Equal(0, shifted.StartTime, 9);
        Assert.Equal(5, shifted.PreviewStart, 9);
        // The section playing at the new t = 0 (Bounce, from 20 s) starts the chart.
        Assert.Equal(new[] { (0.0, SectionMode.Bounce), (35.0, SectionMode.List) }, shifted.Sections.Select(s => (s.Start, s.Mode)));
    }
}

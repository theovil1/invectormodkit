using InvectorModKit.Charting;

namespace InvectorModKit.Charting.Tests;

public class PlayabilityIssuesTests
{
    private static Chart ListChart(params Note[] notes)
    {
        var chart = new Chart { Bpm = 120, EndTime = 60 };
        chart.Sections.Add(new Section(0, SectionMode.List));
        chart[Difficulty.Hard].AddRange(notes);
        return chart;
    }

    [Fact]
    public void TooClose_IsReportedAtTheSecondNote()
    {
        var chart = ListChart(new Note(10.0, NoteKind.Pickup, 1, NoteColor.Down), new Note(10.05, NoteKind.Pickup, 1, NoteColor.Down));
        var issue = Assert.Single(Playability.Issues(chart, Difficulty.Hard));
        Assert.Equal(("gap", 10.05), (issue.Code, issue.Time));
        Assert.Equal(0.05, issue.Gap, 3);
        Assert.Equal($"{10.05:0.00}s: notes only {0.05:0.000}s apart", issue.ToString());
        Assert.Equal(new[] { issue.ToString() }, Playability.Validate(chart, Difficulty.Hard));
    }

    [Fact]
    public void LaneChangeWithoutTurn_HasModeAndCode()
    {
        var chart = ListChart(new Note(10.0, NoteKind.Pickup, 1, NoteColor.Down), new Note(12.0, NoteKind.Pickup, 2, NoteColor.Down));
        var issue = Assert.Single(Playability.Issues(chart, Difficulty.Hard));
        Assert.Equal(("lane-change", 12.0, (SectionMode?)SectionMode.List), (issue.Code, issue.Time, issue.Mode));
    }

    [Fact]
    public void IssuesAreSortedByTime()
    {
        var chart = ListChart(
            new Note(5.0, NoteKind.Pickup, 1, NoteColor.Down), new Note(9.0, NoteKind.Pickup, 2, NoteColor.Down),
            new Note(20.0, NoteKind.Pickup, 2, NoteColor.Down), new Note(20.03, NoteKind.Pickup, 2, NoteColor.Down));
        var times = Playability.Issues(chart, Difficulty.Hard).Select(i => i.Time).ToList();
        Assert.Equal(times.OrderBy(t => t), times);
        Assert.True(Playability.Issues(ListChart(), Difficulty.Hard).Count == 0);
    }
}

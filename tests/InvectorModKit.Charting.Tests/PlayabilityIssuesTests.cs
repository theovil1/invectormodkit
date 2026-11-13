using InvectorModKit.Charting;

namespace InvectorModKit.Charting.Tests;

public class PlayabilityIssuesTests
{
    private static Chart Make(SectionMode mode, Difficulty d, params Note[] notes)
    {
        var chart = new Chart { Bpm = 120, EndTime = 60 };
        chart.Sections.Add(new Section(0, mode));
        chart[d].AddRange(notes);
        return chart;
    }

    private static Chart ListChart(params Note[] notes) => Make(SectionMode.List, Difficulty.Hard, notes);

    private static Note Down(double time, int lane) => new Note(time, NoteKind.Pickup, lane, NoteColor.Down);

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
    public void LaneChangeWithoutTurn_WithTime_IsPlayable()
    {
        Assert.Empty(Playability.Issues(ListChart(Down(10.0, 1), Down(12.0, 2)), Difficulty.Hard));
    }

    [Fact]
    public void LaneChangeWithoutTurn_TooFast_HasModeCodeAndGap()
    {
        var issue = Assert.Single(Playability.Issues(ListChart(Down(10.0, 1), Down(10.30, 2)), Difficulty.Hard));
        Assert.Equal(("lane-time", 10.30, (SectionMode?)SectionMode.List), (issue.Code, issue.Time, issue.Mode));
        Assert.Equal(0.30, issue.Gap, 3);
    }

    [Fact]
    public void CasualTunnel_NeedsMoreTime()
    {
        var issue = Assert.Single(Playability.Issues(Make(SectionMode.List, Difficulty.Casual, Down(10.0, 1), Down(10.40, 2)), Difficulty.Casual));
        Assert.Equal("lane-time", issue.Code);
    }

    [Fact]
    public void Road_TwoLanes_NeedTwiceTheTime()
    {
        var issue = Assert.Single(Playability.Issues(Make(SectionMode.Bounce, Difficulty.Ultra, Down(10.0, 0), Down(10.40, 2)), Difficulty.Ultra));
        Assert.Equal(("lane-time", (SectionMode?)SectionMode.Bounce), (issue.Code, issue.Mode));
        Assert.Empty(Playability.Issues(Make(SectionMode.Bounce, Difficulty.Ultra, Down(10.0, 0), Down(10.50, 2)), Difficulty.Ultra));
    }

    [Fact]
    public void AfterATurn_TheShipStartsOnTheLaneTheTurnLeadsTo()
    {
        Chart RoadUltra(double time, int lane) =>
            Make(SectionMode.Bounce, Difficulty.Ultra, new Note(10.0, NoteKind.Pickup, 1, NoteColor.TurnLeft), Down(time, lane));
        Assert.Empty(Playability.Issues(RoadUltra(10.25, 1), Difficulty.Ultra)); // turned to lane 0, back to 1 in 0.25 s
        Assert.Empty(Playability.Issues(RoadUltra(10.25, 0), Difficulty.Ultra));
        Assert.Equal("lane-time", Assert.Single(Playability.Issues(RoadUltra(10.20, 1), Difficulty.Ultra)).Code);
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

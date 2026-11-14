using System.Linq;
using InvectorModKit.Charting;

namespace InvectorModKit.Charting.Tests;

/// <summary>
/// The game collects one note of each button per frame, and the oldest note still in its timing window shuts out
/// the ones after it. So a second copy of a note (same time, same button) was never collected and made the game
/// ignore every note that followed it closely: a whole fast run went unrecognised. The game's own level editor
/// removes such copies; the chart reader does the same.
/// </summary>
public class DuplicateNoteTests
{
    private static Chart Read(string ultraNotes) =>
        ChartJson.Deserialize("{ \"format\": 1, \"bpm\": 120, \"end\": 100, \"notes\": { \"Ultra\": [ " + ultraNotes + " ] } }");

    [Fact]
    public void TwoCopiesOfAPickup_BecomeOne()
    {
        var notes = Read("{ \"t\": 88.0535, \"color\": 4 }, { \"t\": 88.0535, \"color\": 4 }, { \"t\": 88.1646, \"lane\": 0 }")[Difficulty.Ultra];

        Assert.Equal(new[] { 88.0535, 88.1646 }, notes.Select(n => n.Time));
        Assert.Equal(NoteColor.TurnLeft, notes[0].Color);
    }

    [Fact]
    public void CopiesAFractionOfAMillisecondApart_BecomeOne()
    {
        var notes = Read("{ \"t\": 20.0, \"color\": 3 }, { \"t\": 20.0002, \"color\": 3 }")[Difficulty.Ultra];

        Assert.Single(notes);
    }

    [Fact]
    public void SameButtonOnAnotherLane_StillCountsAsACopy()
    {
        // the game keys its "one per frame" rule on the button only
        var notes = Read("{ \"t\": 30.0, \"lane\": 0, \"color\": 2 }, { \"t\": 30.0, \"lane\": 2, \"color\": 2 }")[Difficulty.Ultra];

        Assert.Single(notes);
        Assert.Equal(0, notes[0].Lane);
    }

    [Fact]
    public void TwoStrumsTogether_BecomeOne_KeepingTheLongestHold()
    {
        var notes = Read("{ \"t\": 40.0, \"strum\": true }, { \"t\": 40.0, \"strum\": true, \"len\": 1.5 }")[Difficulty.Ultra];

        var strum = Assert.Single(notes);
        Assert.Equal(1.5f, strum.Length);
    }

    [Fact]
    public void AStarSetMark_IsKeptFromTheCopyThatHadIt()
    {
        var notes = Read("{ \"t\": 50.0, \"color\": 1 }, { \"t\": 50.0, \"color\": 1, \"set\": 2, \"last\": true }")[Difficulty.Ultra];

        var note = Assert.Single(notes);
        Assert.Equal(2, note.StarSet);
        Assert.True(note.LastInSet);
    }

    [Fact]
    public void AChordOfDifferentButtons_IsKept()
    {
        var notes = Read("{ \"t\": 15.2757, \"color\": 4 }, { \"t\": 15.2757, \"strum\": true, \"len\": 0.444 }, { \"t\": 15.2757, \"color\": 0 }")[Difficulty.Ultra];

        Assert.Equal(3, notes.Count);
    }
}

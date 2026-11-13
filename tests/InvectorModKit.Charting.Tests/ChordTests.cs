using System.Linq;
using InvectorModKit.Charting;

namespace InvectorModKit.Charting.Tests;

/// <summary>
/// The game takes two notes together only when their times are exactly equal: a strum placed on an arrow in the
/// editor but saved 0.1 ms away from it was a separate note, and a key press for it was lost behind the arrow.
/// </summary>
public class ChordTests
{
    private static Chart Read(string ultraNotes) =>
        ChartJson.Deserialize("{ \"format\": 1, \"bpm\": 120, \"end\": 100, \"notes\": { \"Ultra\": [ " + ultraNotes + " ] } }");

    [Fact]
    public void NotesAFractionOfAMillisecondApart_BecomeOneChord()
    {
        var notes = Read("{ \"t\": 179.3349, \"strum\": true }, { \"t\": 179.3348, \"color\": 3 }")[Difficulty.Ultra];

        Assert.Equal(2, notes.Count);
        Assert.Equal(179.3348, notes[0].Time);
        Assert.Equal(notes[0].Time, notes[1].Time);
        Assert.Contains(notes, n => n.Kind == NoteKind.Strum);
        Assert.Contains(notes, n => n.Kind == NoteKind.Pickup && n.Color == NoteColor.Up);
    }

    [Fact]
    public void EveryNoteOfTheChord_TakesTheFirstOnesTime()
    {
        var notes = Read("{ \"t\": 10.0, \"color\": 3 }, { \"t\": 10.0001, \"strum\": true }, { \"t\": 10.0003, \"color\": 4 }")[Difficulty.Ultra];

        Assert.All(notes, n => Assert.Equal(10.0, n.Time));
    }

    [Fact]
    public void NotesFurtherApart_KeepTheirTimes()
    {
        var notes = Read("{ \"t\": 10.0, \"color\": 3 }, { \"t\": 10.01, \"strum\": true }, { \"t\": 10.125, \"color\": 3 }")[Difficulty.Ultra];

        Assert.Equal(new[] { 10.0, 10.01, 10.125 }, notes.Select(n => n.Time));
    }

    [Fact]
    public void ASavedChart_KeepsItsChordsTogether()
    {
        var chart = Read("{ \"t\": 50.2913, \"strum\": true }, { \"t\": 50.2912, \"lane\": 2, \"color\": 3 }");

        var back = ChartJson.Deserialize(ChartJson.Serialize(chart))[Difficulty.Ultra];

        Assert.Equal(back[0].Time, back[1].Time);
    }
}

using System.Collections.Generic;

namespace InvectorModKit.Charting
{
    /// <summary>Game difficulties, in the order the game lists them (Casual is the easiest).</summary>
    public enum Difficulty { Casual, Normal, Hard, Ultra }

    /// <summary>Same values as the game's <c>StrumType</c>.</summary>
    public enum NoteKind { Strum = 0, Pickup = 1 }

    /// <summary>Same values as the game's <c>ModeType</c> (1 is an unused legacy value).</summary>
    public enum SectionMode { List = 0, Bounce = 2, Freefly = 3 }

    /// <summary>
    /// Button a pickup asks for, same values as the game's <c>EntityType</c> for pickups.
    /// Casual charts mostly use Down; each harder difficulty unlocks one more button.
    /// </summary>
    public static class NoteColor
    {
        public const int Left = 0, Right = 1, Down = 2, Up = 3, TurnLeft = 4, TurnRight = 5;
        public static bool IsTurn(int color) => color == TurnLeft || color == TurnRight;
    }

    /// <summary>One chart event. Times are seconds in the song's own audio (before any lead-in padding).</summary>
    public struct Note
    {
        public double Time;
        /// <summary>Hold duration in seconds; only strums can be held.</summary>
        public float Length;
        public NoteKind Kind;
        /// <summary>0..2. List: rotation around the tube; Bounce: left/centre/right; Freefly: position in the air.</summary>
        public int Lane;
        public int Color;
        /// <summary>Power-up star set id (1-based), 0 when the note is not part of a set.</summary>
        public int StarSet;
        public bool LastInSet;

        public Note(double time, NoteKind kind, int lane, int color)
        {
            Time = time; Kind = kind; Lane = lane; Color = color;
            Length = 0f; StarSet = 0; LastInSet = false;
        }

        public override string ToString() => $"{Time:0.000} {Kind} lane={Lane} color={Color} len={Length:0.00} set={StarSet}{(LastInSet ? "!" : "")}";
    }

    public struct Section
    {
        public double Start;
        public SectionMode Mode;

        public Section(double start, SectionMode mode) { Start = start; Mode = mode; }
    }

    /// <summary>Provenance of an automatically generated chart, so stale ones can be regenerated.</summary>
    public sealed class GeneratorStamp
    {
        public int Version;
        /// <summary>Hash of the decoded audio the chart was generated from.</summary>
        public string AudioHash;
    }

    /// <summary>
    /// A complete custom-song chart. All times are in the song's own audio timeline; the loader adds
    /// the lead-in padding the game needs (see <see cref="LeadIn"/>).
    /// </summary>
    public sealed class Chart
    {
        public const int CurrentFormat = 1;

        public int Format = CurrentFormat;
        public double Bpm;
        /// <summary>Time (song seconds) of any beat; the beat grid is FirstBeat + k * 60 / Bpm.</summary>
        public double FirstBeat;
        /// <summary>Time from which notes may appear (the game shows its intro before that).</summary>
        public double StartTime;
        public double EndTime;
        public List<Section> Sections = new List<Section>();
        public Dictionary<Difficulty, List<Note>> Notes = new Dictionary<Difficulty, List<Note>>();
        /// <summary>Null for hand-made charts, which are never regenerated.</summary>
        public GeneratorStamp Generator;

        public double SecondsPerBeat => 60.0 / Bpm;

        public List<Note> this[Difficulty difficulty]
        {
            get
            {
                if (!Notes.TryGetValue(difficulty, out var list))
                {
                    list = new List<Note>();
                    Notes[difficulty] = list;
                }
                return list;
            }
        }

        /// <summary>
        /// Silence to prepend to the audio so that the game's beat grid (which always starts at t=0)
        /// lines up with this song, while leaving at least <paramref name="minimum"/> seconds of intro.
        /// </summary>
        public double LeadIn(double minimum)
        {
            double period = SecondsPerBeat;
            double phase = FirstBeat % period;
            if (phase < 0) phase += period;
            // Need (phase + pad) to be a multiple of the period and pad >= minimum.
            double pad = period - phase;
            if (pad >= period) pad -= period;
            while (pad < minimum) pad += period;
            return pad;
        }
    }
}

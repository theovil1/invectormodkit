using System;
using System.Collections.Generic;

namespace InvectorModKit.Charting
{
    /// <summary>
    /// What the game physically allows, measured on the official charts (5th percentiles where a minimum
    /// is needed). Used by the generator to build charts and by <see cref="Validate"/> to check any chart.
    ///
    /// Key facts: a turn pickup IS the lane change — the next pickup is one lane over in the turn's
    /// direction (List wraps around the tube, Bounce's flat road does not, so a turn never points off
    /// its edge). Freefly is sparse (~0.46 rings/s at every difficulty) with no strums or turns.
    /// </summary>
    public static class Playability
    {
        // Seconds, indexed by Difficulty (Casual, Normal, Hard, Ultra).
        public static readonly double[] MinAfterTurnList = { 0.45, 0.29, 0.15, 0.12 };
        public static readonly double[] MinAfterTurnBounce = { 0.29, 0.24, 0.15, 0.12 };
        public static readonly double[] MinBeforeTurn = { 0.16, 0.15, 0.12, 0.11 };

        /// <summary>Closest two notes ever are in the official charts (1st percentile ~0.115-0.14 s).</summary>
        public const double MinNoteGap = 0.115;
        /// <summary>Closest two consecutive turns are in the official charts.</summary>
        public const double MinTurnGap = 0.23;

        /// <summary>Notes closer than this are a "fast" pair.</summary>
        public const double FastGap = 0.16;
        /// <summary>
        /// Longest run of consecutive fast gaps allowed, by difficulty: about the longest runs of the official
        /// charts at mid tempos (~120 BPM: Casual 1, Normal 3, Hard 6, Ultra 10), kept on the short side.
        /// </summary>
        public static readonly int[] MaxFastRun = { 1, 2, 4, 6 };
        /// <summary>Most fast pairs per minute, by difficulty: the busiest official charts at mid tempos.</summary>
        public static readonly double[] MaxFastPerMinute = { 8, 18, 52, 85 };

        /// <summary>What two consecutive notes ask of the player.</summary>
        public enum Pair { SameArrows, DifferentArrows, ArrowStrum, ArrowTurn, StrumArrow, StrumStrum, StrumTurn, TurnArrow, TurnStrum, TurnTurn }

        /// <summary>
        /// Closest two consecutive notes are in the official charts (1st percentile, outside Freefly), by
        /// pair: two plain pickups need the most time, more again when the arrow changes, while an arrow
        /// next to a turn or a strum (two different gestures) can come much sooner. At 120 BPM, two quarter
        /// beats (0.125 s) apart can be an arrow and a turn, never two different arrows. [difficulty][pair]
        /// </summary>
        private static readonly double[][] MinPairGaps =
        {
            new[] { 0.140, 0.140, 0.140, 0.150, 0.150, 0.175, 0.235, 0.165, 0.235, 0.285 },
            new[] { 0.115, 0.160, 0.120, 0.135, 0.140, 0.115, 0.150, 0.150, 0.160, 0.240 },
            new[] { 0.135, 0.150, 0.115, 0.110, 0.120, 0.115, 0.115, 0.120, 0.115, 0.230 },
            new[] { 0.135, 0.140, 0.115, 0.110, 0.115, 0.115, 0.115, 0.110, 0.115, 0.230 },
        };

        public static Pair PairOf(Note a, Note b)
        {
            int ka = KindIndex(a), kb = KindIndex(b);
            if (ka == 0 && kb == 0) return a.Color == b.Color ? Pair.SameArrows : Pair.DifferentArrows;
            switch (ka * 3 + kb)
            {
                case 1: return Pair.ArrowStrum;
                case 2: return Pair.ArrowTurn;
                case 3: return Pair.StrumArrow;
                case 4: return Pair.StrumStrum;
                case 5: return Pair.StrumTurn;
                case 6: return Pair.TurnArrow;
                case 7: return Pair.TurnStrum;
                default: return Pair.TurnTurn;
            }
        }

        private static int KindIndex(Note n) => n.Kind == NoteKind.Strum ? 1 : NoteColor.IsTurn(n.Color) ? 2 : 0;

        public static double MinPairGap(Difficulty d, Pair pair) => MinPairGaps[(int)d][(int)pair];
        public static double MinPairGap(Difficulty d, Note a, Note b) => MinPairGap(d, PairOf(a, b));

        /// <summary>Smallest gap any two notes of this difficulty may have, whatever they are.</summary>
        public static double MinNoteGapFor(Difficulty d)
        {
            double min = double.MaxValue;
            foreach (double g in MinPairGaps[(int)d]) min = Math.Min(min, g);
            return Math.Max(MinNoteGap, min);
        }

        public const double FreeflyRingsPerSecond = 0.46;
        public const double FreeflyMinGap = 0.70;
        public const double FreeflyFirstRingDelay = 1.2;

        public static double MinAfterTurn(SectionMode mode, Difficulty d) =>
            mode == SectionMode.Bounce ? MinAfterTurnBounce[(int)d] : MinAfterTurnList[(int)d];

        /// <summary>Can a turn in this direction be placed on this lane?</summary>
        public static bool TurnAllowed(SectionMode mode, int lane, int turnColor)
        {
            if (mode == SectionMode.Freefly) return false;
            if (mode == SectionMode.Bounce)
                return turnColor == NoteColor.TurnLeft ? lane > 0 : lane < 2;
            return true;
        }

        /// <summary>Lane of the pickups following a turn.</summary>
        public static int LaneAfterTurn(SectionMode mode, int lane, int turnColor)
        {
            int delta = turnColor == NoteColor.TurnLeft ? -1 : 1;
            return mode == SectionMode.List ? (lane + delta + 3) % 3 : Math.Max(0, Math.Min(2, lane + delta));
        }

        public static SectionMode ModeAt(Chart chart, double time)
        {
            var mode = chart.Sections.Count > 0 ? chart.Sections[0].Mode : SectionMode.List;
            foreach (var s in chart.Sections) if (time >= s.Start - 1e-6) mode = s.Mode;
            return mode;
        }

        /// <summary>Problems a player would hit, one line each (empty = playable).</summary>
        public static List<string> Validate(Chart chart, Difficulty d) => Issues(chart, d).ConvertAll(i => i.ToString());

        /// <summary>Problems a player would hit, with the time of the note concerned (empty = playable).</summary>
        public static List<PlayabilityIssue> Issues(Chart chart, Difficulty d)
        {
            var issues = new List<PlayabilityIssue>();
            void Add(double time, string code, SectionMode? mode, double gap, string detail, string text) =>
                issues.Add(new PlayabilityIssue { Time = time, Code = code, Mode = mode, Gap = gap, Detail = detail, Text = text });
            var notes = chart[d];
            for (int k = 1; k < notes.Count; k++)
            {
                double gap = notes[k].Time - notes[k - 1].Time;
                if (gap > 1e-4 && gap < MinNoteGap - 0.01) Add(notes[k].Time, "gap", null, gap, null, $": notes only {gap:0.000}s apart");
                else if (ModeAt(chart, notes[k].Time) != SectionMode.Freefly && ModeAt(chart, notes[k - 1].Time) != SectionMode.Freefly
                         && gap < MinPairGap(d, notes[k - 1], notes[k]) - 0.005)
                    Add(notes[k].Time, "pair", null, gap, PairOf(notes[k - 1], notes[k]).ToString(), $": {PairOf(notes[k - 1], notes[k])} only {gap:0.000}s apart");
            }
            Note? prevTurn = null;
            foreach (var n in notes)
            {
                if (n.Kind != NoteKind.Pickup || !NoteColor.IsTurn(n.Color)) continue;
                if (prevTurn.HasValue && n.Time - prevTurn.Value.Time < MinTurnGap - 0.01)
                    Add(n.Time, "turns", null, n.Time - prevTurn.Value.Time, null, $": turns only {n.Time - prevTurn.Value.Time:0.000}s apart");
                prevTurn = n;
            }
            Note? prev = null;
            SectionMode prevMode = SectionMode.List;
            foreach (var n in notes)
            {
                var mode = ModeAt(chart, n.Time);
                if (mode == SectionMode.Freefly)
                {
                    if (n.Kind == NoteKind.Strum || NoteColor.IsTurn(n.Color)) Add(n.Time, "freefly-note", mode, 0, null, " Freefly: strum/turn (invisible or impossible)");
                }
                if (n.Kind != NoteKind.Pickup) continue;
                if (NoteColor.IsTurn(n.Color) && !TurnAllowed(mode, n.Lane, n.Color)) Add(n.Time, "turn-off-track", mode, 0, n.Lane.ToString(), $" {mode}: turn points off the track (lane {n.Lane})");
                if (prev.HasValue && prevMode == mode)
                {
                    var p = prev.Value;
                    double gap = n.Time - p.Time;
                    if (mode == SectionMode.Freefly)
                    {
                        if (gap < 0.45) Add(n.Time, "rings", mode, gap, null, $" Freefly: rings {gap:0.00}s apart");
                        if (n.Lane != p.Lane && gap < 2.5) Add(n.Time, "freefly-move", mode, gap, null, $" Freefly: position change in {gap:0.00}s");
                    }
                    else if (NoteColor.IsTurn(p.Color))
                    {
                        if (n.Lane != LaneAfterTurn(mode, p.Lane, p.Color)) Add(n.Time, "turn-lane", mode, gap, null, $" {mode}: lane does not follow the turn");
                        if (gap < MinAfterTurn(mode, d) - 1e-3) Add(n.Time, "turn-time", mode, gap, null, $" {mode}: only {gap:0.00}s to move after a turn");
                    }
                    else if (n.Lane != p.Lane) Add(n.Time, "lane-change", mode, gap, null, $" {mode}: lane change without a turn");
                }
                prev = n;
                prevMode = mode;
            }
            issues.Sort((a, b) => a.Time.CompareTo(b.Time));
            return issues;
        }
    }

    /// <summary>One problem found by <see cref="Playability.Issues"/>.</summary>
    public sealed class PlayabilityIssue
    {
        /// <summary>Song time of the note concerned.</summary>
        public double Time;
        /// <summary>
        /// Which rule (stable, for translations): gap, pair, turns, freefly-note, turn-off-track, rings,
        /// freefly-move, turn-lane, turn-time, lane-change.
        /// </summary>
        public string Code;
        public SectionMode? Mode;
        /// <summary>Seconds between the notes concerned, when the rule is about a gap.</summary>
        public double Gap;
        /// <summary>Pair of notes (pair) or lane (turn-off-track).</summary>
        public string Detail;
        /// <summary>English description, as written after the time.</summary>
        public string Text;

        public override string ToString() => $"{Time:0.00}s{Text}";
    }
}

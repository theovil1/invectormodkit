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
        public static List<string> Validate(Chart chart, Difficulty d)
        {
            var issues = new List<string>();
            var notes = chart[d];
            Note? prev = null;
            SectionMode prevMode = SectionMode.List;
            foreach (var n in notes)
            {
                var mode = ModeAt(chart, n.Time);
                if (mode == SectionMode.Freefly)
                {
                    if (n.Kind == NoteKind.Strum || NoteColor.IsTurn(n.Color)) issues.Add($"{n.Time:0.00}s Freefly: strum/turn (invisible or impossible)");
                }
                if (n.Kind != NoteKind.Pickup) continue;
                if (NoteColor.IsTurn(n.Color) && !TurnAllowed(mode, n.Lane, n.Color)) issues.Add($"{n.Time:0.00}s {mode}: turn points off the track (lane {n.Lane})");
                if (prev.HasValue && prevMode == mode)
                {
                    var p = prev.Value;
                    double gap = n.Time - p.Time;
                    if (mode == SectionMode.Freefly)
                    {
                        if (gap < 0.45) issues.Add($"{n.Time:0.00}s Freefly: rings {gap:0.00}s apart");
                        if (n.Lane != p.Lane && gap < 2.5) issues.Add($"{n.Time:0.00}s Freefly: position change in {gap:0.00}s");
                    }
                    else if (NoteColor.IsTurn(p.Color))
                    {
                        if (n.Lane != LaneAfterTurn(mode, p.Lane, p.Color)) issues.Add($"{n.Time:0.00}s {mode}: lane does not follow the turn");
                        if (gap < MinAfterTurn(mode, d) - 1e-3) issues.Add($"{n.Time:0.00}s {mode}: only {gap:0.00}s to move after a turn");
                    }
                    else if (n.Lane != p.Lane) issues.Add($"{n.Time:0.00}s {mode}: lane change without a turn");
                }
                prev = n;
                prevMode = mode;
            }
            return issues;
        }
    }
}

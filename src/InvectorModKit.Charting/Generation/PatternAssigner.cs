using System;
using System.Collections.Generic;

namespace InvectorModKit.Charting.Generation
{
    /// <summary>
    /// Turns chosen rhythms into playable notes: strums and holds, lanes, turns, colours and star sets,
    /// following the statistics of the official charts. Attributes shared by all difficulties
    /// (kind, hold, lane, turn) are decided once on the Ultra rhythm; colours depend on the
    /// buttons each difficulty uses.
    /// </summary>
    internal sealed class PatternAssigner
    {
        // Share of Ultra notes that are strums, by metric level (bar, beat, half, quarter).
        private static readonly double[] StrumByLevel = { 0.53, 0.30, 0.13, 0.09 };
        private const double StrumShare = 0.23;
        private const double HoldShareOfStrums = 0.10;
        private const double TurnShare = 0.28;
        private const double TurnSameDirection = 0.42;

        // P(lane stays the same) by mode and current lane.
        private static readonly double[] StayList = { 0.75, 0.80, 0.73 };
        private static readonly double[] StayBounce = { 0.61, 0.82, 0.62 };
        private static readonly double[] StayFreefly = { 0.60, 0.95, 0.60 };

        // P(same colour as previous pickup) by difficulty and gap bucket (1q, 2q, 3-4q, >1 beat).
        private static readonly double[][] SameColour =
        {
            new[] { 1.0, 1.0, 1.0, 1.0 },
            new[] { 0.83, 0.54, 0.32, 0.43 },
            new[] { 0.46, 0.23, 0.20, 0.34 },
            new[] { 0.23, 0.12, 0.14, 0.29 },
        };
        private static readonly int[][] Buttons =
        {
            new[] { NoteColor.Down },
            new[] { NoteColor.Left, NoteColor.Down },
            new[] { NoteColor.Left, NoteColor.Right, NoteColor.Down },
            new[] { NoteColor.Left, NoteColor.Right, NoteColor.Down, NoteColor.Up },
        };

        private readonly BeatGrid _grid;
        private readonly List<Section> _sections;
        private readonly Rng _rng;

        /// <summary>Per grid index: attributes decided on the Ultra rhythm.</summary>
        private readonly Dictionary<int, Shared> _shared = new Dictionary<int, Shared>();

        private struct Shared
        {
            public NoteKind Kind;
            public float Length;
            public int Lane;
            public int Turn; // 0 none, else NoteColor.TurnLeft / TurnRight
            public SectionMode Mode;
        }

        public PatternAssigner(BeatGrid grid, List<Section> sections, Rng rng)
        {
            _grid = grid; _sections = sections; _rng = rng;
        }

        /// <param name="rhythms">Grid indices per difficulty; each one a subset of the next (index 3 = Ultra).</param>
        public void AssignShared(List<int>[] rhythms)
        {
            var ultra = rhythms[(int)Difficulty.Ultra];
            var modes = new SectionMode[ultra.Count];
            for (int k = 0; k < ultra.Count; k++) modes[k] = ModeAt(_grid.TimeOf(ultra[k]));

            // Kinds, from Casual up so a note keeps its kind in every difficulty while each
            // difficulty still gets the official ~23% strums. Strums favour bar lines and accents,
            // and never go in Freefly (the game does not show them there).
            var kindOf = new Dictionary<int, NoteKind>();
            foreach (var notes in rhythms)
            {
                int inheritedStrums = 0;
                var fresh = new List<(int i, double w)>();
                foreach (int i in notes)
                {
                    if (kindOf.TryGetValue(i, out var kind)) { if (kind == NoteKind.Strum) inheritedStrums++; continue; }
                    bool freefly = ModeAt(_grid.TimeOf(i)) == SectionMode.Freefly;
                    fresh.Add((i, freefly ? 0 : StrumByLevel[BeatGrid.Level(i)] * (0.6 + 0.8 * _grid.X[i][BeatGrid.FProminence])));
                }
                double target = Math.Max(0, StrumShare * notes.Count - inheritedStrums);
                double scale = ScaleToSum(fresh, target);
                foreach (var (i, w) in fresh)
                    kindOf[i] = _rng.Chance(Math.Min(1, w * scale)) ? NoteKind.Strum : NoteKind.Pickup;
            }
            var kinds = new NoteKind[ultra.Count];
            for (int k = 0; k < ultra.Count; k++) kinds[k] = kindOf[ultra[k]];

            // Holds: the most sustained strums followed by at least one beat without notes.
            var lengths = new float[ultra.Count];
            var holdCandidates = new List<(int k, double score, int quarters)>();
            int strums = 0;
            for (int k = 0; k < ultra.Count; k++)
            {
                if (kinds[k] != NoteKind.Strum) continue;
                strums++;
                if (k + 1 >= ultra.Count) continue;
                int gap = ultra[k + 1] - ultra[k];
                if (gap < 4) continue;
                double sustain = 0, activity = 0;
                for (int i = ultra[k] + 1; i < ultra[k + 1]; i++)
                {
                    sustain += _grid.Loudness[i];
                    activity += _grid.X[i][BeatGrid.FOnset];
                }
                sustain /= gap - 1; activity /= gap - 1;
                double score = sustain / Math.Max(0.05, _grid.Loudness[ultra[k]]) - activity;
                int quarters = Math.Min(14, gap - (gap >= 8 ? 2 : 1));
                if (quarters >= 3) holdCandidates.Add((k, score, quarters));
            }
            holdCandidates.Sort((a, b) => b.score.CompareTo(a.score));
            int holds = (int)Math.Round(strums * HoldShareOfStrums);
            for (int h = 0; h < Math.Min(holds, holdCandidates.Count); h++)
            {
                if (holdCandidates[h].score < 0.5) break;
                lengths[holdCandidates[h].k] = (float)(holdCandidates[h].quarters * _grid.Quarter);
            }

            // Lanes and turns walk the pickups in order.
            int lane = 1, lastTurnIndex = -1000, lastTurn = 0;
            for (int k = 0; k < ultra.Count; k++)
            {
                int i = ultra[k];
                var s = new Shared { Kind = kinds[k], Length = lengths[k], Lane = lane, Mode = modes[k] };
                if (kinds[k] == NoteKind.Pickup)
                {
                    int level = BeatGrid.Level(i);
                    bool canTurn = modes[k] != SectionMode.Freefly && i - lastTurnIndex >= 4;
                    double prominence = _grid.X[i][BeatGrid.FProminence];
                    if (canTurn && _rng.Chance(TurnShare * (1.4 - 0.8 * prominence)))
                    {
                        lastTurn = lastTurn == 0 ? (_rng.Chance(0.5) ? NoteColor.TurnLeft : NoteColor.TurnRight)
                                 : _rng.Chance(TurnSameDirection) ? lastTurn
                                 : lastTurn == NoteColor.TurnLeft ? NoteColor.TurnRight : NoteColor.TurnLeft;
                        s.Turn = lastTurn;
                        lastTurnIndex = i;
                    }
                    else
                    {
                        var stay = modes[k] == SectionMode.List ? StayList : modes[k] == SectionMode.Bounce ? StayBounce : StayFreefly;
                        double pStay = stay[lane];
                        // Lane changes land on stronger metric positions.
                        if (level <= 1) pStay = 1 - (1 - pStay) * 1.3; else if (level == 3) pStay = 1 - (1 - pStay) * 0.6;
                        if (!_rng.Chance(pStay))
                        {
                            int to = lane == 1 ? (_rng.Chance(0.5) ? 0 : 2) : (_rng.Chance(0.6) ? 1 : 2 - lane);
                            lane = to;
                        }
                        s.Lane = lane;
                    }
                }
                _shared[i] = s;
            }
        }

        public List<Note> Build(Difficulty d, List<int> rhythm)
        {
            var notes = new List<Note>(rhythm.Count);
            int prevIndex = int.MinValue, colour = Buttons[(int)d][_rng.Next(Buttons[(int)d].Length)];
            foreach (int i in rhythm)
            {
                var s = _shared[i];
                var n = new Note(_grid.TimeOf(i), s.Kind, s.Lane, NoteColor.Down) { Length = s.Kind == NoteKind.Strum ? s.Length : 0f };
                if (s.Kind == NoteKind.Pickup)
                {
                    if (s.Turn != 0) n.Color = s.Turn;
                    else if (s.Mode == SectionMode.Freefly) n.Color = NoteColor.Down;
                    else
                    {
                        int gap = i - prevIndex;
                        int bucket = gap <= 1 ? 0 : gap == 2 ? 1 : gap <= 4 ? 2 : 3;
                        var buttons = Buttons[(int)d];
                        if (buttons.Length > 1 && !_rng.Chance(SameColour[(int)d][bucket]))
                        {
                            int c;
                            do c = buttons[_rng.Next(buttons.Length)]; while (c == colour);
                            colour = c;
                        }
                        n.Color = colour;
                        prevIndex = i;
                    }
                }
                notes.Add(n);
            }
            AssignStarSets(notes);
            return notes;
        }

        /// <summary>About one set of 4 consecutive coloured pickups every ~30 s, spread over the song.</summary>
        private void AssignStarSets(List<Note> notes)
        {
            if (notes.Count < 8) return;
            double first = notes[0].Time, last = notes[notes.Count - 1].Time;
            int sets = Math.Max(3, Math.Min(8, (int)Math.Round((last - first) / 30)));
            int searchFrom = 0;
            for (int s = 0; s < sets; s++)
            {
                double center = first + (s + 0.5) / sets * (last - first);
                int bestStart = -1;
                double bestDist = double.MaxValue;
                for (int k = searchFrom; k + 3 < notes.Count; k++)
                {
                    bool ok = true;
                    for (int j = k; j < k + 4 && ok; j++)
                        ok = notes[j].Kind == NoteKind.Pickup && !NoteColor.IsTurn(notes[j].Color) && ModeAt(notes[j].Time) != SectionMode.Freefly;
                    if (!ok) continue;
                    double dist = Math.Abs(notes[k].Time - center);
                    if (dist < bestDist) { bestDist = dist; bestStart = k; }
                    else if (notes[k].Time > center) break;
                }
                if (bestStart < 0) continue;
                for (int j = bestStart; j < bestStart + 4; j++)
                {
                    var n = notes[j];
                    n.StarSet = s + 1;
                    n.LastInSet = j == bestStart + 3;
                    notes[j] = n;
                }
                searchFrom = bestStart + 4;
            }
        }

        /// <summary>Factor s such that Σ min(1, s·w) ≈ target.</summary>
        private static double ScaleToSum(List<(int i, double w)> items, double target)
        {
            double lo = 0, hi = 1000;
            for (int it = 0; it < 50; it++)
            {
                double mid = (lo + hi) / 2, sum = 0;
                foreach (var (_, w) in items) sum += Math.Min(1, w * mid);
                if (sum < target) lo = mid; else hi = mid;
            }
            return (lo + hi) / 2;
        }

        private SectionMode ModeAt(double t)
        {
            var m = _sections.Count > 0 ? _sections[0].Mode : SectionMode.List;
            foreach (var s in _sections) if (t >= s.Start - 1e-6) m = s.Mode;
            return m;
        }
    }
}

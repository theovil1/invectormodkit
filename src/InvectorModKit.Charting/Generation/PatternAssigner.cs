using System;
using System.Collections.Generic;

namespace InvectorModKit.Charting.Generation
{
    /// <summary>
    /// Turns chosen rhythms into playable notes, following the official charts:
    /// <list type="bullet">
    /// <item>Kinds: ~23% strums per difficulty, favouring bar lines and accents; a note keeps its kind in every difficulty. Holds are strums.</item>
    /// <item>Lanes only change through turn pickups (the turn is the lane change), with enough time to move afterwards; Bounce turns never point off the road.</item>
    /// <item>Colours: a per-difficulty Markov chain over the buttons that difficulty uses.</item>
    /// <item>Freefly: rings only, one position per section.</item>
    /// </list>
    /// </summary>
    internal sealed class PatternAssigner
    {
        // Share of notes that are strums, by metric level (bar, beat, half, quarter), before scaling to StrumShare.
        private static readonly double[] StrumByLevel = { 0.53, 0.30, 0.13, 0.09 };
        private const double StrumShare = 0.23;
        // Chance that an eligible pickup becomes a turn, by difficulty (tuned so ~21% of pickups are turns).
        private static readonly double[] TurnChance = { 0.34, 0.33, 0.26, 0.24 };
        private const double TurnSameDirection = 0.42;

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
        private readonly Func<int, double> _timeOf;
        private readonly int[] _freeflyLane;
        private readonly Dictionary<int, NoteKind> _kind = new Dictionary<int, NoteKind>();
        private readonly Dictionary<int, float> _holdLength = new Dictionary<int, float>();

        public PatternAssigner(BeatGrid grid, List<Section> sections, Rng rng, Func<int, double> timeOf)
        {
            _grid = grid; _sections = sections; _rng = rng; _timeOf = timeOf;
            _freeflyLane = new int[sections.Count];
            for (int s = 0; s < sections.Count; s++)
            {
                double r = rng.NextDouble();
                _freeflyLane[s] = r < 0.8 ? 1 : r < 0.9 ? 0 : 2;
            }
        }

        /// <param name="rhythms">Grid indices per difficulty; each one a subset of the next (index 3 = Ultra).</param>
        public void AssignKinds(List<int>[] rhythms, List<HoldRegion> holds, HashSet<int> rings)
        {
            foreach (var h in holds)
            {
                _kind[h.Start] = NoteKind.Strum;
                _holdLength[h.Start] = (float)(_grid.TimeOf(h.End) - _timeOf(h.Start));
            }
            foreach (int r in rings) _kind[r] = NoteKind.Pickup;

            // From Casual up, so a note keeps its kind everywhere while each difficulty still gets ~23% strums.
            foreach (var notes in rhythms)
            {
                int strums = 0;
                var fresh = new List<(int i, double w)>();
                foreach (int i in notes)
                {
                    if (_kind.TryGetValue(i, out var kind)) { if (kind == NoteKind.Strum) strums++; continue; }
                    fresh.Add((i, StrumByLevel[BeatGrid.Level(i)] * (0.6 + 0.8 * _grid.X[i][BeatGrid.FProminence])));
                }
                double scale = ScaleToSum(fresh, Math.Max(0, StrumShare * notes.Count - strums));
                foreach (var (i, w) in fresh)
                    _kind[i] = _rng.Chance(Math.Min(1, w * scale)) ? NoteKind.Strum : NoteKind.Pickup;
            }
        }

        public List<Note> Build(Difficulty d, List<int> rhythm)
        {
            var notes = new List<Note>(rhythm.Count);
            var index = new List<int>(rhythm.Count);
            foreach (int i in rhythm)
            {
                var kind = _kind[i];
                notes.Add(new Note(_timeOf(i), kind, 1, NoteColor.Down) { Length = kind == NoteKind.Strum && _holdLength.TryGetValue(i, out var len) ? len : 0f });
                index.Add(i);
            }
            AssignLanesAndTurns(d, notes, index);
            AssignColours(d, notes);
            AssignStarSets(notes);
            return notes;
        }

        private void AssignLanesAndTurns(Difficulty d, List<Note> notes, List<int> index)
        {
            // Pickups grouped by section (lanes reset at every section).
            int lane = 1, section = -1, lastTurn = 0;
            double lastTurnTime = double.MinValue;
            for (int k = 0; k < notes.Count; k++)
            {
                var n = notes[k];
                int s = SectionAt(n.Time);
                var mode = _sections.Count > 0 ? _sections[Math.Max(0, s)].Mode : SectionMode.List;
                if (s != section)
                {
                    section = s;
                    lane = mode == SectionMode.Freefly ? _freeflyLane[Math.Max(0, s)] : 1;
                    lastTurn = 0;
                    lastTurnTime = double.MinValue;
                }
                n.Lane = lane;
                if (n.Kind == NoteKind.Pickup && mode != SectionMode.Freefly)
                {
                    double prevGap = double.MaxValue, nextGap = -1;
                    for (int j = k - 1; j >= 0; j--)
                        if (notes[j].Kind == NoteKind.Pickup) { if (SectionAt(notes[j].Time) == s) prevGap = n.Time - notes[j].Time; break; }
                    for (int j = k + 1; j < notes.Count; j++)
                        if (notes[j].Kind == NoteKind.Pickup) { if (SectionAt(notes[j].Time) == s) nextGap = notes[j].Time - n.Time; break; }

                    bool eligible = nextGap >= Playability.MinAfterTurn(mode, d) && prevGap >= Playability.MinBeforeTurn[(int)d]
                                    && n.Time - lastTurnTime >= Playability.MinTurnGap;
                    double prominence = _grid.X[index[k]][BeatGrid.FProminence];
                    if (eligible && _rng.Chance(TurnChance[(int)d] * (1.25 - 0.5 * prominence)))
                    {
                        int preferred = lastTurn == 0 ? (_rng.Chance(0.5) ? NoteColor.TurnLeft : NoteColor.TurnRight)
                                      : _rng.Chance(TurnSameDirection) ? lastTurn : Opposite(lastTurn);
                        int dir = Playability.TurnAllowed(mode, lane, preferred) ? preferred
                                : Playability.TurnAllowed(mode, lane, Opposite(preferred)) ? Opposite(preferred) : 0;
                        if (dir != 0)
                        {
                            n.Color = dir;
                            lane = Playability.LaneAfterTurn(mode, lane, dir);
                            lastTurn = dir;
                            lastTurnTime = n.Time;
                        }
                    }
                }
                notes[k] = n;
            }
        }

        private static int Opposite(int turn) => turn == NoteColor.TurnLeft ? NoteColor.TurnRight : NoteColor.TurnLeft;

        private void AssignColours(Difficulty d, List<Note> notes)
        {
            var buttons = Buttons[(int)d];
            int colour = buttons[_rng.Next(buttons.Length)];
            double prevTime = double.MinValue;
            for (int k = 0; k < notes.Count; k++)
            {
                var n = notes[k];
                if (n.Kind != NoteKind.Pickup || NoteColor.IsTurn(n.Color)) continue;
                if (ModeAt(n.Time) == SectionMode.Freefly) { n.Color = NoteColor.Down; notes[k] = n; continue; }
                int gap = prevTime == double.MinValue ? 99 : (int)Math.Round((n.Time - prevTime) / _grid.Quarter);
                int bucket = gap <= 1 ? 0 : gap == 2 ? 1 : gap <= 4 ? 2 : 3;
                if (buttons.Length > 1 && !_rng.Chance(SameColour[(int)d][bucket]))
                {
                    int c;
                    do c = buttons[_rng.Next(buttons.Length)]; while (c == colour);
                    colour = c;
                }
                n.Color = colour;
                notes[k] = n;
                prevTime = n.Time;
            }
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

        private int SectionAt(double t)
        {
            int s = -1;
            for (int k = 0; k < _sections.Count; k++) if (t >= _sections[k].Start - 1e-6) s = k;
            return s;
        }

        private SectionMode ModeAt(double t)
        {
            int s = SectionAt(t);
            return s < 0 ? (_sections.Count > 0 ? _sections[0].Mode : SectionMode.List) : _sections[s].Mode;
        }
    }
}

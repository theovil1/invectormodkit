using System;
using System.Collections.Generic;
using System.Linq;
using InvectorModKit.Charting.Audio;

namespace InvectorModKit.Charting.Generation
{
    public sealed class GeneratorOptions
    {
        /// <summary>How strongly the official gap distribution shapes rhythms (0 = onsets only, measured best).</summary>
        public double GapWeight = 0.0;
        /// <summary>Multiplier on the model's expected note count, per difficulty.</summary>
        public double[] Density = { 1.0, 1.0, 1.0, 1.0 };
        /// <summary>Move notes onto the actual attack when one is within this many seconds of the grid (0 = off).</summary>
        public double SnapToOnset = 0.025;
    }

    /// <summary>Builds a complete four-difficulty chart from decoded audio.</summary>
    public static class ChartGenerator
    {
        /// <summary>Bump whenever generation changes, so cached auto-generated charts are rebuilt.</summary>
        public const int Version = 3;

        /// <summary>Sections start this much before their bar line so notes snapped slightly early stay inside.</summary>
        private const double SectionLead = 0.05;

        public static Chart Generate(float[] mono, int sampleRate, string audioHash, GeneratorOptions options = null)
        {
            var f = AudioAnalyzer.Analyze(mono, sampleRate);
            return Generate(f, TempoEstimator.Estimate(f), audioHash, options);
        }

        public static Chart Generate(AudioFeatures f, TempoEstimate tempo, string audioHash, GeneratorOptions options = null)
        {
            options = options ?? new GeneratorOptions();
            var rng = Rng.FromString(audioHash);
            var (songStart, songEnd) = AudibleRange(f);

            var grid = BeatGrid.Build(f, tempo.Bpm, tempo.FirstBeat, songStart, songEnd);
            var sections = StructureAnalyzer.Analyze(f, grid, songStart, songEnd, rng);
            for (int s = 1; s < sections.Count; s++) sections[s] = new Section(sections[s].Start - SectionLead, sections[s].Mode);
            bool Freefly(int i) => ModeAt(sections, grid.TimeOf(i)) == SectionMode.Freefly;

            int from = Math.Max(0, grid.IndexOf(songStart)), to = Math.Min(grid.Count - 1, grid.IndexOf(songEnd - 0.5));
            bool Audible(int i) => grid.X[i][BeatGrid.FSilent] < 0.5f;

            var expected = new double[4];
            for (int d = 0; d < 4; d++)
                for (int i = from; i <= to; i++)
                    if (!Freefly(i)) expected[d] += NoteModel.Probability((Difficulty)d, BeatGrid.Level(i), grid.X[i]) * options.Density[d];

            // Holds come first: the same sustained notes are held in every difficulty, and nothing else
            // may be played while a hold lasts.
            var holds = HoldDetector.Find(f, grid, i => i >= from && i <= to && !Freefly(i) && Audible(i), expected[(int)Difficulty.Ultra]);
            var blocked = new HashSet<int>();
            var holdStarts = new HashSet<int>();
            foreach (var h in holds)
            {
                holdStarts.Add(h.Start);
                for (int i = h.Start + 1; i <= h.End; i++) blocked.Add(i);
            }

            // Ultra first, over every audible grid position outside Freefly; each easier difficulty keeps
            // a subset of the next harder one (as in the official charts, where 99% of notes are shared).
            // Official charts never put two notes closer than ~0.115 s, whatever the tempo
            // (at fast tempos that rules out runs of quarter beats).
            int minGap = Math.Max(1, (int)Math.Ceiling(Playability.MinNoteGap / grid.Quarter - 1e-6));
            var rhythms = new List<int>[4];
            int[] candidates = Enumerable.Range(from, Math.Max(0, to - from + 1))
                .Where(i => Audible(i) && !Freefly(i) && !blocked.Contains(i)).ToArray();
            for (int d = (int)Difficulty.Ultra; d >= 0; d--)
            {
                var diff = (Difficulty)d;
                var score = new double[candidates.Length];
                for (int k = 0; k < candidates.Length; k++)
                    score[k] = holdStarts.Contains(candidates[k]) ? 30 : NoteModel.Logit(diff, BeatGrid.Level(candidates[k]), grid.X[candidates[k]]);
                int target = (int)Math.Round(expected[d]) + holds.Count;
                rhythms[d] = RhythmDecoder.Decode(candidates, score, NoteModel.GapLogPrior(diff), options.GapWeight, target, minGap);
                foreach (int h in holdStarts) if (!rhythms[d].Contains(h)) rhythms[d].Add(h);
                rhythms[d].Sort();
                candidates = rhythms[d].ToArray();
            }

            // Freefly: the same sparse rings in every difficulty.
            var rings = FreeflyRings(grid, sections, songEnd);
            for (int d = 0; d < 4; d++)
            {
                rhythms[d].AddRange(rings);
                rhythms[d].Sort();
            }

            // Snap to attacks, but never closer than the minimum gap to the previous note.
            var times = new Dictionary<int, double>();
            double previous = double.MinValue;
            foreach (int i in rhythms[(int)Difficulty.Ultra])
            {
                double t = SnappedTime(f, grid, i, options.SnapToOnset);
                if (t - previous < Playability.MinNoteGap) t = Math.Max(grid.TimeOf(i), previous + Playability.MinNoteGap);
                times[i] = t;
                previous = t;
            }
            double TimeOf(int i) => times.TryGetValue(i, out var t) ? t : grid.TimeOf(i);

            var patterns = new PatternAssigner(grid, sections, rng, TimeOf);
            patterns.AssignKinds(rhythms, holds, new HashSet<int>(rings));
            var chart = new Chart
            {
                Bpm = tempo.Bpm,
                FirstBeat = tempo.FirstBeat,
                StartTime = songStart,
                EndTime = songEnd,
                PreviewStart = PreviewStart(grid, songStart, songEnd),
                Sections = sections,
                Generator = new GeneratorStamp { Version = Version, AudioHash = audioHash },
            };
            for (int d = 0; d < 4; d++) chart.Notes[(Difficulty)d] = patterns.Build((Difficulty)d, rhythms[d]);
            return chart;
        }

        private static SectionMode ModeAt(List<Section> sections, double t)
        {
            var m = sections.Count > 0 ? sections[0].Mode : SectionMode.List;
            foreach (var s in sections) if (t >= s.Start - 1e-6) m = s.Mode;
            return m;
        }

        /// <summary>
        /// Official Freefly sections hold ~0.46 rings per second at every difficulty, at least 0.7 s apart,
        /// the first one ~1.2 s in. Rings go on the strongest beats.
        /// </summary>
        private static List<int> FreeflyRings(BeatGrid grid, List<Section> sections, double songEnd)
        {
            var rings = new List<int>();
            for (int s = 0; s < sections.Count; s++)
            {
                if (sections[s].Mode != SectionMode.Freefly) continue;
                double start = sections[s].Start, end = s + 1 < sections.Count ? sections[s + 1].Start : songEnd;
                int a = Math.Max(0, grid.IndexOf(start + Playability.FreeflyFirstRingDelay)), b = Math.Min(grid.Count - 1, grid.IndexOf(end - 0.4));
                int count = (int)Math.Round(Playability.FreeflyRingsPerSecond * (end - start));
                var options = new List<(int i, double score)>();
                for (int i = a; i <= b; i++)
                {
                    int level = BeatGrid.Level(i);
                    if (level == 3 || grid.X[i][BeatGrid.FSilent] > 0.5f) continue;
                    options.Add((i, grid.X[i][BeatGrid.FOnset] + (level == 0 ? 0.5 : level == 1 ? 0.3 : 0)));
                }
                var chosen = new List<int>();
                foreach (var (i, _) in options.OrderByDescending(o => o.score))
                {
                    if (chosen.Count >= count) break;
                    if (chosen.All(c => Math.Abs(grid.TimeOf(c) - grid.TimeOf(i)) >= Playability.FreeflyMinGap)) chosen.Add(i);
                }
                rings.AddRange(chosen);
            }
            rings.Sort();
            return rings;
        }

        /// <summary>Live-played music drifts around the grid: put the note on the attack when there is a clear one nearby.</summary>
        private static double SnappedTime(AudioFeatures f, BeatGrid grid, int i, double window)
        {
            double t = grid.TimeOf(i);
            if (window <= 0) return t;
            int c = f.FrameOf(t), r = Math.Max(1, (int)Math.Round(window * f.FrameRate));
            int best = -1;
            float bestV = 0.3f;
            for (int k = Math.Max(1, c - r); k <= Math.Min(f.Frames - 2, c + r); k++)
                if (f.Onset[k] > bestV && f.Onset[k] >= f.Onset[k - 1] && f.Onset[k] >= f.Onset[k + 1]) { bestV = f.Onset[k]; best = k; }
            return best < 0 ? t : f.TimeOf(best);
        }

        /// <summary>Start of the loudest ~20 s between 20% and 75% of the song, snapped to a bar line.</summary>
        private static double PreviewStart(BeatGrid grid, double songStart, double songEnd)
        {
            double len = songEnd - songStart;
            int window = Math.Max(16, grid.IndexOf(songStart + 20) - grid.IndexOf(songStart));
            int from = Math.Max(0, grid.IndexOf(songStart + len * 0.2)), to = Math.Min(grid.Count - window, grid.IndexOf(songStart + len * 0.75));
            double best = double.MinValue;
            int bestIndex = from;
            for (int i = from - from % 16; i <= to; i += 16)
            {
                if (i < 0) continue;
                double sum = 0;
                for (int k = i; k < i + window; k++) sum += grid.Loudness[k];
                if (sum > best) { best = sum; bestIndex = i; }
            }
            return to < from ? songStart + len * 0.3 : grid.TimeOf(bestIndex);
        }

        /// <summary>First and last moments the song is clearly audible (ignores leading/trailing silence).</summary>
        public static (double start, double end) AudibleRange(AudioFeatures f)
        {
            float median = BeatGrid.MedianLoudness(f);
            float threshold = median * 0.08f;
            int a = 0, b = f.Frames - 1;
            while (a < b && f.Rms[a] < threshold) a++;
            while (b > a && f.Rms[b] < threshold) b--;
            return (f.TimeOf(a), f.TimeOf(b));
        }
    }
}

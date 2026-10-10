using System;
using System.Collections.Generic;
using System.Linq;
using InvectorModKit.Charting.Audio;

namespace InvectorModKit.Charting.Generation
{
    /// <summary>
    /// Splits a song into gameplay sections the way the official charts do: List and Bounce alternate
    /// on phrase boundaries (~30 s each), and short Freefly sections (3 or 7 bars) go in quiet passages.
    /// </summary>
    public static class StructureAnalyzer
    {
        public static double SecondsPerSection = 30;
        public static double FreeflyLoudness = 0.9;
        public static double FreeflyFirstModeBounceChance = 0.55;
        private const int MinSectionBars = 4;

        public static List<Section> Analyze(AudioFeatures f, BeatGrid grid, double songStart, double songEnd, Rng rng)
        {
            int firstBar = Math.Max(0, (int)Math.Ceiling(grid.IndexOf(songStart) / 16.0));
            int lastBar = Math.Max(firstBar + 1, (int)Math.Floor(grid.IndexOf(songEnd) / 16.0));
            int bars = lastBar - firstBar;
            double barSeconds = grid.SecondsPerBeat * 4;

            var feat = BarFeatures(f, grid, firstBar, bars, out var loud);
            var novelty = Novelty(feat);

            // Pick phrase boundaries (bar offsets from firstBar): strongest novelty, preferring 4/8-bar phrases.
            int target = Math.Max(1, (int)Math.Round(bars * barSeconds / SecondsPerSection));
            const int minLen = 8;
            var order = Enumerable.Range(1, Math.Max(0, bars - 1))
                .Select(k => (k, score: novelty[k] + (k % 4 == 0 ? 0.35 : 0) + (k % 8 == 0 ? 0.2 : 0)))
                .OrderByDescending(x => x.score);
            var cuts = new List<int> { 0, bars };
            foreach (var (k, _) in order)
            {
                if (cuts.Count - 1 >= target) break;
                if (cuts.All(c => Math.Abs(c - k) >= minLen)) cuts.Add(k);
            }
            cuts.Sort();

            // Novelty peaks can cluster and leave very long stretches; split those at their best inner boundary.
            int maxLen = Math.Max(2 * minLen, (int)Math.Round(1.6 * SecondsPerSection / barSeconds));
            for (bool split = true; split;)
            {
                split = false;
                for (int c = 0; c + 1 < cuts.Count; c++)
                {
                    int a = cuts[c], b = cuts[c + 1];
                    if (b - a <= maxLen) continue;
                    int best = -1;
                    double bestScore = double.MinValue;
                    for (int k = a + minLen; k <= b - minLen; k++)
                    {
                        double s = novelty[k] + (k % 4 == 0 ? 0.35 : 0) + (k % 8 == 0 ? 0.2 : 0);
                        if (s > bestScore) { bestScore = s; best = k; }
                    }
                    if (best < 0) continue;
                    cuts.Insert(c + 1, best);
                    split = true;
                    break;
                }
            }

            // Freefly: the quietest segments below the loudness threshold (at most ~1 per 90 s).
            var segs = new List<(int start, int len, double loud)>();
            for (int i = 0; i + 1 < cuts.Count; i++)
            {
                int s = cuts[i], e = cuts[i + 1];
                double l = 0;
                for (int b = s; b < e; b++) l += loud[b];
                segs.Add((s, e - s, l / Math.Max(1, e - s)));
            }
            int maxFreefly = Math.Max(1, (int)Math.Round(bars * barSeconds / 70));
            var freefly = new HashSet<int>(segs
                .Select((s, i) => (s, i))
                .Where(x => x.i > 0 && x.s.loud < FreeflyLoudness && x.s.len >= 3)
                .OrderBy(x => x.s.loud)
                .Take(maxFreefly)
                .Select(x => x.i));

            var sections = new List<Section>();
            var mode = rng.Chance(FreeflyFirstModeBounceChance) ? SectionMode.Bounce : SectionMode.List;
            for (int i = 0; i < segs.Count; i++)
            {
                var (start, len, _) = segs[i];
                double t = i == 0 ? 0.0 : grid.TimeOf((firstBar + start) * 16);
                if (freefly.Contains(i))
                {
                    // Official Freefly sections last 3 or 7 bars; the rest of the phrase goes back to the track,
                    // joined to the next phrase when it would be too short to be a section of its own.
                    int ffLen = len >= 10 ? 7 : len >= 7 ? Math.Min(len, 7) : Math.Min(len, 3);
                    sections.Add(new Section(t, SectionMode.Freefly));
                    int rest = len - ffLen;
                    if (rest >= MinSectionBars)
                    {
                        sections.Add(new Section(grid.TimeOf((firstBar + start + ffLen) * 16), mode));
                        mode = Other(mode);
                    }
                    else if (rest > 0 && i + 1 < segs.Count)
                        segs[i + 1] = (start + ffLen, segs[i + 1].len + rest, segs[i + 1].loud);
                    continue;
                }
                sections.Add(new Section(t, mode));
                mode = Other(mode);
            }
            return sections;
        }

        private static SectionMode Other(SectionMode m) => m == SectionMode.Bounce ? SectionMode.List : SectionMode.Bounce;

        /// <summary>Per-bar mean band energies (z-scored per band) and per-bar loudness relative to the song median.</summary>
        private static double[][] BarFeatures(AudioFeatures f, BeatGrid grid, int firstBar, int bars, out double[] loud)
        {
            int dims = f.BandCount;
            var feat = new double[bars][];
            loud = new double[bars];
            float median = BeatGrid.MedianLoudness(f);
            for (int b = 0; b < bars; b++)
            {
                int a = Math.Max(0, f.FrameOf(grid.TimeOf((firstBar + b) * 16)));
                int e = Math.Min(f.Frames, f.FrameOf(grid.TimeOf((firstBar + b + 1) * 16)));
                var v = new double[dims];
                double rms = 0;
                int n = 0;
                for (int t = a; t < e; t++, n++)
                {
                    var bands = f.Bands[t];
                    for (int d = 0; d < dims; d++) v[d] += bands[d];
                    rms += f.Rms[t];
                }
                if (n > 0) for (int d = 0; d < dims; d++) v[d] /= n;
                feat[b] = v;
                loud[b] = n > 0 ? rms / n / median : 0;
            }
            for (int d = 0; d < dims; d++)
            {
                double mean = 0, sq = 0;
                for (int b = 0; b < bars; b++) mean += feat[b][d];
                mean /= Math.Max(1, bars);
                for (int b = 0; b < bars; b++) sq += (feat[b][d] - mean) * (feat[b][d] - mean);
                double sd = Math.Sqrt(sq / Math.Max(1, bars)) + 1e-6;
                for (int b = 0; b < bars; b++) feat[b][d] = (feat[b][d] - mean) / sd;
            }
            return feat;
        }

        /// <summary>Contrast between the 4 (and 8) bars before and after each bar line, z-scored.</summary>
        private static double[] Novelty(double[][] feat)
        {
            int bars = feat.Length;
            var nov = new double[bars + 1];
            foreach (int w in new[] { 4, 8 })
            {
                for (int k = 1; k < bars; k++)
                {
                    int a = Math.Max(0, k - w), c = Math.Min(bars, k + w);
                    if (k - a < 2 || c - k < 2) continue;
                    double dist = 0;
                    int dims = feat[0].Length;
                    for (int d = 0; d < dims; d++)
                    {
                        double before = 0, after = 0;
                        for (int b = a; b < k; b++) before += feat[b][d];
                        for (int b = k; b < c; b++) after += feat[b][d];
                        double diff = before / (k - a) - after / (c - k);
                        dist += diff * diff;
                    }
                    nov[k] += Math.Sqrt(dist / dims);
                }
            }
            double m = nov.Average(), s = Math.Sqrt(nov.Select(x => (x - m) * (x - m)).Average()) + 1e-9;
            for (int k = 0; k < nov.Length; k++) nov[k] = (nov[k] - m) / s;
            return nov;
        }
    }
}

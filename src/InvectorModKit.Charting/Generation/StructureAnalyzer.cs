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

            var feat = SpectralBars(f, grid, firstBar, bars);
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

            // Freefly where the official charts put it: a per-bar model learned on them (quieter than the
            // rest of the song, few drums, mostly around the bridge, ~2/3 in). Windows of 3 or 7 bars.
            var bar = InvectorModKit.Charting.Generation.BarFeatures.Compute(f, grid, firstBar, bars);
            var logit = bar.Select(x => x.Zip(FreeflyWeights, (a, b) => a * b).Sum()).ToArray();
            var windows = ChooseFreeflyWindows(logit, barSeconds);
            bool InFreefly(int b) => windows.Any(w => b >= w.start && b < w.start + w.len);

            // Phrase segments alternate List/Bounce; Freefly windows are laid over them.
            var bounds = new SortedSet<int>(cuts);
            foreach (var w in windows) { bounds.Add(w.start); bounds.Add(w.start + w.len); }
            var pieces = bounds.ToList();
            var parts = new List<(int start, SectionMode mode)>();
            var mode = rng.Chance(FreeflyFirstModeBounceChance) ? SectionMode.Bounce : SectionMode.List;
            int lastSegment = -1;
            for (int p = 0; p + 1 < pieces.Count; p++)
            {
                int s = pieces[p], len = pieces[p + 1] - s;
                if (InFreefly(s)) { parts.Add((s, SectionMode.Freefly)); continue; }
                int segment = cuts.FindLastIndex(c => c <= s);
                if (segment != lastSegment) { if (lastSegment >= 0) mode = Other(mode); lastSegment = segment; }
                // A leftover too short to be a section joins the following track section (or the previous one).
                bool nextIsTrack = p + 2 < pieces.Count && !InFreefly(pieces[p + 1]);
                if (len < MinSectionBars && parts.Count > 0 && !nextIsTrack) continue;
                parts.Add((s, len < MinSectionBars && nextIsTrack ? Other(mode) : mode));
            }

            var sections = new List<Section>();
            foreach (var (start, m) in parts)
            {
                if (sections.Count > 0 && sections[sections.Count - 1].Mode == m) continue;
                sections.Add(new Section(sections.Count == 0 ? 0.0 : grid.TimeOf((firstBar + start) * 16), m));
            }
            return sections;
        }

        /// <summary>Learned on the official charts with ChartLab "freefly" (BarFeatures layout).</summary>
        private static readonly double[] FreeflyWeights = { -6.33, -1.42, -1.09, 0.42, 0.27, -0.30, 0.36, 0.08, 0.48, 13.44, -10.20, -0.02 };

        /// <summary>
        /// Official songs have ~1 Freefly per 145 s, 3 or 7 bars long (7 favoured), never in the first
        /// 8 bars or the last 2. Picks the best-scoring non-overlapping windows, 8+ bars apart.
        /// </summary>
        public static List<(int start, int len)> ChooseFreeflyWindows(double[] logit, double barSeconds)
        {
            int bars = logit.Length;
            int count = Math.Max(1, (int)Math.Round(bars * barSeconds / 145));
            var options = new List<(int s, int len, double score)>();
            foreach (int len in new[] { 7, 3 })
                for (int s = 8; s + len + 2 <= bars; s++)
                {
                    double m = 0;
                    for (int k = s; k < s + len; k++) m += logit[k];
                    options.Add((s, len, m / len + (len == 7 ? 0.3 : 0)));
                }
            var chosen = new List<(int start, int len)>();
            foreach (var o in options.OrderByDescending(o => o.score))
            {
                if (chosen.Count >= count) break;
                if (chosen.All(c => o.s >= c.start + c.len + 8 || c.start >= o.s + o.len + 8)) chosen.Add((o.s, o.len));
            }
            chosen.Sort((a, b) => a.start.CompareTo(b.start));
            return chosen;
        }

        private static SectionMode Other(SectionMode m) => m == SectionMode.Bounce ? SectionMode.List : SectionMode.Bounce;

        /// <summary>Per-bar mean band energies, z-scored per band (for phrase boundaries).</summary>
        private static double[][] SpectralBars(AudioFeatures f, BeatGrid grid, int firstBar, int bars)
        {
            int dims = f.BandCount;
            var feat = new double[bars][];
            for (int b = 0; b < bars; b++)
            {
                int a = Math.Max(0, f.FrameOf(grid.TimeOf((firstBar + b) * 16)));
                int e = Math.Min(f.Frames, f.FrameOf(grid.TimeOf((firstBar + b + 1) * 16)));
                var v = new double[dims];
                int n = 0;
                for (int t = a; t < e; t++, n++)
                {
                    var bands = f.Bands[t];
                    for (int d = 0; d < dims; d++) v[d] += bands[d];
                }
                if (n > 0) for (int d = 0; d < dims; d++) v[d] /= n;
                feat[b] = v;
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

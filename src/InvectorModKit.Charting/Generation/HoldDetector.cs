using System;
using System.Collections.Generic;
using InvectorModKit.Charting.Audio;

namespace InvectorModKit.Charting.Generation
{
    /// <summary>A held strum: starts at grid index <see cref="Start"/> and lasts <see cref="Quarters"/> quarter beats.</summary>
    public struct HoldRegion
    {
        public int Start;
        public int Quarters;
        public double Score;
        public int End => Start + Quarters;
    }

    /// <summary>
    /// Finds sustained sounds (a held vocal or synth note) that deserve a hold: a clear attack, then
    /// the mid-range spectrum stays the same shape and loud while few new mid-range attacks occur.
    /// The official charts hold ~3-4% of their notes, the same holds in every difficulty.
    /// </summary>
    public static class HoldDetector
    {
        public static double HoldsPerNote = 0.03;
        public static double MinScore = 0.55;
        private const int MinQuarters = 3, MaxQuarters = 14;

        public static List<HoldRegion> Find(AudioFeatures f, BeatGrid grid, Func<int, bool> allowed, double expectedUltraNotes)
        {
            int n = grid.Count;
            // Per-quarter mid-range spectrum (z-scored per band over the song) and mid-range attack activity.
            int lo = BandIndex(f, 160), hi = BandIndex(f, 2500);
            int dims = hi - lo + 1;
            var v = new double[n][];
            var midAttack = new double[n];
            var mean = new double[dims];
            var sq = new double[dims];
            for (int q = 0; q < n; q++)
            {
                v[q] = new double[dims];
                int a = Math.Max(0, f.FrameOf(grid.TimeOf(q))), b = Math.Min(f.Frames - 1, f.FrameOf(grid.TimeOf(q + 1)) - 1);
                int count = 0;
                for (int t = a; t <= b; t++, count++)
                {
                    for (int d = 0; d < dims; d++) v[q][d] += f.Bands[t][lo + d];
                    midAttack[q] = Math.Max(midAttack[q], f.OnsetMid[t]);
                }
                if (count > 0) for (int d = 0; d < dims; d++) v[q][d] /= count;
                for (int d = 0; d < dims; d++) { mean[d] += v[q][d]; sq[d] += v[q][d] * v[q][d]; }
            }
            for (int d = 0; d < dims; d++)
            {
                mean[d] /= Math.Max(1, n);
                double sd = Math.Sqrt(Math.Max(1e-9, sq[d] / Math.Max(1, n) - mean[d] * mean[d]));
                for (int q = 0; q < n; q++) v[q][d] = (v[q][d] - mean[d]) / sd;
            }

            var candidates = new List<HoldRegion>();
            for (int i = 0; i + MinQuarters < n; i++)
            {
                if (BeatGrid.Level(i) == 3 || !allowed(i)) continue;
                if (grid.X[i][BeatGrid.FOnset] < 0.35f || grid.X[i][BeatGrid.FProminence] < 0.6f) continue;
                var reference = v[Math.Min(n - 1, i + 1)];
                double loudRef = Math.Max(0.05, grid.Loudness[i]);
                double simSum = 0, attackSum = 0, best = 0;
                int bestLen = 0;
                for (int k = 1; k < MaxQuarters && i + k < n; k++)
                {
                    int q = i + k;
                    if (!allowed(q) || grid.Loudness[q] < 0.6 * loudRef) break;
                    simSum += Cosine(reference, v[q]);
                    attackSum += midAttack[q];
                    int len = k + 1;
                    if (len < MinQuarters) continue;
                    double score = simSum / k - 0.6 * attackSum / k;
                    if (score < MinScore) break;
                    if (score * Math.Log(1 + len) > best) { best = score * Math.Log(1 + len); bestLen = len; }
                }
                if (bestLen >= MinQuarters) candidates.Add(new HoldRegion { Start = i, Quarters = bestLen, Score = best });
            }

            // Best first, never overlapping, at least two beats apart.
            candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
            int target = (int)Math.Round(expectedUltraNotes * HoldsPerNote);
            var chosen = new List<HoldRegion>();
            foreach (var c in candidates)
            {
                if (chosen.Count >= target) break;
                bool clear = true;
                foreach (var h in chosen)
                    if (c.Start < h.End + 8 && h.Start < c.End + 8) { clear = false; break; }
                if (clear) chosen.Add(c);
            }
            chosen.Sort((a, b) => a.Start.CompareTo(b.Start));
            return chosen;
        }

        private static int BandIndex(AudioFeatures f, double hz)
        {
            double logMin = Math.Log(30), logMax = Math.Log(16000);
            int b = (int)((Math.Log(hz) - logMin) / (logMax - logMin) * f.BandCount);
            return Math.Max(0, Math.Min(f.BandCount - 1, b));
        }

        private static double Cosine(double[] a, double[] b)
        {
            double ab = 0, aa = 0, bb = 0;
            for (int i = 0; i < a.Length; i++) { ab += a[i] * b[i]; aa += a[i] * a[i]; bb += b[i] * b[i]; }
            return aa <= 0 || bb <= 0 ? 0 : ab / Math.Sqrt(aa * bb);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Places holds the way the official charts do: a model learned on their holds (ChartLab "holds")
    /// favours strong beats followed by sustained, harmonic sound with no new sung attacks and no
    /// drums — the floating moments. A hold lasts until the next sung attack (official median ratio
    /// 0.94), 0.75 to 4 beats. Official charts hold ~2% of their notes, the same in every difficulty.
    /// </summary>
    public static class HoldDetector
    {
        public static double HoldsPerNote = 0.02;
        private const int MinQuarters = 3, MaxQuarters = 16;

        /// <summary>HoldFeatures layout; learned on the official charts.</summary>
        private static readonly double[] Weights = { -5.097, -0.673, 0.238, 0.731, 0.903, -3.747, -2.014, 1.741, -0.2, -1.808, -1.179, 0.854 };

        public static List<HoldRegion> Find(AudioFeatures f, BeatGrid grid, Func<int, bool> allowed, double expectedUltraNotes)
        {
            var candidates = new List<HoldRegion>();
            for (int i = 0; i + MinQuarters < grid.Count; i++)
            {
                if (BeatGrid.Level(i) == 3 || !allowed(i)) continue;
                var x = HoldFeatures.At(f, grid, i);
                double z = 0;
                for (int k = 0; k < x.Length; k++) z += Weights[k] * x[k];
                int quarters = Length(f, grid, i, allowed);
                if (quarters >= MinQuarters) candidates.Add(new HoldRegion { Start = i, Quarters = quarters, Score = z });
            }

            // Best first, never overlapping, at least two beats apart.
            int target = (int)Math.Round(expectedUltraNotes * HoldsPerNote);
            var chosen = new List<HoldRegion>();
            foreach (var c in candidates.OrderByDescending(c => c.Score))
            {
                if (chosen.Count >= target) break;
                if (chosen.All(h => c.Start >= h.End + 8 || h.Start >= c.End + 8)) chosen.Add(c);
            }
            chosen.Sort((a, b) => a.Start.CompareTo(b.Start));
            return chosen;
        }

        /// <summary>Until the next strong sung attack, on the quarter grid, 0.75 to 4 beats, staying in allowed positions.</summary>
        private static int Length(AudioFeatures f, BeatGrid grid, int i, Func<int, bool> allowed)
        {
            double t = grid.TimeOf(i);
            double next = -1;
            for (int k = Math.Max(1, f.FrameOf(t + 0.15)); k < Math.Min(f.Frames - 1, f.FrameOf(t + grid.SecondsPerBeat * 4.5)); k++)
                if (f.OnsetHarmonic[k] > 0.5f && f.OnsetHarmonic[k] >= f.OnsetHarmonic[k - 1] && f.OnsetHarmonic[k] >= f.OnsetHarmonic[k + 1]) { next = f.TimeOf(k); break; }
            if (next < 0) next = t + grid.SecondsPerBeat * 4;
            int quarters = (int)Math.Floor(0.94 * (next - t) / grid.Quarter);
            quarters = Math.Min(MaxQuarters, quarters);
            for (int k = 1; k <= quarters; k++)
                if (i + k >= grid.Count || !allowed(i + k)) { quarters = k - 1; break; }
            return quarters;
        }
    }
}

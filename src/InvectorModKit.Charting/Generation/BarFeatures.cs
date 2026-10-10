using System;
using InvectorModKit.Charting.Audio;

namespace InvectorModKit.Charting.Generation
{
    /// <summary>
    /// Per-bar descriptors used to decide where the game's sections go (Freefly in particular), each
    /// z-scored over the song so the model sees "quieter than the rest of this song", not absolute levels.
    /// </summary>
    public static class BarFeatures
    {
        public const int Count = 12;
        // bias, loudness, percussive, harmonic onsets, harmonic ratio, kick, hats,
        // loudness vs previous 4 bars, loudness vs next 4 bars, position, position², sung activity
        public static double[][] Compute(AudioFeatures f, BeatGrid grid, int firstBar, int bars)
        {
            var raw = new double[bars][];
            for (int b = 0; b < bars; b++)
            {
                double a = grid.TimeOf((firstBar + b) * 16), e = grid.TimeOf((firstBar + b + 1) * 16);
                raw[b] = new[]
                {
                    Mean(f.Rms, f, a, e),
                    Mean(f.OnsetPercussive, f, a, e),
                    Mean(f.OnsetHarmonic, f, a, e),
                    Mean(f.HarmonicRatio, f, a, e),
                    Mean(f.OnsetLow, f, a, e),
                    Mean(f.OnsetHigh, f, a, e),
                };
            }
            int dims = raw.Length == 0 ? 0 : raw[0].Length;
            var z = ZScore(raw, dims);
            var x = new double[bars][];
            for (int b = 0; b < bars; b++)
            {
                double prev = 0, next = 0;
                int np = 0, nn = 0;
                for (int k = Math.Max(0, b - 4); k < b; k++) { prev += z[k][0]; np++; }
                for (int k = b + 1; k <= Math.Min(bars - 1, b + 4); k++) { next += z[k][0]; nn++; }
                double pos = bars <= 1 ? 0 : b / (double)(bars - 1);
                x[b] = new[]
                {
                    1.0, z[b][0], z[b][1], z[b][2], z[b][3], z[b][4], z[b][5],
                    np > 0 ? z[b][0] - prev / np : 0,
                    nn > 0 ? z[b][0] - next / nn : 0,
                    pos, pos * pos,
                    z[b][2] * z[b][3],
                };
            }
            return x;
        }

        private static double Mean(float[] series, AudioFeatures f, double from, double to)
        {
            int a = Math.Max(0, f.FrameOf(from)), b = Math.Min(series.Length - 1, f.FrameOf(to));
            if (b < a) return 0;
            double s = 0;
            for (int i = a; i <= b; i++) s += series[i];
            return s / (b - a + 1);
        }

        private static double[][] ZScore(double[][] raw, int dims)
        {
            int n = raw.Length;
            var z = new double[n][];
            for (int b = 0; b < n; b++) z[b] = new double[dims];
            for (int d = 0; d < dims; d++)
            {
                double m = 0, s = 0;
                for (int b = 0; b < n; b++) m += raw[b][d];
                m /= Math.Max(1, n);
                for (int b = 0; b < n; b++) s += (raw[b][d] - m) * (raw[b][d] - m);
                s = Math.Sqrt(s / Math.Max(1, n)) + 1e-9;
                for (int b = 0; b < n; b++) z[b][d] = (raw[b][d] - m) / s;
            }
            return z;
        }
    }
}

using System;

namespace InvectorModKit.Charting.Audio
{
    /// <summary>
    /// Turns a separated vocal track into the "sung attack" and "singing activity" features of a mix's
    /// analysis. Used identically when training on the official songs and in game, so the note model
    /// sees the same kind of values in both places.
    /// </summary>
    public static class VocalFeatures
    {
        public static void Inject(AudioFeatures mix, float[] vocalsMono, int sampleRate)
        {
            var stem = AudioAnalyzer.AnalyzeOnsets(vocalsMono, sampleRate);
            var sorted = (float[])stem.Rms.Clone();
            Array.Sort(sorted);
            float loud = Math.Max(1e-6f, sorted[(int)(0.95 * (sorted.Length - 1))]);
            for (int i = 0; i < mix.Frames; i++)
            {
                int j = stem.FrameOf(mix.TimeOf(i));
                bool inside = j >= 0 && j < stem.Frames;
                mix.OnsetVocal[i] = inside ? stem.Onset[j] : 0f;
                mix.VocalRatio[i] = inside ? Math.Min(1f, stem.Rms[j] / loud) : 0f;
            }
            mix.VocalPeakTimes = Peaks(stem);
            mix.HasSeparatedVocals = true;
        }

        /// <summary>Local maxima above mean + 1.5 standard deviations over a ~1 s window: clear sung attacks.</summary>
        private static double[] Peaks(AudioFeatures stem)
        {
            var x = stem.Onset;
            int n = x.Length, w = 43;
            var peaks = new System.Collections.Generic.List<double>();
            double sum = 0, sq = 0;
            int lo = 0, hi = -1;
            for (int i = 0; i < n; i++)
            {
                while (hi < Math.Min(n - 1, i + w)) { hi++; sum += x[hi]; sq += x[hi] * x[hi]; }
                while (lo < i - w) { sum -= x[lo]; sq -= x[lo] * x[lo]; lo++; }
                int count = hi - lo + 1;
                double mean = sum / count, sd = Math.Sqrt(Math.Max(0, sq / count - mean * mean));
                if (x[i] <= mean + 1.5 * sd) continue;
                bool max = true;
                for (int k = Math.Max(0, i - 2); k <= Math.Min(n - 1, i + 2) && max; k++) if (x[k] > x[i]) max = false;
                if (max) peaks.Add(stem.TimeOf(i));
            }
            return peaks.ToArray();
        }
    }
}

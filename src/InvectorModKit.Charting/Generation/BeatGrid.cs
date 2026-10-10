using System;
using InvectorModKit.Charting.Audio;

namespace InvectorModKit.Charting.Generation
{
    /// <summary>
    /// Quarter-beat grid over a song with the audio features the note model reads at each position.
    /// Index 0 is a downbeat, so <c>i % 16 == 0</c> is a bar line and <c>i % 4 == 0</c> a beat.
    /// </summary>
    public sealed class BeatGrid
    {
        public const int FeatureCount = 20;
        // Feature layout (see Build): bias, onset, low, mid, high, loudness, prominence, silence,
        // onset at -1q, onset at +1q, onset ±1 bar, onset², onset ±2 bars,
        // harmonic (sung/melodic) onset, percussive onset, harmonic onset ±1 bar, sung activity ±1 beat, harmonic ratio.
        public const int FOnset = 1, FLow = 2, FMid = 3, FHigh = 4, FLoud = 5, FProminence = 6, FSilent = 7;
        public const int FHarmonic = 13, FPercussive = 14, FHarmonicBar = 15, FSungActivity = 16, FHarmonicRatio = 17;
        // Estimated sung attack (stereo centre) and singing activity around the position.
        public const int FVocal = 18, FVocalActivity = 19;

        public double Origin;
        public double SecondsPerBeat;
        public int Count;
        public float[][] X;
        /// <summary>Song loudness (RMS) per position relative to the song median.</summary>
        public float[] Loudness;

        public double Quarter => SecondsPerBeat / 4;
        public double TimeOf(int i) => Origin + i * Quarter;
        public int IndexOf(double time) => (int)Math.Round((time - Origin) / Quarter);

        /// <summary>0 = bar line, 1 = beat, 2 = half beat, 3 = quarter beat.</summary>
        public static int Level(int i) => (i & 15) == 0 ? 0 : (i & 3) == 0 ? 1 : (i & 1) == 0 ? 2 : 3;

        public static BeatGrid Build(AudioFeatures f, double bpm, double firstBeat, double from, double to)
        {
            double spb = 60 / bpm, bar = 4 * spb;
            double origin = firstBeat + Math.Floor((from - firstBeat) / bar) * bar;
            var g = new BeatGrid { Origin = origin, SecondsPerBeat = spb };
            g.Count = Math.Max(0, (int)Math.Floor((to - origin) / g.Quarter) + 1);
            g.X = new float[g.Count][];
            g.Loudness = new float[g.Count];

            float median = MedianLoudness(f);
            var onset = new float[g.Count];
            var harmonic = new float[g.Count];
            for (int i = 0; i < g.Count; i++)
            {
                double t = g.TimeOf(i);
                onset[i] = f.MaxAround(f.Onset, t, 2);
                harmonic[i] = f.MaxAround(f.OnsetHarmonic, t, 2);
                g.Loudness[i] = MeanRms(f, t - spb / 2, t + spb / 2) / median;
            }

            for (int i = 0; i < g.Count; i++)
            {
                double t = g.TimeOf(i);
                // Prominence: onset relative to the strongest one within ±2 beats.
                float local = 1e-6f;
                for (int k = Math.Max(0, i - 8); k <= Math.Min(g.Count - 1, i + 8); k++) local = Math.Max(local, onset[k]);
                float loud = Math.Min(3f, g.Loudness[i]);
                float At(int k) => k >= 0 && k < g.Count ? onset[k] : 0f;
                g.X[i] = new float[FeatureCount]
                {
                    1f,
                    onset[i],
                    f.MaxAround(f.OnsetLow, t, 2),
                    f.MaxAround(f.OnsetMid, t, 2),
                    f.MaxAround(f.OnsetHigh, t, 2),
                    loud,
                    onset[i] / local,
                    loud < 0.15f ? 1f : 0f,
                    // Context: neighbours decide between adjacent positions; the same position one bar
                    // away captures the repeated patterns charters follow.
                    At(i - 1),
                    At(i + 1),
                    0.5f * (At(i - 16) + At(i + 16)),
                    onset[i] * onset[i],
                    0.5f * (At(i - 32) + At(i + 32)),
                    harmonic[i],
                    f.MaxAround(f.OnsetPercussive, t, 2),
                    0.5f * ((i >= 16 ? harmonic[i - 16] : 0f) + (i + 16 < g.Count ? harmonic[i + 16] : 0f)),
                    MeanOf(f.OnsetHarmonic, f, t - spb, t + spb),
                    MeanOf(f.HarmonicRatio, f, t - spb / 2, t + spb / 2),
                    f.MaxAround(f.OnsetVocal, t, 2),
                    MeanOf(f.VocalRatio, f, t - spb / 2, t + spb / 2),
                };
            }
            return g;
        }

        private static float MeanOf(float[] series, AudioFeatures f, double from, double to)
        {
            int a = Math.Max(0, f.FrameOf(from)), b = Math.Min(series.Length - 1, f.FrameOf(to));
            if (b < a) return 0f;
            double s = 0;
            for (int i = a; i <= b; i++) s += series[i];
            return (float)(s / (b - a + 1));
        }

        private static float MeanRms(AudioFeatures f, double from, double to)
        {
            int a = Math.Max(0, f.FrameOf(from)), b = Math.Min(f.Frames - 1, f.FrameOf(to));
            if (b < a) return 0f;
            double s = 0;
            for (int i = a; i <= b; i++) s += f.Rms[i];
            return (float)(s / (b - a + 1));
        }

        internal static float MedianLoudness(AudioFeatures f)
        {
            var copy = new float[f.Rms.Length];
            int n = 0;
            foreach (var r in f.Rms) if (r > 1e-4f) copy[n++] = r;
            if (n == 0) return 1e-4f;
            Array.Sort(copy, 0, n);
            return Math.Max(1e-4f, copy[n / 2]);
        }
    }
}

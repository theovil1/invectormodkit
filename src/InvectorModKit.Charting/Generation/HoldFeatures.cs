using System;
using InvectorModKit.Charting.Audio;

namespace InvectorModKit.Charting.Generation
{
    /// <summary>
    /// Descriptors of "should a hold start here?" for a grid position: the attack itself, then how
    /// sustained the next one and two beats are (few new sung attacks, harmonic sound, steady level).
    /// </summary>
    public static class HoldFeatures
    {
        public const int Count = 12;
        // bias, onset, harmonic onset, beat, bar, harmonic onsets next beat, next 2 beats,
        // harmonic ratio next 2 beats, loudness kept next 2 beats, percussive next 2 beats, sung activity, prominence
        public static double[] At(AudioFeatures f, BeatGrid g, int i)
        {
            double t = g.TimeOf(i), spb = g.SecondsPerBeat;
            var x = g.X[i];
            int level = BeatGrid.Level(i);
            double loudHere = Math.Max(0.05, g.Loudness[i]);
            double loudNext = Mean(f.Rms, f, t + spb * 0.25, t + 2 * spb) / Math.Max(1e-4, BeatGrid.MedianLoudness(f));
            return new[]
            {
                1.0,
                x[BeatGrid.FOnset],
                x[BeatGrid.FHarmonic],
                level <= 1 ? 1.0 : 0.0,
                level == 0 ? 1.0 : 0.0,
                Mean(f.OnsetHarmonic, f, t + 0.08, t + spb),
                Mean(f.OnsetHarmonic, f, t + 0.08, t + 2 * spb),
                Mean(f.HarmonicRatio, f, t, t + 2 * spb),
                Math.Min(2.0, loudNext / loudHere),
                Mean(f.OnsetPercussive, f, t + 0.08, t + 2 * spb),
                x[BeatGrid.FSungActivity],
                x[BeatGrid.FProminence],
            };
        }

        public static double Mean(float[] series, AudioFeatures f, double from, double to)
        {
            int a = Math.Max(0, f.FrameOf(from)), b = Math.Min(series.Length - 1, f.FrameOf(to));
            if (b < a) return 0;
            double s = 0;
            for (int k = a; k <= b; k++) s += series[k];
            return s / (b - a + 1);
        }
    }
}

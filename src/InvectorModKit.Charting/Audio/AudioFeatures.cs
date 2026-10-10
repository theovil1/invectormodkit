using System;

namespace InvectorModKit.Charting.Audio
{
    /// <summary>Frame-level descriptors of a song, computed once and shared by tempo, structure and note generation.</summary>
    public sealed class AudioFeatures
    {
        public int SampleRate;
        public int Hop;
        public int Frames;
        public double Duration;

        /// <summary>Onset strength (spectral flux) over the whole spectrum and in three bands.</summary>
        public float[] Onset, OnsetLow, OnsetMid, OnsetHigh;
        /// <summary>Onsets of the harmonic part in the singing range (syllables, melody notes) and of the percussive part.</summary>
        public float[] OnsetHarmonic, OnsetPercussive;
        /// <summary>0..1, how much of the singing-range energy is sustained (harmonic) rather than percussive.</summary>
        public float[] HarmonicRatio;
        /// <summary>Linear RMS loudness per frame.</summary>
        public float[] Rms;
        /// <summary>Log-energy in <see cref="BandCount"/> log-spaced bands per frame (for structure and timbre).</summary>
        public float[][] Bands;
        /// <summary>Spectral centroid in Hz per frame.</summary>
        public float[] Centroid;

        /// <summary>
        /// Seconds between a frame's start and the moment an onset detected in it is heard.
        /// Calibrated on the official charts' beat grids (see ChartLab "tempo").
        /// </summary>
        public double TimeOffset;

        public int BandCount => Bands.Length == 0 ? 0 : Bands[0].Length;
        public double FrameRate => SampleRate / (double)Hop;
        public double TimeOf(double frame) => frame * Hop / SampleRate + TimeOffset;
        public int FrameOf(double time) => (int)Math.Round((time - TimeOffset) * SampleRate / Hop);

        /// <summary>Linear interpolation of a frame series at a fractional frame index (0 outside).</summary>
        public static float Sample(float[] series, double frame)
        {
            if (frame < 0 || frame >= series.Length - 1) return 0f;
            int i = (int)frame;
            double f = frame - i;
            return (float)(series[i] * (1 - f) + series[i + 1] * f);
        }

        /// <summary>Maximum of a frame series within ±radius frames around a time.</summary>
        public float MaxAround(float[] series, double time, int radius)
        {
            int c = FrameOf(time);
            float m = 0f;
            for (int i = Math.Max(0, c - radius); i <= Math.Min(series.Length - 1, c + radius); i++)
                if (series[i] > m) m = series[i];
            return m;
        }
    }
}

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
        /// <summary>Estimated sung attacks (harmonic onsets of the stereo centre in the singing range).</summary>
        public float[] OnsetVocal;
        /// <summary>0..1, share of the singing range that is centred and sustained (a proxy for "someone is singing").</summary>
        public float[] VocalRatio;
        /// <summary>True when OnsetVocal/VocalRatio come from a separated vocal track rather than the stereo-centre estimate.</summary>
        public bool HasSeparatedVocals;
        /// <summary>Times (s) of clear sung attacks in the separated vocal track (adaptive-threshold peaks), sorted.</summary>
        public double[] VocalPeakTimes;
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

using System;

namespace InvectorModKit.Charting.Audio
{
    /// <summary>Short-time spectral analysis: onset strength (SuperFlux-style), loudness, band energies.</summary>
    public static class AudioAnalyzer
    {
        public const int BandCount = 48;
        private const double MinHz = 30, MaxHz = 16000;
        private const double LowSplitHz = 160, HighSplitHz = 2500;
        /// <summary>Onsets are heard this fraction of half a window after the frame start (measured: 18.8 ms at 44.1 kHz).</summary>
        public static double OnsetLatency = 0.81;

        /// <param name="mono">Mono samples in [-1, 1].</param>
        public static AudioFeatures Analyze(float[] mono, int sampleRate)
        {
            // ~11.6 ms hop (512 samples at 44.1 kHz), ~46 ms window.
            int hop = (int)Math.Round(sampleRate * 512.0 / 44100.0);
            int n = 2048;
            while (n < hop * 4) n <<= 1;
            var fft = new Fft(n);
            int frames = Math.Max(1, (mono.Length - n) / hop + 1);

            var window = new double[n];
            for (int i = 0; i < n; i++) window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / n);

            // Map FFT bins to log-spaced bands.
            int bins = n / 2;
            var binBand = new int[bins];
            double logMin = Math.Log(MinHz), logMax = Math.Log(MaxHz);
            for (int k = 0; k < bins; k++)
            {
                double hz = k * sampleRate / (double)n;
                if (hz < MinHz || hz > MaxHz) { binBand[k] = -1; continue; }
                int b = (int)((Math.Log(hz) - logMin) / (logMax - logMin) * BandCount);
                binBand[k] = Math.Min(BandCount - 1, Math.Max(0, b));
            }
            var bandHz = new double[BandCount];
            for (int b = 0; b < BandCount; b++) bandHz[b] = Math.Exp(logMin + (b + 0.5) / BandCount * (logMax - logMin));

            var f = new AudioFeatures
            {
                SampleRate = sampleRate,
                Hop = hop,
                Frames = frames,
                Duration = mono.Length / (double)sampleRate,
                TimeOffset = OnsetLatency * (n / 2.0) / sampleRate,
                Onset = new float[frames], OnsetLow = new float[frames], OnsetMid = new float[frames], OnsetHigh = new float[frames],
                Rms = new float[frames],
                Centroid = new float[frames],
                Bands = new float[frames][],
            };

            var re = new double[n];
            var im = new double[n];
            var bandEnergy = new double[BandCount];
            const int lag = 2; // compare with 2 frames back, max-filtered across neighbouring bands (SuperFlux)
            for (int t = 0; t < frames; t++)
            {
                int off = t * hop;
                double sumSq = 0;
                for (int i = 0; i < n; i++)
                {
                    double s = mono[off + i];
                    if (i >= (n - hop) / 2 && i < (n + hop) / 2) sumSq += s * s;
                    re[i] = s * window[i];
                    im[i] = 0;
                }
                f.Rms[t] = (float)Math.Sqrt(sumSq / hop);
                fft.Transform(re, im);

                Array.Clear(bandEnergy, 0, BandCount);
                double magSum = 0, weighted = 0;
                for (int k = 1; k < bins; k++)
                {
                    int b = binBand[k];
                    if (b < 0) continue;
                    double p = re[k] * re[k] + im[k] * im[k];
                    bandEnergy[b] += p;
                    double mag = Math.Sqrt(p);
                    magSum += mag;
                    weighted += mag * k * sampleRate / (double)n;
                }
                f.Centroid[t] = (float)(magSum > 1e-9 ? weighted / magSum : 0);
                var bands = new float[BandCount];
                for (int b = 0; b < BandCount; b++) bands[b] = (float)Math.Log(1 + 1000 * bandEnergy[b]);
                f.Bands[t] = bands;

                if (t >= lag)
                {
                    var prev = f.Bands[t - lag];
                    double all = 0, lo = 0, mid = 0, hi = 0;
                    for (int b = 0; b < BandCount; b++)
                    {
                        float ref0 = prev[b];
                        if (b > 0 && prev[b - 1] > ref0) ref0 = prev[b - 1];
                        if (b < BandCount - 1 && prev[b + 1] > ref0) ref0 = prev[b + 1];
                        double d = bands[b] - ref0;
                        if (d <= 0) continue;
                        all += d;
                        if (bandHz[b] < LowSplitHz) lo += d;
                        else if (bandHz[b] < HighSplitHz) mid += d;
                        else hi += d;
                    }
                    f.Onset[t] = (float)all; f.OnsetLow[t] = (float)lo; f.OnsetMid[t] = (float)mid; f.OnsetHigh[t] = (float)hi;
                }
            }

            Normalize(f.Onset); Normalize(f.OnsetLow); Normalize(f.OnsetMid); Normalize(f.OnsetHigh);
            return f;
        }

        /// <summary>Removes the slow-moving baseline and scales so that the 99th percentile is 1.</summary>
        private static void Normalize(float[] x)
        {
            int n = x.Length;
            if (n == 0) return;
            const int radius = 22; // ~0.25 s
            var baseline = new float[n];
            double acc = 0;
            int lo = 0, hi = -1;
            for (int i = 0; i < n; i++)
            {
                while (hi < Math.Min(n - 1, i + radius)) acc += x[++hi];
                while (lo < i - radius) acc -= x[lo++];
                baseline[i] = (float)(acc / (hi - lo + 1));
            }
            for (int i = 0; i < n; i++) x[i] = Math.Max(0f, x[i] - baseline[i]);
            var sorted = (float[])x.Clone();
            Array.Sort(sorted);
            float p99 = sorted[(int)(0.99 * (n - 1))];
            if (p99 <= 1e-9f) return;
            for (int i = 0; i < n; i++) x[i] = Math.Min(2f, x[i] / p99);
        }
    }
}

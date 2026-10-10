using System;
using System.Collections.Generic;

namespace InvectorModKit.Charting.Audio
{
    /// <summary>
    /// Integrated loudness (ITU-R BS.1770 / EBU R128: K-weighting, 400 ms blocks, absolute and relative
    /// gates) and level matching. The official songs are mastered very loud (median about -7.4 LUFS),
    /// so custom songs are brought to the same level and soft-limited to avoid clipping.
    /// </summary>
    public static class Loudness
    {
        /// <summary>Median integrated loudness of the official songs, measured with ffmpeg ebur128.</summary>
        public const double OfficialLufs = -7.4;
        public const double MaxGainDb = 18, MinGainDb = -12;

        public static double IntegratedLufs(DecodedAudio audio)
        {
            int ch = audio.Channels, rate = audio.SampleRate, frames = audio.Frames;
            var (b1, a1) = HighShelf(rate);
            var (b2, a2) = HighPass(rate);
            int block = (int)(0.4 * rate), step = (int)(0.1 * rate);
            int blocks = frames < block ? 0 : (frames - block) / step + 1;
            if (blocks == 0) return -70;

            // K-weighted squared signal summed over channels (L/R weights are 1), per 100 ms hop.
            int hops = frames / step + 1;
            var hopEnergy = new double[hops];
            for (int c = 0; c < ch; c++)
            {
                double x1 = 0, x2 = 0, y1 = 0, y2 = 0, u1 = 0, u2 = 0, z1 = 0, z2 = 0;
                for (int i = 0; i < frames; i++)
                {
                    double x = audio.Samples[i * ch + c];
                    double y = b1[0] * x + b1[1] * x1 + b1[2] * x2 - a1[1] * y1 - a1[2] * y2;
                    x2 = x1; x1 = x; y2 = y1; y1 = y;
                    double z = b2[0] * y + b2[1] * u1 + b2[2] * u2 - a2[1] * z1 - a2[2] * z2;
                    u2 = u1; u1 = y; z2 = z1; z1 = z;
                    hopEnergy[i / step] += z * z;
                }
            }
            var loudness = new List<double>(blocks);
            var power = new List<double>(blocks);
            for (int k = 0; k < blocks; k++)
            {
                double sum = 0;
                for (int h = k; h < k + 4 && h < hops; h++) sum += hopEnergy[h];
                double p = sum / block;
                power.Add(p);
                loudness.Add(-0.691 + 10 * Math.Log10(Math.Max(p, 1e-12)));
            }
            double Gated(double threshold)
            {
                double s = 0; int n = 0;
                for (int k = 0; k < blocks; k++) if (loudness[k] > threshold) { s += power[k]; n++; }
                return n == 0 ? -70 : -0.691 + 10 * Math.Log10(s / n);
            }
            double ungated = Gated(-70);
            return Gated(ungated - 10);
        }

        /// <summary>Gain (dB) that brings a song to the official loudness, clamped to a sane range.</summary>
        public static double MatchOfficialGainDb(DecodedAudio audio)
        {
            double lufs = IntegratedLufs(audio);
            if (lufs <= -69) return 0;
            return Math.Max(MinGainDb, Math.Min(MaxGainDb, OfficialLufs - lufs));
        }

        /// <summary>
        /// Applies a gain, then a peak limiter (5 ms look-ahead, 80 ms release) so samples stay
        /// within ±<paramref name="ceiling"/>. In place.
        /// </summary>
        public static void ApplyGainLimited(DecodedAudio audio, double gainDb, float ceiling = 0.98f)
        {
            if (Math.Abs(gainDb) < 0.01) return;
            float gain = (float)Math.Pow(10, gainDb / 20);
            int ch = audio.Channels, frames = audio.Frames, rate = audio.SampleRate;
            var s = audio.Samples;
            int look = Math.Max(1, rate / 200);
            float release = (float)Math.Exp(-1.0 / (0.08 * rate));

            // Required gain reduction per frame, then a sliding minimum over the look-ahead window
            // so the limiter starts reducing before a peak arrives.
            var need = new float[frames];
            for (int i = 0; i < frames; i++)
            {
                float peak = 0;
                for (int c = 0; c < ch; c++) peak = Math.Max(peak, Math.Abs(s[i * ch + c] * gain));
                need[i] = peak > ceiling ? ceiling / peak : 1f;
            }
            var deque = new LinkedList<int>();
            float env = 1f;
            for (int i = 0; i < frames; i++)
            {
                int j = i + look; // frame entering the look-ahead window
                if (j < frames)
                {
                    while (deque.Count > 0 && need[deque.Last.Value] >= need[j]) deque.RemoveLast();
                    deque.AddLast(j);
                }
                while (deque.Count > 0 && deque.First.Value < i) deque.RemoveFirst();
                float target = Math.Min(need[i], deque.Count > 0 ? need[deque.First.Value] : 1f);
                env = target < env ? target : target + (env - target) * release; // instant attack, smooth release
                float g = gain * Math.Min(env, need[i]);
                for (int c = 0; c < ch; c++) s[i * ch + c] *= g;
            }
        }

        // K-weighting filters for any sample rate (coefficients as derived in pyloudnorm).
        private static (double[] b, double[] a) HighShelf(int rate)
        {
            double G = 3.99984385397, Q = 0.7071752369554193, fc = 1681.9744509555319;
            double A = Math.Pow(10, G / 40), w0 = 2 * Math.PI * fc / rate, alpha = Math.Sin(w0) / (2 * Q), cos = Math.Cos(w0);
            double b0 = A * ((A + 1) + (A - 1) * cos + 2 * Math.Sqrt(A) * alpha);
            double b1 = -2 * A * ((A - 1) + (A + 1) * cos);
            double b2 = A * ((A + 1) + (A - 1) * cos - 2 * Math.Sqrt(A) * alpha);
            double a0 = (A + 1) - (A - 1) * cos + 2 * Math.Sqrt(A) * alpha;
            double a1 = 2 * ((A - 1) - (A + 1) * cos);
            double a2 = (A + 1) - (A - 1) * cos - 2 * Math.Sqrt(A) * alpha;
            return (new[] { b0 / a0, b1 / a0, b2 / a0 }, new[] { 1, a1 / a0, a2 / a0 });
        }

        private static (double[] b, double[] a) HighPass(int rate)
        {
            double Q = 0.5003270373253953, fc = 38.13547087613982;
            double w0 = 2 * Math.PI * fc / rate, alpha = Math.Sin(w0) / (2 * Q), cos = Math.Cos(w0);
            double b0 = (1 + cos) / 2, b1 = -(1 + cos), b2 = (1 + cos) / 2;
            double a0 = 1 + alpha, a1 = -2 * cos, a2 = 1 - alpha;
            return (new[] { b0 / a0, b1 / a0, b2 / a0 }, new[] { 1, a1 / a0, a2 / a0 });
        }
    }
}

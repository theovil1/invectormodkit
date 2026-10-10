using System;
using Newtonsoft.Json.Linq;

namespace InvectorModKit.Charting.Audio
{
    /// <summary>Where a song's reference timeline sits in a local audio file, and how sure we are.</summary>
    public sealed class FingerprintMatch
    {
        public static readonly FingerprintMatch None = new FingerprintMatch();

        public bool IsMatch;
        /// <summary>Local time = reference time + Offset (seconds).</summary>
        public double Offset;
        /// <summary>Correlation of the two attack envelopes at that offset (0..1).</summary>
        public double Score;
        /// <summary>Share of 20 s stretches of the song found at that same offset (1 = same version throughout).</summary>
        public double Consistency;

        public override string ToString() => $"{(IsMatch ? "match" : "no match")} offset {Offset:0.000}s score {Score:0.00} consistency {Consistency:0.00}";
    }

    /// <summary>
    /// A song's attack envelope (onset strength at 100 Hz, 8 bits): enough to recognise the song in another
    /// file and find how its timeline is shifted, while no sound can be rebuilt from it. Shared with charts
    /// on the marketplace so a chart can be placed on each player's own copy of the song.
    /// </summary>
    public sealed class Fingerprint
    {
        public const int Rate = 100;
        /// <summary>
        /// Lowest correlation accepted as the same song. ChartLab fpeval on 16 official songs: re-encoded copies
        /// with another intro score 0.90 or more, other songs 0.10 at most (offset error 2.4 ms at most).
        /// </summary>
        public static double MatchThreshold = 0.5;

        private const int MaxLag = 90 * Rate;
        private const int Window = 20 * Rate;
        private const int WindowSearch = 10 * Rate;
        private const int WindowTolerance = 3;
        private const double MinConsistency = 0.8;

        public double Duration;
        public byte[] Envelope;

        public static Fingerprint Of(DecodedAudio audio)
        {
            var f = AudioAnalyzer.AnalyzeOnsets(audio.ToMono(), audio.SampleRate);
            int n = (int)(audio.Duration * Rate);
            var env = new double[n];
            for (int k = 0; k < n; k++)
            {
                double p = (k / (double)Rate - f.TimeOffset) * f.FrameRate;
                int i = (int)Math.Floor(p);
                double a = i >= 0 && i < f.Frames ? f.Onset[i] : 0, b = i + 1 >= 0 && i + 1 < f.Frames ? f.Onset[i + 1] : 0;
                env[k] = a + (b - a) * (p - i);
            }
            var sorted = (double[])env.Clone();
            Array.Sort(sorted);
            double scale = n > 0 ? sorted[(int)(0.99 * (n - 1))] : 0;
            var bytes = new byte[n];
            if (scale > 1e-9)
                for (int k = 0; k < n; k++) bytes[k] = (byte)Math.Round(255 * Math.Min(1, Math.Max(0, env[k] / scale)));
            return new Fingerprint { Duration = audio.Duration, Envelope = bytes };
        }

        public string ToJson() =>
            new JObject { ["rate"] = Rate, ["duration"] = Math.Round(Duration, 3), ["data"] = Convert.ToBase64String(Envelope) }.ToString(Newtonsoft.Json.Formatting.None);

        public static Fingerprint FromJson(string json)
        {
            var o = JObject.Parse(json);
            if ((int?)o["rate"] != Rate) throw new FormatException("fingerprint rate must be " + Rate);
            return new Fingerprint { Duration = (double)o["duration"], Envelope = Convert.FromBase64String((string)o["data"]) };
        }

        /// <summary>Finds the reference song in the local file: global offset by cross-correlation, then a per-stretch check.</summary>
        public static FingerprintMatch Match(Fingerprint reference, Fingerprint local)
        {
            var r = Whiten(reference.Envelope);
            var l = Whiten(local.Envelope);
            if (r == null || l == null) return FingerprintMatch.None;
            int nr = r.Length, nl = l.Length;

            // c[k] = Σt r[t]·l[t+k], all lags at once with one FFT size covering both lengths.
            int size = 1;
            while (size < nr + nl) size <<= 1;
            var ar = new double[size]; var ai = new double[size];
            var br = new double[size]; var bi = new double[size];
            Array.Copy(r, ar, nr);
            Array.Copy(l, br, nl);
            var fft = new Fft(size);
            fft.Transform(ar, ai);
            fft.Transform(br, bi);
            for (int i = 0; i < size; i++)
            {
                double re = ar[i] * br[i] + ai[i] * bi[i], im = ar[i] * bi[i] - ai[i] * br[i]; // conj(A)·B
                ar[i] = re; ai[i] = -im; // conjugated: the forward FFT below then gives the inverse
            }
            fft.Transform(ar, ai);

            var r2 = Prefix(r);
            var l2 = Prefix(l);
            int minOverlap = Math.Min(Window, Math.Min(nr, nl) / 2);
            double Score(int k)
            {
                int t0 = Math.Max(0, -k), t1 = Math.Min(nr, nl - k);
                if (t1 - t0 < minOverlap) return double.NaN;
                double er = r2[t1] - r2[t0], el = l2[t1 + k] - l2[t0 + k];
                if (er <= 1e-9 || el <= 1e-9) return double.NaN;
                return ar[(k + size) % size] / size / Math.Sqrt(er * el);
            }

            int best = 0;
            double bestScore = double.NegativeInfinity;
            for (int k = -Math.Min(MaxLag, nr); k <= Math.Min(MaxLag, nl); k++)
            {
                double s = Score(k);
                if (!double.IsNaN(s) && s > bestScore) { bestScore = s; best = k; }
            }
            if (double.IsNegativeInfinity(bestScore)) return FingerprintMatch.None;

            double before = Score(best - 1), after = Score(best + 1), shift = 0;
            if (!double.IsNaN(before) && !double.IsNaN(after))
            {
                double curve = before - 2 * bestScore + after;
                if (curve < 0) shift = Math.Max(-0.5, Math.Min(0.5, 0.5 * (before - after) / curve));
            }

            double consistency = Consistency(r, l, best);
            return new FingerprintMatch
            {
                Offset = (best + shift) / Rate,
                Score = bestScore,
                Consistency = consistency,
                IsMatch = bestScore >= MatchThreshold && consistency >= MinConsistency,
            };
        }

        /// <summary>
        /// Share of the song's 20 s stretches whose own best alignment (searched ±10 s around the global one)
        /// is the global one. Music repeats every few bars, so a stretch still correlates fairly well a few
        /// bars off; what tells an edited version apart is that each stretch fits better somewhere else.
        /// </summary>
        private static double Consistency(double[] r, double[] l, int lag)
        {
            int considered = 0, consistent = 0;
            for (int w0 = 0; w0 + Window <= r.Length; w0 += Window)
            {
                int inside = Math.Min(w0 + Window + lag, l.Length) - Math.Max(w0 + lag, 0);
                if (inside < Window / 2) continue;
                double energy = 0;
                for (int t = w0; t < w0 + Window; t++) energy += r[t] * r[t];
                if (energy < 0.05 * Window) continue; // near silence: says nothing either way

                double atLag = double.NegativeInfinity, elsewhere = double.NegativeInfinity;
                for (int k = lag - WindowSearch; k <= lag + WindowSearch; k++)
                {
                    double s = WindowScore(r, l, w0, k);
                    if (Math.Abs(k - lag) <= WindowTolerance) atLag = Math.Max(atLag, s);
                    else elsewhere = Math.Max(elsewhere, s);
                }
                considered++;
                if (atLag > elsewhere) consistent++;
            }
            return considered == 0 ? 0 : consistent / (double)considered;
        }

        private static double WindowScore(double[] r, double[] l, int w0, int k)
        {
            double num = 0, er = 0, el = 0;
            int from = Math.Max(w0, -k), to = Math.Min(w0 + Window, l.Length - k);
            if (to - from < Window / 2) return double.NegativeInfinity;
            for (int t = from; t < to; t++)
            {
                double a = r[t], b = l[t + k];
                num += a * b; er += a * a; el += b * b;
            }
            return er <= 1e-9 || el <= 1e-9 ? double.NegativeInfinity : num / Math.Sqrt(er * el);
        }

        /// <summary>Attacks relative to their surroundings (1 s moving average removed), unit variance; null when flat.</summary>
        private static double[] Whiten(byte[] envelope)
        {
            int n = envelope?.Length ?? 0;
            if (n < 2 * Rate) return null;
            var sum = new double[n + 1];
            for (int i = 0; i < n; i++) sum[i + 1] = sum[i] + envelope[i];
            var y = new double[n];
            double mean = 0;
            for (int i = 0; i < n; i++)
            {
                int a = Math.Max(0, i - Rate / 2), b = Math.Min(n, i + Rate / 2 + 1);
                y[i] = envelope[i] - (sum[b] - sum[a]) / (b - a);
                mean += y[i];
            }
            mean /= n;
            double variance = 0;
            for (int i = 0; i < n; i++) variance += (y[i] - mean) * (y[i] - mean);
            double sd = Math.Sqrt(variance / n);
            if (sd < 1e-6) return null;
            for (int i = 0; i < n; i++) y[i] = (y[i] - mean) / sd;
            return y;
        }

        private static double[] Prefix(double[] x)
        {
            var p = new double[x.Length + 1];
            for (int i = 0; i < x.Length; i++) p[i + 1] = p[i] + x[i] * x[i];
            return p;
        }
    }
}

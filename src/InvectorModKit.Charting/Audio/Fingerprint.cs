using System;
using System.Collections.Generic;
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
        /// <summary>Mean correlation of the song's 20 s stretches at that offset, each within ±0.1 s of it (0..1).</summary>
        public double Score;
        /// <summary>Share of 20 s stretches of the song found at that same offset (1 = same version throughout).</summary>
        public double Consistency;
        /// <summary>Offset measured in each stretch found in place: (reference time of its middle, offset), in time order.</summary>
        public IReadOnlyList<(double Time, double Offset)> Anchors = new (double, double)[0];

        /// <summary>
        /// Local time - reference time at that point of the song: follows two copies whose timelines drift apart
        /// (linear between stretches, the nearest stretch's offset past either end, Offset without stretches).
        /// </summary>
        public double OffsetAt(double referenceTime)
        {
            if (Anchors.Count == 0) return Offset;
            if (referenceTime <= Anchors[0].Time) return Anchors[0].Offset;
            for (int i = 1; i < Anchors.Count; i++)
            {
                var (t1, o1) = Anchors[i];
                if (referenceTime > t1) continue;
                var (t0, o0) = Anchors[i - 1];
                return o0 + (o1 - o0) * (referenceTime - t0) / (t1 - t0);
            }
            return Anchors[Anchors.Count - 1].Offset;
        }

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
        /// <summary>
        /// How far a stretch may sit from the global alignment and still count as in place: two copies of one
        /// recording can drift apart by a few tens of ms (frames dropped by an encoder or a decoder), while an
        /// edited version moves stretches by beats or bars.
        /// </summary>
        private const int WindowTolerance = Rate / 10;
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

            var (score, consistency, anchors) = Stretches(r, l, best);
            if (double.IsNaN(score)) score = bestScore;
            return new FingerprintMatch
            {
                Offset = Peak(Score, best, bestScore) / Rate,
                Score = score,
                Consistency = consistency,
                Anchors = anchors,
                IsMatch = score >= MatchThreshold && consistency >= MinConsistency,
            };
        }

        /// <summary>
        /// The song's 20 s stretches against the global alignment: their mean correlation within ±0.1 s of it
        /// (NaN when no stretch says anything), the share whose own best alignment (searched ±10 s around)
        /// is that one, and the exact offset of each of those. Music repeats every few bars, so a stretch still
        /// correlates fairly well a few bars off; what tells an edited version apart is that each stretch fits
        /// better somewhere else.
        /// </summary>
        private static (double Score, double Consistency, List<(double Time, double Offset)> Anchors) Stretches(double[] r, double[] l, int lag)
        {
            int considered = 0, consistent = 0;
            double sum = 0;
            var anchors = new List<(double Time, double Offset)>();
            for (int w0 = 0; w0 + Window <= r.Length; w0 += Window)
            {
                int inside = Math.Min(w0 + Window + lag, l.Length) - Math.Max(w0 + lag, 0);
                if (inside < Window / 2) continue;
                double energy = 0;
                for (int t = w0; t < w0 + Window; t++) energy += r[t] * r[t];
                if (energy < 0.05 * Window) continue; // near silence: says nothing either way

                double atLag = double.NegativeInfinity, elsewhere = double.NegativeInfinity;
                int atK = lag;
                for (int k = lag - WindowSearch; k <= lag + WindowSearch; k++)
                {
                    double s = WindowScore(r, l, w0, k);
                    if (Math.Abs(k - lag) > WindowTolerance) elsewhere = Math.Max(elsewhere, s);
                    else if (s > atLag) { atLag = s; atK = k; }
                }
                considered++;
                if (double.IsNegativeInfinity(atLag)) continue;
                sum += atLag;
                if (atLag <= elsewhere) continue;
                consistent++;
                int start = w0;
                anchors.Add(((w0 + Window / 2.0) / Rate, Peak(k => WindowScore(r, l, start, k), atK, atLag) / Rate));
            }
            return considered == 0 ? (double.NaN, 0, anchors) : (sum / considered, consistent / (double)considered, anchors);
        }

        /// <summary>Where a correlation peaks around its best lag k, between frames (parabola through k-1, k, k+1).</summary>
        private static double Peak(Func<int, double> score, int k, double atK)
        {
            double before = score(k - 1), after = score(k + 1);
            if (double.IsNaN(before) || double.IsNaN(after) || double.IsInfinity(before) || double.IsInfinity(after)) return k;
            double curve = before - 2 * atK + after;
            return curve < 0 ? k + Math.Max(-0.5, Math.Min(0.5, 0.5 * (before - after) / curve)) : k;
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

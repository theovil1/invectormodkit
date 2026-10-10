using System;
using System.Collections.Generic;

namespace InvectorModKit.Charting.Audio
{
    public sealed class TempoEstimate
    {
        public double Bpm;
        /// <summary>Time of a downbeat (first beat of a bar), in [0, one bar).</summary>
        public double FirstBeat;
        /// <summary>How concentrated onset energy is on the beat phase (1 = no beat at all; 3+ = clear beat).</summary>
        public double Confidence;

        public double SecondsPerBeat => 60.0 / Bpm;
        public override string ToString() => $"{Bpm:0.###} BPM, first beat {FirstBeat:0.000}s, confidence {Confidence:0.00}";
    }

    /// <summary>
    /// Constant-tempo beat tracking: autocorrelation picks tempo candidates, then a fine search
    /// over BPM and phase maximises how much onset energy falls on the grid across the whole song.
    /// Precision matters: 0.05 BPM of error drifts ~70 ms over a 3-minute song.
    /// </summary>
    public static class TempoEstimator
    {
        public static double MinBpm = 70, MaxBpm = 185;
        /// <summary>Log-normal tempo prior, centred on the median tempo of the official songs.</summary>
        public static double PriorCenterBpm = 120, PriorOctaves = 0.9;
        private const int PhaseBins = 128;

        public static TempoEstimate Estimate(AudioFeatures f)
        {
            float[] env = Envelope(f);
            double fr = f.FrameRate;

            // 1) Coarse candidates from a multi-lag autocorrelation.
            int maxLag = (int)(fr * 60 / MinBpm * 4) + 2;
            var ac = Autocorrelation(env, maxLag);
            var coarse = new List<(double bpm, double score)>();
            double prevScore = 0, prevPrev = 0, prevBpm = 0;
            for (double bpm = MinBpm; bpm <= MaxBpm; bpm += 0.5)
            {
                double lag = fr * 60 / bpm;
                double s = Interp(ac, lag) + 0.5 * Interp(ac, 2 * lag) + 0.33 * Interp(ac, 3 * lag) + 0.25 * Interp(ac, 4 * lag);
                if (prevScore > s && prevScore >= prevPrev && prevBpm > 0) coarse.Add((prevBpm, prevScore));
                prevPrev = prevScore; prevScore = s; prevBpm = bpm;
            }
            coarse.Sort((a, b) => b.score.CompareTo(a.score));

            // 2) Fine search around the best candidates (and their octave neighbours).
            var seeds = new List<double>();
            foreach (var c in coarse)
            {
                if (seeds.Count >= 4) break;
                seeds.Add(c.bpm);
            }
            int baseSeeds = seeds.Count;
            for (int i = 0; i < baseSeeds; i++)
            {
                if (seeds[i] * 2 <= MaxBpm) seeds.Add(seeds[i] * 2);
                if (seeds[i] / 2 >= MinBpm) seeds.Add(seeds[i] / 2);
            }

            TempoEstimate best = null;
            double bestWeighted = double.MinValue;
            foreach (double seed in seeds)
            {
                var (bpm, phase, score) = Refine(env, fr, seed, 0.015, 120);
                (bpm, phase, score) = Refine(env, fr, bpm, 0.0005, 30);
                (bpm, phase, score) = SnapToRoundTempo(env, fr, bpm, phase, score);
                double weighted = score * Prior(bpm);
                if (weighted > bestWeighted)
                {
                    bestWeighted = weighted;
                    best = new TempoEstimate { Bpm = bpm, FirstBeat = phase, Confidence = score };
                }
            }
            if (best == null) return new TempoEstimate { Bpm = 120, FirstBeat = f.TimeOffset, Confidence = 0 };

            best.FirstBeat += f.TimeOffset; // phase was measured in frame time
            best.FirstBeat = PreferKickPhase(f, best.Bpm, best.FirstBeat);
            best.FirstBeat = FindDownbeat(f, best.Bpm, best.FirstBeat);
            return best;
        }

        /// <summary>Onset envelope used for beat tracking (broadband plus extra weight on kick/snare bands).</summary>
        private static float[] Envelope(AudioFeatures f)
        {
            var e = new float[f.Frames];
            for (int i = 0; i < e.Length; i++) e[i] = f.Onset[i] + 0.5f * f.OnsetLow[i] + 0.25f * f.OnsetMid[i];
            return e;
        }

        private static double Prior(double bpm)
        {
            double o = Math.Log(bpm / PriorCenterBpm, 2) / PriorOctaves;
            return Math.Exp(-0.5 * o * o);
        }

        private static double[] Autocorrelation(float[] x, int maxLag)
        {
            int n = x.Length;
            var ac = new double[maxLag + 1];
            for (int lag = 1; lag <= maxLag && lag < n; lag++)
            {
                double s = 0;
                for (int i = 0; i + lag < n; i++) s += x[i] * x[i + lag];
                ac[lag] = s / (n - lag);
            }
            return ac;
        }

        private static double Interp(double[] a, double x)
        {
            int i = (int)x;
            if (i < 0 || i + 1 >= a.Length) return 0;
            double t = x - i;
            return a[i] * (1 - t) + a[i + 1] * t;
        }

        /// <summary>Search bpm in center·(1 ± halfRange) in <paramref name="steps"/> steps; returns best (bpm, phase seconds, score).</summary>
        private static (double bpm, double phase, double score) Refine(float[] env, double fr, double center, double halfRange, int steps)
        {
            double bestBpm = center, bestPhase = 0, bestScore = double.MinValue;
            double lo = center * (1 - halfRange), step = 2 * center * halfRange / steps;
            for (int i = 0; i <= steps; i++)
            {
                double bpm = lo + i * step;
                var (phase, score) = PhaseScore(env, fr, bpm);
                if (score > bestScore) { bestScore = score; bestBpm = bpm; bestPhase = phase; }
            }
            return (bestBpm, bestPhase, bestScore);
        }

        /// <summary>Histogram of onset energy by beat phase; the peak gives the beat phase and how concentrated the energy is.</summary>
        internal static (double phase, double score) PhaseScore(float[] env, double fr, double bpm)
        {
            double period = fr * 60 / bpm;
            var hist = new double[PhaseBins];
            double total = 0;
            for (int t = 0; t < env.Length; t++)
            {
                float v = env[t];
                if (v <= 0) continue;
                double ph = (t % period) / period * PhaseBins;
                int b = (int)ph;
                double w = ph - b;
                hist[b % PhaseBins] += v * (1 - w);
                hist[(b + 1) % PhaseBins] += v * w;
                total += v;
            }
            if (total <= 0) return (0, 0);
            // Circular smoothing over ±4 bins (~3% of a beat) with a triangular kernel.
            int bestBin = 0;
            double bestVal = double.MinValue;
            for (int b = 0; b < PhaseBins; b++)
            {
                double s = 0;
                for (int k = -4; k <= 4; k++) s += hist[(b + k + PhaseBins) % PhaseBins] * (5 - Math.Abs(k));
                if (s > bestVal) { bestVal = s; bestBin = b; }
            }
            double phaseSeconds = bestBin / (double)PhaseBins * 60 / bpm;
            // 1.0 = onsets spread uniformly over the beat; higher = concentrated on the grid.
            return (phaseSeconds, bestVal / (25 * total) * PhaseBins);
        }

        /// <summary>Most produced music uses an integer (sometimes .5) BPM; prefer it when it fits as well.</summary>
        private static (double bpm, double phase, double score) SnapToRoundTempo(float[] env, double fr, double bpm, double phase, double score)
        {
            foreach (double candidate in new[] { Math.Round(bpm), Math.Round(bpm * 2) / 2 })
            {
                if (Math.Abs(candidate - bpm) > 0.3) continue;
                var (p, s) = PhaseScore(env, fr, candidate);
                if (s >= score * 0.985) return (candidate, p, s);
            }
            return (bpm, phase, score);
        }

        /// <summary>
        /// Songs with heavy off-beat accents can lock the grid half a beat off; kicks almost always sit
        /// on the beat, so keep whichever of the two phases carries more low-frequency onset energy.
        /// </summary>
        private static double PreferKickPhase(AudioFeatures f, double bpm, double phase)
        {
            double spb = 60 / bpm;
            double on = 0, off = 0;
            for (double t = phase; t + spb / 2 < f.Duration; t += spb)
            {
                on += f.MaxAround(f.OnsetLow, t, 1);
                off += f.MaxAround(f.OnsetLow, t + spb / 2, 1);
            }
            return off > on * KickPhaseRatio ? phase + spb / 2 : phase;
        }

        public static double KickPhaseRatio = 2.0;

        /// <summary>Picks which of the 4 beats of a bar is the downbeat (strongest low-frequency onsets).</summary>
        private static double FindDownbeat(AudioFeatures f, double bpm, double phase)
        {
            double spb = 60 / bpm;
            var sums = new double[4];
            int beat = 0;
            for (double t = phase; t < f.Duration; t += spb, beat++)
                sums[beat % 4] += f.MaxAround(f.OnsetLow, t, 1) + 0.5 * f.MaxAround(f.Onset, t, 1);
            int best = 0;
            for (int i = 1; i < 4; i++) if (sums[i] > sums[best]) best = i;
            return phase + best * spb;
        }
    }
}

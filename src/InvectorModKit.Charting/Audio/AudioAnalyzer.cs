using System;

namespace InvectorModKit.Charting.Audio
{
    /// <summary>Short-time spectral analysis: onset strength (SuperFlux-style), loudness, band energies.</summary>
    public static class AudioAnalyzer
    {
        public const int BandCount = 48;
        /// <summary>Semitone bands from 200 Hz (about 4 kHz at the top): the singing range.</summary>
        public const int FineBands = 52;
        private const double FineMinHz = 200;
        private const double MinHz = 30, MaxHz = 16000;
        private const double LowSplitHz = 160, HighSplitHz = 2500;
        /// <summary>Onsets are heard this fraction of half a window after the frame start (measured: 18.8 ms at 44.1 kHz).</summary>
        public static double OnsetLatency = 0.81;

        /// <summary>
        /// Stereo-aware analysis: besides everything <see cref="Analyze(float[], int)"/> computes, finds the
        /// sound panned to the centre (where lead vocals almost always sit, unlike strings, pads and reverb)
        /// to estimate sung attacks without a separation model.
        /// </summary>
        /// <param name="estimateVocals">False when a separated vocal track will provide the singing features.</param>
        public static AudioFeatures Analyze(DecodedAudio audio, bool estimateVocals = true)
        {
            if (audio.Channels < 2) return Analyze(audio.Samples, audio.SampleRate, null, null, true, estimateVocals);
            int frames = audio.Frames, ch = audio.Channels;
            var mono = new float[frames];
            var left = new float[frames];
            var right = new float[frames];
            for (int i = 0, o = 0; i < frames; i++, o += ch)
            {
                left[i] = audio.Samples[o];
                right[i] = audio.Samples[o + 1];
                float s = 0;
                for (int c = 0; c < ch; c++) s += audio.Samples[o + c];
                mono[i] = s / ch;
            }
            return Analyze(mono, audio.SampleRate, left, right, true, estimateVocals);
        }

        /// <param name="mono">Mono samples in [-1, 1].</param>
        public static AudioFeatures Analyze(float[] mono, int sampleRate) => Analyze(mono, sampleRate, null, null, true);

        /// <summary>Onsets and loudness only (no harmonic/percussive or vocal estimates): for separated stems.</summary>
        public static AudioFeatures AnalyzeOnsets(float[] mono, int sampleRate) => Analyze(mono, sampleRate, null, null, false);

        private static AudioFeatures Analyze(float[] mono, int sampleRate, float[] left, float[] right, bool full = true, bool estimateVocals = true)
        {
            bool stereo = left != null && right != null;
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
            // Semitone bands: sum the bins inside the band, or interpolate at its centre when it is narrower than a bin.
            var fineFrom = new int[FineBands]; var fineTo = new int[FineBands]; var fineFrac = new double[FineBands];
            for (int b = 0; b < FineBands; b++)
            {
                double lo = FineMinHz * Math.Pow(2, b / 12.0), hi = FineMinHz * Math.Pow(2, (b + 1) / 12.0), centre = Math.Sqrt(lo * hi);
                fineFrom[b] = (int)Math.Ceiling(lo * n / sampleRate);
                fineTo[b] = (int)Math.Ceiling(hi * n / sampleRate) - 1;
                fineFrac[b] = centre * n / sampleRate;
            }
            var fine = new float[frames][];
            var center = new float[frames][];
            var power = new double[bins];
            var centerPower = new double[bins];
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
                    // Stereo: one complex FFT of left + i·right gives both channels' spectra.
                    re[i] = (stereo ? left[off + i] : s) * window[i];
                    im[i] = stereo ? right[off + i] * window[i] : 0;
                }
                f.Rms[t] = (float)Math.Sqrt(sumSq / hop);
                fft.Transform(re, im);

                Array.Clear(bandEnergy, 0, BandCount);
                double magSum = 0, weighted = 0;
                for (int k = 1; k < bins; k++)
                {
                    double p;
                    if (stereo)
                    {
                        // L_k = (Z_k + conj Z_{n-k}) / 2,  R_k = (Z_k - conj Z_{n-k}) / 2i
                        double zr = re[k], zi = im[k], cr = re[n - k], ci = -im[n - k];
                        double lr = (zr + cr) / 2, li = (zi + ci) / 2;
                        double rr = (zi - ci) / 2, ri = -(zr - cr) / 2;
                        double mr = (lr + rr) / 2, mi = (li + ri) / 2;
                        p = mr * mr + mi * mi;
                        // Centre-ness: 1 when both channels carry the same thing, 0 when one side only or out of phase.
                        double dl = Math.Sqrt(lr * lr + li * li), dr = Math.Sqrt(rr * rr + ri * ri);
                        double diff = Math.Sqrt((lr - rr) * (lr - rr) + (li - ri) * (li - ri));
                        double sim = 1 - diff / (dl + dr + 1e-12);
                        centerPower[k] = p * sim * sim;
                    }
                    else
                    {
                        p = re[k] * re[k] + im[k] * im[k];
                        centerPower[k] = p;
                    }
                    power[k] = p;
                    int b = binBand[k];
                    if (b < 0) continue;
                    bandEnergy[b] += p;
                    double mag = Math.Sqrt(p);
                    magSum += mag;
                    weighted += mag * k * sampleRate / (double)n;
                }
                f.Centroid[t] = (float)(magSum > 1e-9 ? weighted / magSum : 0);
                if (full)
                {
                var fineRow = new float[FineBands];
                var centerRow = new float[FineBands];
                for (int b = 0; b < FineBands; b++)
                {
                    double e = 0, c = 0;
                    if (fineTo[b] >= fineFrom[b])
                        for (int k = fineFrom[b]; k <= fineTo[b] && k < bins; k++) { e += power[k]; c += centerPower[k]; }
                    else
                    {
                        int k0 = Math.Min(bins - 2, (int)fineFrac[b]);
                        double w = fineFrac[b] - k0;
                        e = power[k0] * (1 - w) + power[k0 + 1] * w;
                        c = centerPower[k0] * (1 - w) + centerPower[k0 + 1] * w;
                    }
                    fineRow[b] = (float)e;
                    centerRow[b] = (float)c;
                }
                fine[t] = fineRow;
                center[t] = centerRow;
                }
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
            if (!full) return f;
            SeparateHarmonicPercussive(f, fine);
            if (estimateVocals) EstimateVocals(f, fine, center);
            else { f.OnsetVocal = new float[frames]; f.VocalRatio = new float[frames]; }
            return f;
        }

        /// <summary>
        /// Sung attacks ≈ onsets of the harmonic part of the centre channel in the singing range, and how
        /// much of that range's energy is centred and sustained. For mono input this falls back to the
        /// harmonic part of the whole mix.
        /// </summary>
        private static void EstimateVocals(AudioFeatures f, float[][] fine, float[][] center)
        {
            int frames = f.Frames, k = FineBands;
            f.OnsetVocal = new float[frames];
            f.VocalRatio = new float[frames];
            var h = MedianAlongTime(center, k, 8);
            var p = MedianAlongFrequency(center, k, 3);
            var voc = new float[frames][];
            for (int t = 0; t < frames; t++)
            {
                voc[t] = new float[k];
                double vs = 0, all = 0;
                for (int b = 0; b < k; b++)
                {
                    double hh = h[t][b], pp = p[t][b];
                    double v = center[t][b] * (hh * hh / (hh * hh + pp * pp + 1e-18));
                    voc[t][b] = (float)Math.Log(1 + 1e4 * v);
                    vs += v; all += fine[t][b];
                }
                f.VocalRatio[t] = (float)(vs / (all + 1e-18));
            }
            Flux(voc, f.OnsetVocal);
            Normalize(f.OnsetVocal);
        }

        /// <summary>
        /// Median-filtering HPSS (Fitzgerald 2010): sustained partials are smooth along time, drum hits along
        /// frequency. Onsets of the harmonic part in the singing range (sung syllables, melody notes) and
        /// of the percussive part are then measured separately, plus how harmonic the sound is.
        /// </summary>
        private static void SeparateHarmonicPercussive(AudioFeatures f, float[][] fine)
        {
            int frames = f.Frames;
            f.OnsetHarmonic = new float[frames];
            f.OnsetPercussive = new float[frames];
            f.HarmonicRatio = new float[frames];

            // Harmonic part of the semitone spectrum (200 Hz - 4 kHz).
            int k = FineBands;
            var hTime = MedianAlongTime(fine, k, 8);
            var pFreq = MedianAlongFrequency(fine, k, 3);
            var harm = new float[frames][];
            for (int t = 0; t < frames; t++)
            {
                harm[t] = new float[k];
                double hs = 0, ps = 0;
                for (int b = 0; b < k; b++)
                {
                    double h = hTime[t][b], p = pFreq[t][b];
                    double mask = h * h / (h * h + p * p + 1e-18);
                    harm[t][b] = (float)Math.Log(1 + 1e4 * fine[t][b] * mask);
                    hs += h; ps += p;
                }
                f.HarmonicRatio[t] = (float)(hs / (hs + ps + 1e-18));
            }
            Flux(harm, f.OnsetHarmonic);

            // Percussive part of the full log-band spectrum.
            int nb = f.BandCount;
            var lin = new float[frames][];
            for (int t = 0; t < frames; t++)
            {
                lin[t] = new float[nb];
                for (int b = 0; b < nb; b++) lin[t][b] = (float)((Math.Exp(f.Bands[t][b]) - 1) / 1000);
            }
            var hb = MedianAlongTime(lin, nb, 8);
            var pb = MedianAlongFrequency(lin, nb, 2);
            var perc = new float[frames][];
            for (int t = 0; t < frames; t++)
            {
                perc[t] = new float[nb];
                for (int b = 0; b < nb; b++)
                {
                    double h = hb[t][b], p = pb[t][b];
                    perc[t][b] = (float)Math.Log(1 + 1000 * lin[t][b] * (p * p / (h * h + p * p + 1e-18)));
                }
            }
            Flux(perc, f.OnsetPercussive);
            Normalize(f.OnsetHarmonic);
            Normalize(f.OnsetPercussive);
        }

        /// <summary>SuperFlux: rise against the max of the neighbouring bins two frames back.</summary>
        private static void Flux(float[][] spec, float[] output)
        {
            for (int t = 2; t < spec.Length; t++)
            {
                var cur = spec[t];
                var prev = spec[t - 2];
                double sum = 0;
                for (int b = 0; b < cur.Length; b++)
                {
                    float r = prev[b];
                    if (b > 0 && prev[b - 1] > r) r = prev[b - 1];
                    if (b + 1 < cur.Length && prev[b + 1] > r) r = prev[b + 1];
                    if (cur[b] > r) sum += cur[b] - r;
                }
                output[t] = (float)sum;
            }
        }

        /// <summary>Running median along time: a sorted window updated by one removal and one insertion per step.</summary>
        private static float[][] MedianAlongTime(float[][] spec, int bands, int radius)
        {
            int frames = spec.Length;
            var result = new float[frames][];
            for (int t = 0; t < frames; t++) result[t] = new float[bands];
            var win = new float[2 * radius + 2];
            for (int b = 0; b < bands; b++)
            {
                int count = 0;
                for (int u = 0; u <= Math.Min(frames - 1, radius); u++) Insert(win, ref count, spec[u][b]);
                for (int t = 0; t < frames; t++)
                {
                    result[t][b] = win[count / 2];
                    int outgoing = t - radius, incoming = t + radius + 1;
                    if (outgoing >= 0) Remove(win, ref count, spec[outgoing][b]);
                    if (incoming < frames) Insert(win, ref count, spec[incoming][b]);
                }
            }
            return result;
        }

        private static void Insert(float[] win, ref int count, float v)
        {
            int i = count++;
            while (i > 0 && win[i - 1] > v) { win[i] = win[i - 1]; i--; }
            win[i] = v;
        }

        private static void Remove(float[] win, ref int count, float v)
        {
            int i = 0;
            while (i < count && win[i] != v) i++;
            if (i == count) return;
            for (; i < count - 1; i++) win[i] = win[i + 1];
            count--;
        }

        private static float[][] MedianAlongFrequency(float[][] spec, int bands, int radius)
        {
            int frames = spec.Length;
            var result = new float[frames][];
            var buf = new float[2 * radius + 1];
            for (int t = 0; t < frames; t++)
            {
                result[t] = new float[bands];
                var row = spec[t];
                for (int b = 0; b < bands; b++)
                {
                    int n = 0;
                    for (int c = Math.Max(0, b - radius); c <= Math.Min(bands - 1, b + radius); c++) Insert(buf, ref n, row[c]);
                    result[t][b] = buf[n / 2];
                }
            }
            return result;
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

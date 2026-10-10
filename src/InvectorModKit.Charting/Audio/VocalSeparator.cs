using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace InvectorModKit.Charting.Audio
{
    /// <summary>
    /// Vocal track estimate with Spleeter's 2-stems vocal network (Deezer, MIT; ONNX export from
    /// sherpa-onnx). The network takes 512-frame blocks of stereo STFT magnitudes (4096-point frames,
    /// 1024 hop, first 1024 bins) and returns the vocals' magnitudes; the ratio to the mix is used as a
    /// soft mask and the vocal signal rebuilt by overlap-add. Blocks are processed one at a time with
    /// reused buffers, so memory stays small; the frames of a block are transformed in parallel.
    /// </summary>
    public sealed class VocalSeparator : IDisposable
    {
        private const int N = 4096, Hop = 1024, Bins = 1024, Block = 512;
        /// <summary>Frames this many apart do not overlap.</summary>
        private const int Overlap = N / Hop;
        private readonly InferenceSession _session;
        private readonly string _input;
        private readonly double[] _window = new double[N];
        private readonly Fft _fft = new Fft(N);
        private readonly ParallelOptions _parallel;

        /// <summary>One frame's FFT buffers (one per worker thread).</summary>
        private sealed class Frame { public readonly double[] Re = new double[N], Im = new double[N]; }

        public VocalSeparator(string modelPath, int threads = 0)
        {
            var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
            if (threads > 0) options.IntraOpNumThreads = threads;
            _parallel = new ParallelOptions { MaxDegreeOfParallelism = threads > 0 ? threads : Environment.ProcessorCount };
            _session = new InferenceSession(modelPath, options);
            foreach (var name in _session.InputMetadata.Keys) { _input = name; break; }
            for (int i = 0; i < N; i++) _window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / N); // periodic Hann
        }

        /// <summary>Mono vocal estimate, same rate and length as the input.</summary>
        public float[] SeparateMono(DecodedAudio audio)
        {
            int frames = audio.Frames, ch = audio.Channels;
            var channels = new float[2][];
            for (int c = 0; c < 2; c++)
            {
                channels[c] = new float[frames];
                int src = Math.Min(c, ch - 1);
                for (int i = 0; i < frames; i++) channels[c][i] = audio.Samples[i * ch + src];
            }

            int stftFrames = frames / Hop + 1; // centred frames
            var output = new float[frames];
            var norm = new float[frames];
            // Only the first 1024 bins are kept: the network ignores the rest and they are muted on output.
            var specRe = new float[2 * Block * Bins];
            var specIm = new float[2 * Block * Bins];
            var magnitudes = new float[2 * Block * Bins];
            var vocals = new float[2 * Block * Bins];

            for (int start = 0; start < stftFrames; start += Block)
            {
                int first = start, count = Math.Min(Block, stftFrames - start);
                Array.Clear(magnitudes, 0, magnitudes.Length);
                Parallel.For(0, count, _parallel, () => new Frame(), (k, state, frame) =>
                {
                    // One complex FFT of left + i·right gives both channels' spectra.
                    Analyse(channels[0], channels[1], (first + k) * Hop - N / 2, frame);
                    int atL = k * Bins, atR = (Block + k) * Bins;
                    for (int b = 0; b < Bins; b++)
                    {
                        int m = b == 0 ? 0 : N - b;
                        double zr = frame.Re[b], zi = frame.Im[b], cr = frame.Re[m], ci = -frame.Im[m];
                        double lr = (zr + cr) / 2, li = (zi + ci) / 2;   // L = (Z + conj Z[N-b]) / 2
                        double rr = (zi - ci) / 2, ri = -(zr - cr) / 2;  // R = (Z - conj Z[N-b]) / 2i
                        specRe[atL + b] = (float)lr; specIm[atL + b] = (float)li;
                        specRe[atR + b] = (float)rr; specIm[atR + b] = (float)ri;
                        magnitudes[atL + b] = (float)Math.Sqrt(lr * lr + li * li);
                        magnitudes[atR + b] = (float)Math.Sqrt(rr * rr + ri * ri);
                    }
                    return frame;
                }, frame => { });

                var tensor = new DenseTensor<float>(magnitudes, new[] { 2, 1, Block, Bins });
                using (var results = _session.Run(new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_input, tensor) }))
                {
                    foreach (var r in results) { r.AsTensor<float>().ToDenseTensor().Buffer.Span.CopyTo(vocals); break; }
                    // Frames Overlap apart do not overlap, so each pass can overlap-add in parallel.
                    for (int phase = 0; phase < Overlap; phase++)
                    {
                        int p = phase;
                        Parallel.For(0, (count - p + Overlap - 1) / Overlap, _parallel, () => new Frame(), (j, state, frame) =>
                        {
                            int k = p + j * Overlap;
                            Synthesize(specRe, specIm, vocals, magnitudes, k * Bins, (Block + k) * Bins, (first + k) * Hop - N / 2, output, norm, frame);
                            return frame;
                        }, frame => { });
                    }
                }
            }
            for (int i = 0; i < frames; i++) output[i] = norm[i] > 1e-6f ? output[i] / norm[i] : 0f;
            return output;
        }

        /// <summary>Windowed FFT of left + i·right for one frame starting at <paramref name="offset"/> (reflect padding).</summary>
        private void Analyse(float[] left, float[] right, int offset, Frame frame)
        {
            int last = left.Length - 1;
            for (int i = 0; i < N; i++)
            {
                int j = offset + i;
                if (j < 0) j = -j;
                if (j > last) j = 2 * last - j;
                bool inside = j >= 0 && j <= last;
                frame.Re[i] = (inside ? left[j] : 0) * _window[i];
                frame.Im[i] = (inside ? right[j] : 0) * _window[i];
            }
            _fft.Transform(frame.Re, frame.Im);
        }

        /// <summary>
        /// Masks both channels with vocals/mix and rebuilds their average with one inverse FFT (the
        /// average of two inverse transforms is the inverse transform of the average), then overlap-adds.
        /// </summary>
        private void Synthesize(float[] specRe, float[] specIm, float[] vocals, float[] mix, int atL, int atR, int offset, float[] output, float[] norm, Frame frame)
        {
            double[] fr = frame.Re, fi = frame.Im;
            Array.Clear(fr, 0, N);
            Array.Clear(fi, 0, N);
            for (int b = 0; b < Bins; b++)
            {
                float mL = mix[atL + b] > 1e-8f ? Math.Min(1f, Math.Max(0f, vocals[atL + b] / mix[atL + b])) : 0f;
                float mR = mix[atR + b] > 1e-8f ? Math.Min(1f, Math.Max(0f, vocals[atR + b] / mix[atR + b])) : 0f;
                fr[b] = 0.5 * (specRe[atL + b] * mL + specRe[atR + b] * mR);
                fi[b] = 0.5 * (specIm[atL + b] * mL + specIm[atR + b] * mR);
                if (b > 0) { fr[N - b] = fr[b]; fi[N - b] = -fi[b]; } // Hermitian: real output
            }
            // ifft(X) = conj(fft(conj(X))) / N; the result is real, so only the real part is needed.
            for (int i = 0; i < N; i++) fi[i] = -fi[i];
            _fft.Transform(fr, fi);
            for (int i = 0; i < N; i++)
            {
                int j = offset + i;
                if (j < 0 || j >= output.Length) continue;
                output[j] += (float)(fr[i] / N * _window[i]);
                norm[j] += (float)(_window[i] * _window[i]);
            }
        }

        public void Dispose() => _session.Dispose();
    }
}

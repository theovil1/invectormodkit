using System;

namespace InvectorModKit.Charting.Audio
{
    /// <summary>In-place iterative radix-2 complex FFT with cached twiddles for one size.</summary>
    internal sealed class Fft
    {
        private readonly int _n;
        private readonly int[] _bitReverse;
        private readonly double[] _cos, _sin;

        public Fft(int n)
        {
            if (n < 2 || (n & (n - 1)) != 0) throw new ArgumentException("FFT size must be a power of two.", nameof(n));
            _n = n;
            int bits = 0;
            while ((1 << bits) < n) bits++;
            _bitReverse = new int[n];
            for (int i = 0; i < n; i++)
            {
                int r = 0;
                for (int b = 0; b < bits; b++) r |= ((i >> b) & 1) << (bits - 1 - b);
                _bitReverse[i] = r;
            }
            _cos = new double[n / 2];
            _sin = new double[n / 2];
            for (int i = 0; i < n / 2; i++)
            {
                _cos[i] = Math.Cos(-2 * Math.PI * i / n);
                _sin[i] = Math.Sin(-2 * Math.PI * i / n);
            }
        }

        public int Size => _n;

        public void Transform(double[] re, double[] im)
        {
            for (int i = 0; i < _n; i++)
            {
                int j = _bitReverse[i];
                if (j > i)
                {
                    (re[i], re[j]) = (re[j], re[i]);
                    (im[i], im[j]) = (im[j], im[i]);
                }
            }
            for (int size = 2; size <= _n; size <<= 1)
            {
                int half = size >> 1, step = _n / size;
                for (int start = 0; start < _n; start += size)
                {
                    for (int k = 0; k < half; k++)
                    {
                        double wr = _cos[k * step], wi = _sin[k * step];
                        int a = start + k, b = a + half;
                        double tr = re[b] * wr - im[b] * wi;
                        double ti = re[b] * wi + im[b] * wr;
                        re[b] = re[a] - tr; im[b] = im[a] - ti;
                        re[a] += tr; im[a] += ti;
                    }
                }
            }
        }
    }
}

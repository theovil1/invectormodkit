namespace InvectorModKit.Charting.Generation
{
    /// <summary>Small deterministic PRNG (xorshift64*), identical on Mono and .NET, so a song always gets the same chart.</summary>
    public sealed class Rng
    {
        private ulong _s;

        public Rng(ulong seed) { _s = seed == 0 ? 0x9E3779B97F4A7C15UL : seed; }

        public static Rng FromString(string s)
        {
            ulong h = 1469598103934665603UL; // FNV-1a
            foreach (char c in s ?? "") { h ^= c; h *= 1099511628211UL; }
            return new Rng(h);
        }

        public ulong NextULong()
        {
            _s ^= _s >> 12; _s ^= _s << 25; _s ^= _s >> 27;
            return _s * 2685821657736338717UL;
        }

        /// <summary>Uniform in [0, 1).</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

        public int Next(int maxExclusive) => (int)(NextDouble() * maxExclusive);

        public bool Chance(double p) => NextDouble() < p;
    }
}

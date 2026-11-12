using System;

namespace InvectorModKit.Charting
{
    /// <summary>
    /// The cover of a song that has none: a white pixel note (two beamed eighths) with a dark shadow, on diagonal bands
    /// of big blocks in colours of the song's own (the same song always gets the same ones). Drawn on
    /// <see cref="Size"/> x <see cref="Size"/> pixels, to show with nearest filtering so the pixels stay square.
    /// </summary>
    public static class CoverArt
    {
        public const int Size = 32;

        // Beamed eighth notes, 14 x 12, drawn twice as big.
        private static readonly string[] Note =
        {
            "00000111111111", "00000111111111", "00000100000001", "00000100000001", "00000100000001", "00000100000001",
            "00000100000001", "00000100000001", "01111100011111", "11111100111111", "11111100111111", "01111000011110",
        };

        /// <summary>RGBA, 4 bytes a pixel, rows from the bottom up (a texture's order). <paramref name="key"/>: the song (its title).</summary>
        public static byte[] Rgba32(string key)
        {
            float hue = (Hash(key ?? "") & 0xffff) / 65535f;
            var bright = Hsv(hue, 0.65f, 0.85f);
            var dark = Hsv(hue + 0.15f, 0.8f, 0.35f);
            var shadow = Hsv(hue + 0.15f, 0.8f, 0.2f);
            var white = (r: (byte)255, g: (byte)255, b: (byte)255);

            var rgba = new byte[Size * Size * 4];
            void Put(int x, int y, (byte r, byte g, byte b) c) // y from the top
            {
                int i = ((Size - 1 - y) * Size + x) * 4;
                rgba[i] = c.r;
                rgba[i + 1] = c.g;
                rgba[i + 2] = c.b;
                rgba[i + 3] = 255;
            }

            // Five bands of 4 x 4 blocks, dark at the bottom left, bright at the top right.
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    int band = Math.Min(4, (x / 4 + (Size - 1 - y) / 4) * 5 / 16);
                    Put(x, y, Mix(dark, bright, band / 4f));
                }

            int x0 = (Size - Note[0].Length * 2) / 2, y0 = (Size - Note.Length * 2) / 2;
            foreach (var (dx, color) in new[] { (1, shadow), (0, white) })
                for (int y = 0; y < Note.Length * 2; y++)
                    for (int x = 0; x < Note[0].Length * 2; x++)
                        if (Note[y / 2][x / 2] == '1') Put(x0 + x + dx, y0 + y + dx, color);
            return rgba;
        }

        /// <summary>FNV-1a: the same on every run and every PC, unlike string.GetHashCode.</summary>
        private static uint Hash(string s)
        {
            uint h = 2166136261;
            foreach (char c in s) { h ^= c; h *= 16777619; }
            return h;
        }

        private static (byte r, byte g, byte b) Mix((byte r, byte g, byte b) a, (byte r, byte g, byte b) b, float t) =>
            ((byte)(a.r + (b.r - a.r) * t), (byte)(a.g + (b.g - a.g) * t), (byte)(a.b + (b.b - a.b) * t));

        private static (byte r, byte g, byte b) Hsv(float h, float s, float v)
        {
            h = (h % 1f + 1f) % 1f * 6f;
            int sector = (int)h % 6;
            float f = h - (int)h, p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
            float r, g, b;
            switch (sector)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
            return ((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }
    }
}

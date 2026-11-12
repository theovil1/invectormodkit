using System;
using System.Collections.Generic;
using InvectorModKit.Charting;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InvectorModKit.CustomSongs
{
    /// <summary>
    /// Song-select artwork: the song's cover if it has one, otherwise a pixel note in colours of the song's own (<see cref="CoverArt"/>).
    /// Official covers are square, so other shapes (video thumbnails) are cropped to fill like them.
    /// </summary>
    internal static class Covers
    {
        public static Sprite Make(CustomSong song)
        {
            Texture2D tex = null;
            if (song.Cover != null)
            {
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(tex, song.Cover))
                {
                    Object.Destroy(tex);
                    tex = null;
                }
            }
            if (tex == null) return Drawn(song.Title ?? song.Id);
            tex.wrapMode = TextureWrapMode.Clamp;
            GameAssets.Keep(tex);
            return GameAssets.Keep(Sprite.Create(tex, SquareCrop(tex), new Vector2(0.5f, 0.5f)));
        }

        /// <summary>
        /// The largest centred square of the picture, once the bars around it are removed: video thumbnails
        /// usually come as a 16:9 frame padded into a square with transparent (or black) bars.
        /// </summary>
        private static Rect SquareCrop(Texture2D tex)
        {
            int w = tex.width, h = tex.height;
            var px = tex.GetPixels32();
            bool DarkRow(int y) { for (int x = 0; x < w; x++) if (IsPicture(px[y * w + x])) return false; return true; }
            bool DarkColumn(int x, int y0, int y1) { for (int y = y0; y < y1; y++) if (IsPicture(px[y * w + x])) return false; return true; }

            // Bars come in pairs of about the same size; a dark picture with one dark edge keeps it.
            int bottom = 0, top = 0;
            while (bottom < h / 4 && DarkRow(bottom)) bottom++;
            while (top < h / 4 && DarkRow(h - 1 - top)) top++;
            int bar = Math.Min(bottom, top) >= h / 50 ? Math.Min(bottom, top) : 0;
            int y0 = bar, y1 = h - bar;
            int left = 0, right = 0;
            while (left < w / 4 && DarkColumn(left, y0, y1)) left++;
            while (right < w / 4 && DarkColumn(w - 1 - right, y0, y1)) right++;
            int side = Math.Min(left, right) >= w / 50 ? Math.Min(left, right) : 0;
            int x0 = side, x1 = w - side;

            int size = Math.Min(x1 - x0, y1 - y0);
            return new Rect(x0 + (x1 - x0 - size) / 2, y0 + (y1 - y0 - size) / 2, size, size);
        }

        /// <summary>Not padding: visible, and not black.</summary>
        private static bool IsPicture(Color32 c) => c.a >= 16 && (c.r > 40 || c.g > 40 || c.b > 40);

        private static readonly Dictionary<string, Sprite> DrawnCovers = new Dictionary<string, Sprite>();

        /// <summary>The cover drawn for a song without one, made once per title: big square pixels (nearest filtering).</summary>
        public static Sprite Drawn(string title)
        {
            title = title ?? "";
            if (DrawnCovers.TryGetValue(title, out var known) && known) return known;
            var tex = new Texture2D(CoverArt.Size, CoverArt.Size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            tex.LoadRawTextureData(CoverArt.Rgba32(title));
            tex.Apply();
            GameAssets.Keep(tex);
            return DrawnCovers[title] = GameAssets.Keep(Sprite.Create(tex, new Rect(0, 0, CoverArt.Size, CoverArt.Size), new Vector2(0.5f, 0.5f)));
        }
    }
}

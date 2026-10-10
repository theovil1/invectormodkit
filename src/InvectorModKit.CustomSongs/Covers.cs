using UnityEngine;

namespace InvectorModKit.CustomSongs
{
    /// <summary>Song-select artwork: the song's cover if it has one, otherwise a colour gradient unique to the song.</summary>
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
            tex = tex ?? Placeholder(song.Id);
            tex.wrapMode = TextureWrapMode.Clamp;
            GameAssets.Keep(tex);
            return GameAssets.Keep(Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f)));
        }

        private static Texture2D Placeholder(string id)
        {
            const int size = 256;
            float hue = (id.GetHashCode() & 0xffff) / 65535f;
            Color a = Color.HSVToRGB(hue, 0.65f, 0.85f), b = Color.HSVToRGB((hue + 0.15f) % 1f, 0.8f, 0.35f);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    pixels[y * size + x] = Color.Lerp(b, a, (x + y) / (2f * size));
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }
    }
}

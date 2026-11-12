using System;

namespace InvectorModKit.CustomSongs
{
    /// <summary>
    /// Where the editor skin copies a sprite's pixels from in its texture. Pure, so the editor's tests cover it.
    /// </summary>
    internal static class EditorSkinRegion
    {
        /// <summary>
        /// The whole-pixel rectangle of a sprite in its texture (left, bottom, width, height). False when there is no
        /// rectangle to copy: the sprite is tightly packed in an atlas (its pixels follow its mesh, so Unity has no
        /// texture rectangle for it), its rectangle is empty, or it does not fit in the texture.
        /// </summary>
        public static bool TryGet(bool tightlyPacked, float x, float y, float width, float height, int textureWidth, int textureHeight,
            out int left, out int bottom, out int w, out int h)
        {
            left = (int)Math.Round(x);
            bottom = (int)Math.Round(y);
            w = (int)Math.Round(width);
            h = (int)Math.Round(height);
            return !tightlyPacked && w > 0 && h > 0 && left >= 0 && bottom >= 0
                && left + w <= textureWidth && bottom + h <= textureHeight;
        }
    }
}

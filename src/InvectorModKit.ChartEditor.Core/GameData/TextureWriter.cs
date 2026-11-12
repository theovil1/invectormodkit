using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace InvectorModKit.ChartEditor
{
    /// <summary>A Texture2D as the game stores it: Unity's format number, size, mip chain bytes and sampling settings.</summary>
    public sealed class TextureData
    {
        public int Format, Width, Height, MipCount, ColorSpace, Filter, WrapU, WrapV;
        public byte[] Bytes;
    }

    /// <summary>Writes a texture to the cache unchanged: a header line, then the bytes as stored by the game.</summary>
    public static class TextureWriter
    {
        /// <summary>The formats the preview can upload to WebGL 2: Alpha8, RGB24, RGBA32, DXT1, DXT5, RGBAHalf, BC6H, BC7.</summary>
        public static readonly IReadOnlyCollection<int> Supported = new HashSet<int> { 1, 3, 4, 10, 12, 17, 24, 25 };

        /// <summary>Crunch-compressed DXT1, DXT5, ETC RGB4 and ETC2 RGBA8.</summary>
        public static bool IsCrunched(int format) => format == 28 || format == 29 || format == 64 || format == 65;

        public static void Write(TextureData texture, Stream output)
        {
            CacheFiles.WriteHeaderLine(output, new JObject
            {
                ["format"] = texture.Format,
                ["width"] = texture.Width,
                ["height"] = texture.Height,
                ["mips"] = texture.MipCount,
                ["colorSpace"] = texture.ColorSpace,
                ["filter"] = texture.Filter,
                ["wrap"] = new JArray(texture.WrapU, texture.WrapV),
            });
            output.Write(texture.Bytes, 0, texture.Bytes.Length);
        }
    }
}

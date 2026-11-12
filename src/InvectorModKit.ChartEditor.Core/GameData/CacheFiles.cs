using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InvectorModKit.ChartEditor
{
    /// <summary>
    /// Names and headers of the game data cache's files. Binary files (meshes, textures) start with one line of JSON,
    /// padded with spaces so that the line, its "\n" included, fills a multiple of 4 bytes: the data after it can be
    /// viewed as 32-bit arrays without copying.
    /// </summary>
    public static class CacheFiles
    {
        /// <summary>A file name made of letters, digits, '-' and '_' (anything else becomes '_').</summary>
        public static string FileSafe(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_' ? c : '_');
            return sb.ToString();
        }

        public static void WriteHeaderLine(Stream output, JObject header)
        {
            byte[] json = Encoding.UTF8.GetBytes(header.ToString(Formatting.None));
            int padding = (4 - (json.Length + 1) % 4) % 4;
            output.Write(json, 0, json.Length);
            for (int i = 0; i < padding; i++) output.WriteByte((byte)' ');
            output.WriteByte((byte)'\n');
        }

        /// <summary>Reads the header line and leaves the stream just after its "\n".</summary>
        public static JObject ReadHeaderLine(Stream input)
        {
            var line = new MemoryStream();
            int b;
            while ((b = input.ReadByte()) != '\n')
            {
                if (b < 0) throw new InvalidDataException("cache file without a header line");
                line.WriteByte((byte)b);
            }
            return JObject.Parse(Encoding.UTF8.GetString(line.ToArray()));
        }
    }
}

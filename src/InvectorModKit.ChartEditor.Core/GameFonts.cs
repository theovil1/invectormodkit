using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace InvectorModKit.ChartEditor
{
    /// <summary>
    /// The game's own fonts (Exo 2 Condensed), read from its Addressables bundles on this computer for the editor's
    /// look: UnityFS bundles are opened (LZ4 blocks), and the OpenType files embedded in their Font assets are cut out.
    /// Nothing is downloaded or redistributed.
    /// </summary>
    public static class GameFonts
    {
        /// <summary>Writes the fonts whose name contains <paramref name="family"/> to outDir; returns their file names.</summary>
        public static List<string> Extract(string gameDir, string outDir, string family = "Exo2", Action<string> log = null)
        {
            var found = new List<string>();
            string bundles = Path.Combine(gameDir, "Invector Rhythm Galaxy_Data", "StreamingAssets", "aa", "StandaloneWindows64");
            if (!Directory.Exists(bundles)) return found;
            Directory.CreateDirectory(outDir);
            // Interface bundles first; songs, levels, worlds and videos never hold the fonts.
            var files = Directory.GetFiles(bundles, "*.bundle", SearchOption.AllDirectories)
                .Where(f => !Regex(Path.GetFileName(f)))
                .OrderBy(f => new FileInfo(f).Length)
                .ToList();
            foreach (string file in files)
            {
                try
                {
                    foreach (var data in UnityFsData(file))
                        foreach (var font in CarveFonts(data))
                        {
                            if (font.Name.IndexOf(family, StringComparison.OrdinalIgnoreCase) < 0) continue;
                            string name = Safe(font.Name) + (font.OpenType ? ".otf" : ".ttf");
                            if (found.Contains(name)) continue;
                            File.WriteAllBytes(Path.Combine(outDir, name), font.Bytes);
                            found.Add(name);
                            log?.Invoke($"{name} from {Path.GetFileName(file)}");
                        }
                }
                catch (Exception e) { log?.Invoke($"{Path.GetFileName(file)}: {e.Message}"); }
                if (found.Count >= 4) break; // Regular, Medium, SemiBold, Bold
            }
            return found;
        }

        private static bool Regex(string name) =>
            name.StartsWith("basegame_assets_audio-") || name.StartsWith("basegame_assets_level-") || name.Contains("exit.bundle") || name.Contains("intro.bundle")
            || name.Contains(".mp4.") || name.Contains(".webm.") || name.Contains("unitybuiltinshaders");

        // ----- UnityFS

        /// <summary>The decompressed content of a UnityFS bundle (one array; null when a block uses LZMA).</summary>
        public static IEnumerable<byte[]> UnityFsData(string path)
        {
            using (var s = File.OpenRead(path))
            using (var r = new BinaryReader(s))
            {
                if (CString(r) != "UnityFS") yield break;
                uint version = BE32(r);
                CString(r);
                CString(r);
                long size = BE64(r);
                uint infoCompressed = BE32(r), infoSize = BE32(r), flags = BE32(r);
                if (version >= 7) Align(s, 16);
                long afterHeader = s.Position;
                byte[] packedInfo;
                if ((flags & 0x80) != 0)
                {
                    s.Position = s.Length - infoCompressed;
                    packedInfo = r.ReadBytes((int)infoCompressed);
                    s.Position = afterHeader;
                }
                else packedInfo = r.ReadBytes((int)infoCompressed);
                if ((flags & 0x200) != 0) Align(s, 16);
                byte[] info = Decompress(packedInfo, (int)infoSize, (int)(flags & 0x3F));
                if (info == null) yield break;

                var blocks = new List<(int Size, int Packed, int Kind)>();
                using (var ir = new BinaryReader(new MemoryStream(info)))
                {
                    ir.ReadBytes(16);
                    int count = (int)BE32(ir);
                    for (int i = 0; i < count; i++) blocks.Add(((int)BE32(ir), (int)BE32(ir), BE16(ir) & 0x3F));
                }
                long total = blocks.Sum(b => (long)b.Size);
                if (total > 400L * 1024 * 1024 || blocks.Any(b => b.Kind == 1)) yield break; // too big, or LZMA
                var data = new byte[total];
                int at = 0;
                foreach (var b in blocks)
                {
                    byte[] packed = r.ReadBytes(b.Packed);
                    if (b.Kind == 0) Buffer.BlockCopy(packed, 0, data, at, b.Size);
                    else Lz4Decode(packed, 0, packed.Length, data, at, b.Size);
                    at += b.Size;
                }
                yield return data;
            }
        }

        private static byte[] Decompress(byte[] packed, int size, int kind)
        {
            if (kind == 0) return packed;
            if (kind != 2 && kind != 3) return null;
            var output = new byte[size];
            Lz4Decode(packed, 0, packed.Length, output, 0, size);
            return output;
        }

        /// <summary>LZ4 block format (what UnityFS uses for its LZ4 and LZ4HC blocks).</summary>
        public static int Lz4Decode(byte[] src, int srcOffset, int srcLength, byte[] dst, int dstOffset, int dstLength)
        {
            int s = srcOffset, end = srcOffset + srcLength, d = dstOffset, dEnd = dstOffset + dstLength;
            while (s < end)
            {
                int token = src[s++];
                int literals = token >> 4;
                if (literals == 15) { int b; do { b = src[s++]; literals += b; } while (b == 255); }
                Buffer.BlockCopy(src, s, dst, d, literals);
                s += literals;
                d += literals;
                if (s >= end) break;
                int offset = src[s] | (src[s + 1] << 8);
                s += 2;
                int length = token & 15;
                if (length == 15) { int b; do { b = src[s++]; length += b; } while (b == 255); }
                length += 4;
                if (offset == 0 || d - offset < dstOffset || d + length > dEnd) throw new InvalidDataException("corrupt LZ4 block");
                for (int from = d - offset, i = 0; i < length; i++) dst[d++] = dst[from++];
            }
            return d - dstOffset;
        }

        // ----- OpenType

        public sealed class FontFile
        {
            public string Name;
            public bool OpenType;
            public byte[] Bytes;
        }

        /// <summary>Every complete sfnt (OpenType "OTTO" or TrueType 0x00010000) inside a byte array.</summary>
        public static IEnumerable<FontFile> CarveFonts(byte[] data)
        {
            for (int i = 0; i + 12 < data.Length; i++)
            {
                bool otto = data[i] == 'O' && data[i + 1] == 'T' && data[i + 2] == 'T' && data[i + 3] == 'O';
                bool ttf = data[i] == 0 && data[i + 1] == 1 && data[i + 2] == 0 && data[i + 3] == 0;
                if (!otto && !ttf) continue;
                int tables = (data[i + 4] << 8) | data[i + 5];
                if (tables < 4 || tables > 64 || i + 12 + tables * 16 > data.Length) continue;
                long length = 0;
                int nameOffset = -1, nameLength = 0;
                bool valid = true;
                for (int t = 0; t < tables && valid; t++)
                {
                    int rec = i + 12 + t * 16;
                    for (int k = 0; k < 4; k++) if (data[rec + k] < 32 || data[rec + k] > 126) valid = false;
                    long offset = U32(data, rec + 8), len = U32(data, rec + 12);
                    if (offset + len > data.Length - i || offset < 12) valid = false;
                    length = Math.Max(length, offset + len);
                    if (Encoding.ASCII.GetString(data, rec, 4) == "name") { nameOffset = (int)offset; nameLength = (int)len; }
                }
                if (!valid || nameOffset < 0 || length < 1024) continue;
                var bytes = new byte[length];
                Buffer.BlockCopy(data, i, bytes, 0, (int)length);
                string name = FontName(bytes, nameOffset, nameLength);
                if (name == null) continue;
                yield return new FontFile { Name = name, OpenType = otto, Bytes = bytes };
                i += (int)length - 1;
            }
        }

        /// <summary>PostScript name (nameID 6), else full name (4), from the name table.</summary>
        private static string FontName(byte[] font, int table, int length)
        {
            if (table + 6 > font.Length) return null;
            int count = (font[table + 2] << 8) | font[table + 3], strings = table + ((font[table + 4] << 8) | font[table + 5]);
            string best = null;
            for (int k = 0; k < count; k++)
            {
                int rec = table + 6 + k * 12;
                if (rec + 12 > font.Length) break;
                int platform = (font[rec] << 8) | font[rec + 1], nameId = (font[rec + 6] << 8) | font[rec + 7];
                int len = (font[rec + 8] << 8) | font[rec + 9], off = (font[rec + 10] << 8) | font[rec + 11];
                if ((nameId != 6 && nameId != 4) || strings + off + len > font.Length) continue;
                string text = platform == 3 || platform == 0 ? Encoding.BigEndianUnicode.GetString(font, strings + off, len) : Encoding.ASCII.GetString(font, strings + off, len);
                if (nameId == 6) return text;
                best = best ?? text;
            }
            return best;
        }

        private static long U32(byte[] b, int i) => ((long)b[i] << 24) | ((long)b[i + 1] << 16) | ((long)b[i + 2] << 8) | b[i + 3];
        private static uint BE32(BinaryReader r) { var b = r.ReadBytes(4); return (uint)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]); }
        private static int BE16(BinaryReader r) { var b = r.ReadBytes(2); return (b[0] << 8) | b[1]; }
        private static long BE64(BinaryReader r) => ((long)BE32(r) << 32) | BE32(r);
        private static void Align(Stream s, int n) { long rem = s.Position % n; if (rem != 0) s.Position += n - rem; }

        private static string CString(BinaryReader r)
        {
            var sb = new StringBuilder();
            for (int c; (c = r.ReadByte()) != 0 && sb.Length < 64;) sb.Append((char)c);
            return sb.ToString();
        }

        private static string Safe(string name)
        {
            var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
            return new string(name.Select(c => invalid.Contains(c) || c == ' ' ? '_' : c).ToArray());
        }
    }
}

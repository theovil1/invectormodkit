using System;
using System.IO;
using System.Text;

namespace InvectorModKit.Charting.Audio
{
    /// <summary>Reads title, artist and embedded cover art from an MP3's ID3v2 tag (v2.3/v2.4).</summary>
    public sealed class Id3Tags
    {
        public string Title;
        public string Artist;
        /// <summary>Raw PNG/JPEG bytes of the attached picture, if any.</summary>
        public byte[] Cover;

        public static Id3Tags Read(string path)
        {
            var tags = new Id3Tags();
            try
            {
                using (var fs = File.OpenRead(path))
                {
                    var header = new byte[10];
                    if (fs.Read(header, 0, 10) < 10 || header[0] != 'I' || header[1] != 'D' || header[2] != '3') return tags;
                    int version = header[3];
                    int size = SyncSafe(header, 6);
                    var body = new byte[Math.Min(size, (int)Math.Min(int.MaxValue, fs.Length - 10))];
                    fs.Read(body, 0, body.Length);
                    int pos = 0;
                    if ((header[5] & 0x40) != 0 && body.Length >= 4) // extended header
                        pos += version >= 4 ? SyncSafe(body, 0) : BigEndian(body, 0) + 4;
                    while (pos + 10 <= body.Length)
                    {
                        string id = Encoding.ASCII.GetString(body, pos, 4);
                        if (id[0] == '\0') break;
                        int frameSize = version >= 4 ? SyncSafe(body, pos + 4) : BigEndian(body, pos + 4);
                        int data = pos + 10;
                        if (frameSize <= 0 || data + frameSize > body.Length) break;
                        if (id == "TIT2") tags.Title = Text(body, data, frameSize);
                        else if (id == "TPE1") tags.Artist = Text(body, data, frameSize);
                        else if (id == "APIC" && tags.Cover == null) tags.Cover = Picture(body, data, frameSize);
                        pos = data + frameSize;
                    }
                }
            }
            catch (IOException) { }
            return tags;
        }

        private static int SyncSafe(byte[] b, int o) => (b[o] & 0x7f) << 21 | (b[o + 1] & 0x7f) << 14 | (b[o + 2] & 0x7f) << 7 | (b[o + 3] & 0x7f);
        private static int BigEndian(byte[] b, int o) => b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3];

        private static Encoding EncodingOf(byte e) =>
            e == 1 ? Encoding.Unicode : e == 2 ? Encoding.BigEndianUnicode : e == 3 ? Encoding.UTF8 : Encoding.GetEncoding("ISO-8859-1");

        private static string Text(byte[] b, int o, int len)
        {
            if (len < 2) return null;
            var enc = EncodingOf(b[o]);
            string s = enc.GetString(b, o + 1, len - 1);
            return s.Trim('\0', '﻿', ' ').Split('\0')[0].Trim();
        }

        private static byte[] Picture(byte[] b, int o, int len)
        {
            int end = o + len;
            byte encoding = b[o++];
            while (o < end && b[o] != 0) o++; // MIME type
            o++;
            o++; // picture type
            // Description, terminated by 0 (or 00 for UTF-16).
            if (encoding == 1 || encoding == 2) { while (o + 1 < end && !(b[o] == 0 && b[o + 1] == 0)) o += 2; o += 2; }
            else { while (o < end && b[o] != 0) o++; o++; }
            if (o >= end) return null;
            var pic = new byte[end - o];
            Array.Copy(b, o, pic, 0, pic.Length);
            return pic;
        }
    }
}

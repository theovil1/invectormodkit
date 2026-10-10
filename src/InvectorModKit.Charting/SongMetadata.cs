using System;
using System.Text.RegularExpressions;

namespace InvectorModKit.Charting
{
    /// <summary>Tidies artist/title taken from downloaded files and video titles.</summary>
    public static class SongMetadata
    {
        private const string Junk = @"official|music\s+video|lyric\s+video|lyrics?|video|audio|visuali[sz]er|4k|hd|hq|subtitled|subtitles|remaster(ed)?|explicit|clip(\s+officiel)?|mv";

        // "(Official Video)", "[4K]"...
        private static readonly Regex BracketedJunk = new Regex(@"\s*[\(\[][^\)\]]*\b(" + Junk + @")\b[^\)\]]*[\)\]]", RegexOptions.IgnoreCase);
        // "... Official Music Video 4K Subtitled" at the end, with or without a separator before it.
        private static readonly Regex TrailingJunk = new Regex(@"(\s*[-–—|:]?\s*\b(" + Junk + @")\b)+\s*$", RegexOptions.IgnoreCase);
        private static readonly string[] Separators = { " — ", " – ", " - ", " | " };

        /// <summary>
        /// "Coldplay - Viva La Vida (Official Video)" by "Coldplay" → ("Coldplay", "Viva La Vida");
        /// "GENER8ION Yung Lean — STORM II Official Music Video 4K Subtitled" by "GENER8ION"
        /// → ("GENER8ION Yung Lean", "STORM II").
        /// </summary>
        public static (string artist, string title) Normalize(string artist, string title)
        {
            string t = Clean(title ?? "");
            string a = string.IsNullOrWhiteSpace(artist) ? null : artist.Trim();
            foreach (string sep in Separators)
            {
                int i = t.IndexOf(sep, StringComparison.Ordinal);
                if (i <= 0) continue;
                string left = t.Substring(0, i).Trim(), right = t.Substring(i + sep.Length).Trim();
                if (right.Length == 0) continue;
                // "Artist - Title" inside the title: keep the longer, more complete artist name.
                if (a == null || left.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0 || a.IndexOf(left, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    a = a == null || left.Length > a.Length ? left : a;
                    t = right;
                }
                break;
            }
            t = Clean(t);
            return (a, string.IsNullOrEmpty(t) ? title : t);
        }

        /// <summary>Title with video/download junk removed (returns the original if that would leave nothing).</summary>
        public static string CleanTitle(string title, string artist) => Normalize(artist, title).title;

        private static string Clean(string s)
        {
            string r = BracketedJunk.Replace(s, "");
            r = TrailingJunk.Replace(r, "").Trim();
            return r.Length == 0 ? s.Trim() : r;
        }

        /// <summary>"Artist - Title" file names (also – and —); null artist when there is no separator.</summary>
        public static (string artist, string title) SplitFileName(string baseName)
        {
            foreach (string sep in Separators)
            {
                int i = baseName.IndexOf(sep, StringComparison.Ordinal);
                if (i > 0) return (baseName.Substring(0, i).Trim(), baseName.Substring(i + sep.Length).Trim());
            }
            return (null, baseName);
        }
    }
}

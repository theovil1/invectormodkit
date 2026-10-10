using System;
using System.Text.RegularExpressions;

namespace InvectorModKit.Charting
{
    public static class SongMetadata
    {
        private static readonly Regex VideoJunk = new Regex(
            @"\s*[\(\[][^\)\]]*\b(official|video|audio|lyrics?|clip|visuali[sz]er|hd|hq|4k|remaster(ed)?|explicit|music)\b[^\)\]]*[\)\]]",
            RegexOptions.IgnoreCase);

        /// <summary>
        /// Tidies titles taken from downloaded files: "Coldplay - Viva La Vida (Official Video)" by
        /// "Coldplay" becomes "Viva La Vida". Returns the original if cleaning would leave nothing.
        /// </summary>
        public static string CleanTitle(string title, string artist)
        {
            string t = VideoJunk.Replace(title ?? "", "").Trim();
            if (!string.IsNullOrEmpty(artist) && t.StartsWith(artist + " - ", StringComparison.OrdinalIgnoreCase))
                t = t.Substring(artist.Length + 3).Trim();
            return string.IsNullOrEmpty(t) ? title : t;
        }

        /// <summary>"Artist - Title" file names; null parts when the name has no " - ".</summary>
        public static (string artist, string title) SplitFileName(string baseName)
        {
            int dash = baseName.IndexOf(" - ", StringComparison.Ordinal);
            return dash > 0 ? (baseName.Substring(0, dash).Trim(), baseName.Substring(dash + 3).Trim()) : (null, baseName);
        }
    }
}

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace InvectorModKit.Marketplace.Client
{
    /// <summary>
    /// Whether the YouTube video on the clipboard is the song being published: only then is its link filled in for the
    /// player (a link copied for another song must never sit ready behind the PUBLISH button).
    /// </summary>
    public static class LinkGuess
    {
        /// <summary>Every word of the song's title (case and accents ignored) is in the video's title.</summary>
        public static bool IsThisSong(string songTitle, string videoTitle)
        {
            var song = Words(songTitle);
            if (song.Count == 0 || string.IsNullOrEmpty(videoTitle)) return false;
            var video = new HashSet<string>(Words(videoTitle));
            return song.All(video.Contains);
        }

        private static List<string> Words(string text)
        {
            var words = new List<string>();
            if (string.IsNullOrEmpty(text)) return words;
            var current = new StringBuilder();
            foreach (char c in text.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                if (char.IsLetterOrDigit(c)) current.Append(char.ToLowerInvariant(c));
                else if (current.Length > 0) { words.Add(current.ToString()); current.Clear(); }
            }
            if (current.Length > 0) words.Add(current.ToString());
            return words;
        }
    }
}

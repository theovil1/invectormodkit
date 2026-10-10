using System.Text.RegularExpressions;

namespace InvectorModKit.Marketplace
{
    /// <summary>The same link shapes the server accepts (watch, youtu.be, shorts, embed, live, music).</summary>
    internal static class YoutubeLink
    {
        private static readonly Regex Pattern = new Regex(
            @"^(?:https?://)?(?:(?:www|m|music)\.)?(?:youtube\.com/(?:watch\?(?:[^#\s]*&)?v=|shorts/|embed/|live/)|youtu\.be/)([A-Za-z0-9_-]{11})(?![A-Za-z0-9_-])",
            RegexOptions.IgnoreCase);

        /// <summary>The canonical watch link for <paramref name="text"/>, or null if it is not a YouTube video link.</summary>
        public static string Normalize(string text)
        {
            var m = Pattern.Match((text ?? "").Trim());
            return m.Success ? "https://www.youtube.com/watch?v=" + m.Groups[1].Value : null;
        }
    }
}

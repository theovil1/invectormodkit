using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace InvectorModKit.Marketplace.Client
{
    /// <summary>An excerpt file in the cache, and when it was last played.</summary>
    public sealed class CachedFile
    {
        public string Path;
        public long Bytes;
        public DateTime LastUsed;
    }

    /// <summary>
    /// Excerpts fetched from YouTube are kept (BepInEx/cache/InvectorModKit/previews) so a chart heard again plays at
    /// once: for <see cref="MaxAge"/> after their last use, and <see cref="MaxBytes"/> at most, the oldest going first.
    /// </summary>
    public static class ExcerptCache
    {
        public static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);
        public const long MaxBytes = 200L * 1024 * 1024;

        /// <summary>One file per video and start: "&lt;youtube id&gt;-&lt;start, 0.1 s&gt;.ogg".</summary>
        public static string FileName(string youtubeId, double start) =>
            youtubeId + "-" + Math.Round(start, 1).ToString("0.0", CultureInfo.InvariantCulture) + ".ogg";

        /// <summary>The files to delete: too old, then the oldest until the rest fits in <see cref="MaxBytes"/>.</summary>
        public static List<string> ToDelete(IEnumerable<CachedFile> files, DateTime now)
        {
            var doomed = new List<string>();
            var kept = new List<CachedFile>();
            foreach (var f in files)
                if (now - f.LastUsed > MaxAge) doomed.Add(f.Path);
                else kept.Add(f);
            long total = kept.Sum(f => f.Bytes);
            foreach (var f in kept.OrderBy(f => f.LastUsed))
            {
                if (total <= MaxBytes) break;
                doomed.Add(f.Path);
                total -= f.Bytes;
            }
            return doomed;
        }
    }
}

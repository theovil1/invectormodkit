using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

namespace InvectorModKit.Marketplace.Ui
{
    /// <summary>A YouTube video's title and channel, from YouTube's public oEmbed endpoint (no media involved).</summary>
    internal static class VideoInfo
    {
        private static readonly Dictionary<string, (string Title, string Channel)> Cache = new Dictionary<string, (string, string)>();

        /// <summary>Calls <paramref name="use"/> on the main thread once known (never if the video is unavailable).</summary>
        public static void Get(string youtubeId, Action<string, string> use)
        {
            if (string.IsNullOrEmpty(youtubeId)) return;
            if (Cache.TryGetValue(youtubeId, out var known)) { use(known.Title, known.Channel); return; }
            string url = "https://www.youtube.com/oembed?format=json&url=" + Uri.EscapeDataString("https://www.youtube.com/watch?v=" + youtubeId);
            var request = UnityWebRequest.Get(url);
            request.SendWebRequest().completed += _ =>
            {
                try
                {
                    if (request.result != UnityWebRequest.Result.Success) return;
                    var o = JObject.Parse(request.downloadHandler.text);
                    var info = ((string)o["title"] ?? "", (string)o["author_name"] ?? "");
                    Cache[youtubeId] = info;
                    use(info.Item1, info.Item2);
                }
                catch (Exception e) { MarketplacePlugin.Log.LogWarning("YouTube video info: " + e.Message); }
                finally { request.Dispose(); }
            };
        }
    }
}

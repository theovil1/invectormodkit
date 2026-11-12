using System;
using System.Collections.Generic;
using InvectorModKit.CustomSongs;
using UnityEngine;
using UnityEngine.Networking;

namespace InvectorModKit.Marketplace.Ui
{
    /// <summary>YouTube thumbnails as square covers (centre of the 16:9 frame, like the custom song covers).</summary>
    internal static class Thumbnails
    {
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, List<(Action<Sprite> use, Action failed)>> Waiting =
            new Dictionary<string, List<(Action<Sprite> use, Action failed)>>();

        /// <summary>
        /// The video's cover, or once there is none to load (no video, or its thumbnail failed) the cover drawn for that
        /// title, as song select draws it for a song without one. Until then, the gray square stays.
        /// </summary>
        public static void GetOrDrawn(string youtubeId, string title, Action<Sprite> use) =>
            Get(youtubeId, use, () => use(CustomSongsApi.DrawnCover(title)));

        /// <summary>Calls <paramref name="use"/> on the main thread once the cover is loaded, or <paramref name="failed"/>.</summary>
        public static void Get(string youtubeId, Action<Sprite> use, Action failed = null)
        {
            if (string.IsNullOrEmpty(youtubeId)) { failed?.Invoke(); return; }
            if (Cache.TryGetValue(youtubeId, out var sprite)) { use(sprite); return; }
            if (Waiting.TryGetValue(youtubeId, out var list)) { list.Add((use, failed)); return; }
            Waiting[youtubeId] = new List<(Action<Sprite> use, Action failed)> { (use, failed) };

            var request = UnityWebRequestTexture.GetTexture($"https://i.ytimg.com/vi/{youtubeId}/mqdefault.jpg");
            request.SendWebRequest().completed += _ =>
            {
                Sprite result = null;
                if (request.result == UnityWebRequest.Result.Success)
                {
                    var tex = DownloadHandlerTexture.GetContent(request);
                    tex.wrapMode = TextureWrapMode.Clamp;
                    int size = Mathf.Min(tex.width, tex.height);
                    result = Sprite.Create(tex, new Rect((tex.width - size) / 2, (tex.height - size) / 2, size, size), new Vector2(0.5f, 0.5f));
                    Cache[youtubeId] = result;
                }
                request.Dispose();
                var callbacks = Waiting[youtubeId];
                Waiting.Remove(youtubeId);
                foreach (var (use, failed) in callbacks)
                {
                    try
                    {
                        if (result != null) use(result);
                        else failed?.Invoke();
                    }
                    catch (Exception e) { MarketplacePlugin.Log.LogError(e); }
                }
            };
        }

        /// <summary>Saves the video thumbnail's raw jpg next to an audio file (Song.jpg), so song select has a cover.</summary>
        public static void Save(string youtubeId, string path)
        {
            if (string.IsNullOrEmpty(youtubeId)) return;
            var request = UnityWebRequest.Get($"https://i.ytimg.com/vi/{youtubeId}/mqdefault.jpg");
            request.SendWebRequest().completed += _ =>
            {
                try { if (request.result == UnityWebRequest.Result.Success) System.IO.File.WriteAllBytes(path, request.downloadHandler.data); }
                catch (Exception e) { MarketplacePlugin.Log.LogError("thumbnail save: " + e.GetBaseException().Message); }
                finally { request.Dispose(); }
            };
        }
    }
}

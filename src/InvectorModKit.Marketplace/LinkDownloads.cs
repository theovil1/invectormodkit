using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using InvectorModKit.CustomSongs;
using InvectorModKit.Marketplace.Client;
using InvectorModKit.Marketplace.Ui;
using UnityEngine;

namespace InvectorModKit.Marketplace
{
    /// <summary>A song being added from a YouTube link.</summary>
    internal sealed class LinkDownload
    {
        public string VideoId, Link;
        /// <summary>The video's title once known (else the link).</summary>
        public string Title;
        /// <summary>0..1 of the download, written by yt-dlp's output thread.</summary>
        public volatile float Progress;
        /// <summary>The audio is in CustomSongs; the game prepares its chart.</summary>
        public bool Preparing;
        /// <summary>The Loc key of why it failed (and its detail), or null.</summary>
        public string ErrorKey, ErrorDetail;
        public string AudioPath;
        public float GiveUpAt;
    }

    /// <summary>
    /// Songs added from a YouTube link: fetched with yt-dlp into CustomSongs, then waited for until the game has charted
    /// them. Lives with the plugin, so a download goes on (and the song arrives) when the marketplace closes; failures
    /// stay listed until the marketplace closes.
    /// </summary>
    internal static class LinkDownloads
    {
        private static readonly List<LinkDownload> Items = new List<LinkDownload>();
        private static float _nextPoll;

        public static IReadOnlyList<LinkDownload> All => Items;

        /// <summary>A download started, failed or finished (main thread).</summary>
        public static event Action Changed;

        public static void Start(string youtubeLink)
        {
            string link = YoutubeLink.Normalize(youtubeLink);
            if (link == null) return;
            string id = link.Substring(link.IndexOf("v=", StringComparison.Ordinal) + 2);
            Items.RemoveAll(d => d.VideoId == id && d.ErrorKey != null);
            if (Items.Any(d => d.VideoId == id)) return;
            var download = new LinkDownload { VideoId = id, Link = link, Title = link };
            Items.Insert(0, download);
            Changed?.Invoke();
            Run(download);
        }

        /// <summary>The marketplace closed: failures are forgotten.</summary>
        public static void ForgetFailed()
        {
            if (Items.RemoveAll(d => d.ErrorKey != null) > 0) Changed?.Invoke();
        }

        private static async void Run(LinkDownload d)
        {
            try
            {
                var (ytDlp, ffmpegDir) = AudioFetch.Resolve(MarketplacePlugin.SharedDir);
                if (ytDlp == null) { Fail(d, "no_tools", null); return; }
                var (channel, title) = await Task.Run(() => AudioFetch.VideoName(ytDlp, d.VideoId, line => MarketplacePlugin.Log.LogInfo(line)));
                if (string.IsNullOrEmpty(title)) { Fail(d, "link_failed", "YouTube refused"); return; }
                d.Title = title;
                Changed?.Invoke();
                string artist = channel.Length > 0 ? channel : "YouTube";
                if (AudioFetch.Existing(CustomSongsApi.SongsFolder, channel, title) != null) { Fail(d, "link_exists", null); return; }
                string target = AudioFetch.Target(CustomSongsApi.SongsFolder, artist, title);
                string tmpBase = Path.Combine(Path.GetTempPath(), "invectormodkit-link-" + d.VideoId);
                await Task.Run(() => AudioFetch.Download(ytDlp, AudioFetch.BuildArgs(d.VideoId, tmpBase, ffmpegDir), MarketplacePlugin.Log.LogInfo,
                    line => { if (AudioFetch.ProgressOf(line) is double p) d.Progress = (float)p; }));
                string produced = tmpBase + ".ogg";
                if (!File.Exists(produced)) throw new FileNotFoundException("yt-dlp left nothing at " + produced);
                AudioFetch.Move(produced, target);
                MarketplacePlugin.Log.LogInfo("Song added: " + target);
                Thumbnails.Save(d.VideoId, Path.ChangeExtension(target, ".jpg"));
                CustomSongsApi.RequestMetadataReload();
                d.Progress = 1;
                d.AudioPath = target;
                d.Preparing = true;
                d.GiveUpAt = Time.unscaledTime + 180f; // the song only counts once the game has charted it
                Changed?.Invoke();
            }
            catch (Exception e)
            {
                MarketplacePlugin.Log.LogError("Link add failed: " + e);
                Fail(d, e.GetBaseException() is AudioFetchException f && f.Reason != null ? f.Reason : "link_failed", e.GetBaseException().Message);
            }
        }

        private static void Fail(LinkDownload d, string key, string detail)
        {
            d.ErrorKey = key;
            d.ErrorDetail = detail;
            d.Preparing = false;
            Changed?.Invoke();
        }

        /// <summary>Every frame (the plugin's Update): a prepared song leaves the list once the game lists it, ready.</summary>
        public static void Tick()
        {
            if (Time.unscaledTime < _nextPoll || !Items.Any(d => d.Preparing)) return;
            _nextPoll = Time.unscaledTime + 0.5f;
            var ready = CustomSongsApi.Songs().Where(s => s.Ready && s.AudioPath != null)
                .Select(s => Path.GetFullPath(s.AudioPath)).ToList();
            bool changed = false;
            foreach (var d in Items.Where(d => d.Preparing).ToList())
            {
                bool done = ready.Any(p => string.Equals(p, Path.GetFullPath(d.AudioPath), StringComparison.OrdinalIgnoreCase));
                if (done || Time.unscaledTime >= d.GiveUpAt) { Items.Remove(d); changed = true; }
            }
            if (changed) Changed?.Invoke();
        }
    }
}

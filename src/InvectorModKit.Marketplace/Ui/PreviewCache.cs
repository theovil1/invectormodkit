using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BepInEx;
using InvectorModKit.CustomSongs;
using InvectorModKit.Marketplace.Client;
using UnityEngine;
using UnityEngine.Networking;

namespace InvectorModKit.Marketplace.Ui
{
    /// <summary>
    /// A chart's excerpt as an audio clip: a song already on this PC plays the excerpt song select uses (instant); any
    /// other chart gets about 20 s of its video, fetched with yt-dlp (that section only) into
    /// BepInEx/cache/InvectorModKit/previews and kept there a while (<see cref="ExcerptCache"/>). One fetch at a time:
    /// the latest request wins, earlier answers are kept for later but not played.
    /// </summary>
    internal sealed class PreviewCache
    {
        private readonly MonoBehaviour _host;
        private readonly string _dir = Path.Combine(Paths.CachePath, "InvectorModKit", "previews");
        /// <summary>Videos being fetched, by any screen opened since the game started (a closed screen's fetch goes on).</summary>
        private static readonly HashSet<string> Running = new HashSet<string>();
        private string _wanted;
        private bool _fetching;
        private Request _next;
        private AudioClip _clip;

        private sealed class Request
        {
            public ChartSummary Chart;
            public Action<AudioClip, double, double> Ready;
            public Action Failed;
        }

        public PreviewCache(MonoBehaviour host)
        {
            _host = host;
            Evict();
        }

        /// <summary>The excerpt of <paramref name="c"/>: <paramref name="ready"/> gets the clip and where to play it in the clip.</summary>
        public void Get(ChartSummary c, Action<AudioClip, double, double> ready, Action failed)
        {
            _wanted = c.Id;
            var local = CustomSongsApi.Songs().FirstOrDefault(s => s.MarketplaceId == c.Id && s.PreviewPath != null);
            if (local != null)
            {
                _host.StartCoroutine(Load(local.PreviewPath, AudioType.WAV, c.Id, clip => ready(clip, 0, Math.Min(ExcerptWindow.Length, clip.length)), failed));
                return;
            }
            var window = ExcerptWindow.For(c.Preview, c.Duration);
            string file = Path.Combine(_dir, ExcerptCache.FileName(c.YoutubeId, window.Start));
            if (File.Exists(file))
            {
                Touch(file);
                _host.StartCoroutine(Load(file, AudioType.OGGVORBIS, c.Id, clip => ready(clip, 0, clip.length), failed));
                return;
            }
            if (_fetching)
            {
                _next = new Request { Chart = c, Ready = ready, Failed = failed };
                return;
            }
            Fetch(c, window, file, ready, failed);
        }

        /// <summary>The excerpt the Custom Songs plugin prepared for a song on this PC (instant).</summary>
        public void GetLocal(string songId, string previewPath, Action<AudioClip, double, double> ready, Action failed)
        {
            _wanted = songId;
            if (string.IsNullOrEmpty(previewPath) || !File.Exists(previewPath)) { failed(); return; }
            _host.StartCoroutine(Load(previewPath, AudioType.WAV, songId, clip => ready(clip, 0, Math.Min(ExcerptWindow.Length, clip.length)), failed));
        }

        /// <summary>No excerpt wanted any more (the player moved on or closed the screen).</summary>
        public void Forget() => _wanted = null;

        public void Dispose()
        {
            _wanted = null;
            _next = null;
            if (_clip) UnityEngine.Object.Destroy(_clip);
        }

        private async void Fetch(ChartSummary c, ExcerptWindow window, string file, Action<AudioClip, double, double> ready, Action failed)
        {
            _fetching = true;
            Running.Add(c.YoutubeId);
            try
            {
                var (ytDlp, ffmpegDir) = AudioFetch.Resolve(MarketplacePlugin.SharedDir);
                if (ytDlp == null)
                {
                    if (_wanted == c.Id) failed();
                    return;
                }
                Directory.CreateDirectory(_dir);
                string tmpBase = Path.Combine(_dir, "fetch-" + c.YoutubeId + "-" + Guid.NewGuid().ToString("N").Substring(0, 8)); // its own, if another screen fetches it too
                string args = AudioFetch.BuildSectionArgs(c.YoutubeId, tmpBase, ffmpegDir, window.Start, window.End);
                await Task.Run(() => AudioFetch.Download(ytDlp, args));
                string produced = tmpBase + ".ogg";
                if (!File.Exists(produced)) throw new FileNotFoundException("yt-dlp left nothing at " + produced);
                if (File.Exists(file)) File.Delete(file);
                File.Move(produced, file);
                if (_host && _wanted == c.Id) _host.StartCoroutine(Load(file, AudioType.OGGVORBIS, c.Id, clip => ready(clip, 0, clip.length), failed));
            }
            catch (Exception e)
            {
                MarketplacePlugin.Log.LogWarning($"Excerpt of {c.Title}: {e.GetBaseException().Message}");
                if (_host && _wanted == c.Id) failed();
            }
            finally
            {
                _fetching = false;
                Running.Remove(c.YoutubeId);
                var next = _next;
                _next = null;
                if (next != null && _host && next.Chart.Id == _wanted) Get(next.Chart, next.Ready, next.Failed);
            }
        }

        private IEnumerator Load(string path, AudioType type, string id, Action<AudioClip> ready, Action failed)
        {
            using (var request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, type))
            {
                yield return request.SendWebRequest();
                if (_wanted != id) yield break;
                AudioClip clip = null;
                if (request.result == UnityWebRequest.Result.Success)
                {
                    try { clip = DownloadHandlerAudioClip.GetContent(request); }
                    catch (Exception) { }
                }
                if (clip == null || clip.length <= 0)
                {
                    failed();
                    yield break;
                }
                if (_clip && _clip != clip) UnityEngine.Object.Destroy(_clip);
                _clip = clip;
                ready(clip);
            }
        }

        private static void Touch(string file)
        {
            try { File.SetLastWriteTimeUtc(file, DateTime.UtcNow); } catch (Exception) { }
        }

        /// <summary>Old excerpts and the leftovers of interrupted fetches go.</summary>
        private void Evict()
        {
            try
            {
                if (!Directory.Exists(_dir)) return;
                foreach (var partial in Directory.GetFiles(_dir, "fetch-*"))
                    if (ExcerptCache.IsLeftover(Path.GetFileName(partial), Running)) File.Delete(partial);
                var files = new DirectoryInfo(_dir).GetFiles("*.ogg")
                    .Select(f => new CachedFile { Path = f.FullName, Bytes = f.Length, LastUsed = f.LastWriteTimeUtc });
                foreach (var path in ExcerptCache.ToDelete(files, DateTime.UtcNow)) File.Delete(path);
            }
            catch (Exception e) { MarketplacePlugin.Log.LogWarning("Excerpt cache: " + e.Message); }
        }
    }
}

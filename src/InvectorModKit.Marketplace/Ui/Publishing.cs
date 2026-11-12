using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using InvectorModKit.CustomSongs;
using InvectorModKit.Marketplace.Client;

namespace InvectorModKit.Marketplace.Ui
{
    /// <summary>Publishing one's own chart, pinning it to its marketplace chart, opening a song in the level editor (My library, My publications).</summary>
    internal static class Publishing
    {
        /// <summary>
        /// Signs in if needed, fingerprints the song and sends its chart with the video link (the server makes a new
        /// version when this video was published before). A first publication asks to accept the rules once. The link
        /// and the chart's content hash are remembered, and the local chart is pinned to the published one.
        /// </summary>
        /// <param name="step">The Loc key of the step under way (fingerprinting, sending).</param>
        /// <param name="failed">Why it failed, in the player's language; null when it simply did not happen (not signed in, rules to accept).</param>
        public static async void Publish(MarketplaceScreen screen, LocalSong s, string link, Action<string> step, Action<ChartSummary> done, Action<string> failed, bool acceptRules = false)
        {
            if (!await screen.EnsureSignedIn()) { failed(null); return; }
            try
            {
                step("fingerprinting");
                string fingerprint = await Task.Run(() => CustomSongsApi.FingerprintJson(s.Id));
                string chart = File.ReadAllText(s.ChartPath);
                step("sending");
                var published = await MarketplacePlugin.Api.PublishAsync(new PublishRequest
                {
                    Title = s.Title, Artist = s.Artist, YoutubeUrl = link, Duration = s.Duration, ChartJson = chart, FingerprintJson = fingerprint,
                    AcceptRules = acceptRules,
                });
                MarketplacePlugin.Session.RememberPublished(s.Id, published.Id, link, ChartContent.Hash(chart));
                try { Pin(s, published.Id, published.Version, published.Author); }
                catch (Exception e) { MarketplacePlugin.Log.LogWarning($"Published, but the local chart could not be pinned: {e.Message}"); }
                done(published);
            }
            catch (MarketplaceException e) when (e.Status == 428 && !acceptRules)
            {
                failed(null);
                screen.Confirm(Loc.T("rules_confirm"), () => Publish(screen, s, link, step, done, failed, acceptRules: true));
            }
            catch (Exception e)
            {
                failed(Loc.T("publish_failed", e.GetBaseException().Message));
            }
        }

        /// <summary>The local chart becomes (or stops being, with a null id) the marketplace chart, with its leaderboard.</summary>
        public static void Pin(LocalSong s, string chartId, int version, string author)
        {
            CustomSongsApi.SetMarketplaceSource(s.Id, chartId, version, author);
            s.MarketplaceId = chartId;
            s.MarketplaceVersion = chartId == null ? 0 : version;
            s.MarketplaceAuthor = author;
            Scores.MarketplaceLeaderboard.Instance.Forget();
        }

        /// <summary>Charted by the player: generated, hand-made, or their own chart reinstalled from the marketplace.</summary>
        public static bool IsOwn(LocalSong s)
        {
            if (s.MarketplaceId == null) return true;
            var me = MarketplacePlugin.Session.Current;
            return MarketplacePlugin.Session.IsPublishedByMe(s.MarketplaceId) || (me != null && s.MarketplaceAuthor == me.Name);
        }

        /// <summary>Where the mod's installer puts the level editor (EditorInstaller.DefaultDir).</summary>
        private static string EditorExe => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "InvectorLevelEditor", "InvectorLevelEditor.exe");

        /// <summary>
        /// Opens the song in the level editor (the editor alone with none). An editor already open takes the song and
        /// comes to the front, after asking about its unsaved changes; the game picks up the saved chart.
        /// </summary>
        public static void OpenInEditor(LocalSong s, Action<string> status)
        {
            string exe = EditorExe;
            if (!File.Exists(exe)) { status(Loc.T("editor_missing")); return; }
            try
            {
                string song = s == null || string.IsNullOrEmpty(s.AudioPath) ? "" : "\"" + s.AudioPath + "\"";
                Process.Start(new ProcessStartInfo(exe, song) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) });
                status(Loc.T("editor_opening"));
            }
            catch (Exception e)
            {
                status(Loc.T("editor_failed", e.GetBaseException().Message));
            }
        }
    }
}

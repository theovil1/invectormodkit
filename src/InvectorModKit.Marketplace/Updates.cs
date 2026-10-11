using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using BepInEx;
using InvectorModKit.Marketplace.Client;
using UnityEngine;
using UnityEngine.Networking;

namespace InvectorModKit.Marketplace
{
    /// <summary>
    /// Mod updates, offered in the main menu: at start the latest release on GitHub is read; when it is newer, the
    /// menu shows it. Accepting downloads its installer, which is started in update mode: it waits for the game to
    /// close, then installs, so the next launch runs the new version.
    /// </summary>
    internal static class Updates
    {
        public enum Step { None, Downloading, Ready, Failed }

        public static Release Available { get; private set; }
        public static Step State { get; private set; }

        public static async void Check()
        {
            try
            {
                var answer = await new UnityWebRequestTransport().SendAsync("GET", Releases.LatestUrl, null, null);
                var release = answer.Status == 200 ? Releases.Parse(answer.Body) : null;
                if (release != null && Releases.IsNewer(release.Version, ModKit.Version))
                {
                    Available = release;
                    MarketplacePlugin.Log.LogInfo($"Update available: {ModKit.Version} -> {release.Version}");
                }
                else MarketplacePlugin.Log.LogInfo($"Update check: {ModKit.Version} is the latest" + (release == null ? $" (HTTP {answer.Status})" : ""));
            }
            catch (Exception e) { MarketplacePlugin.Log.LogInfo("Update check skipped: " + e.GetBaseException().Message); }
        }

        /// <summary>Downloads the installer and starts it in update mode; <paramref name="report"/> gets the outcome.</summary>
        public static IEnumerator Download(Action<string> report)
        {
            var release = Available;
            if (release == null || State == Step.Downloading || State == Step.Ready) yield break;
            State = Step.Downloading;
            report(Loc.T("update_downloading"));
            string dir = Path.Combine(Path.GetTempPath(), "InvectorModKit");
            string file = Path.Combine(dir, $"InvectorModKitSetup-{release.Version}.exe");
            Directory.CreateDirectory(dir);
            using (var request = UnityWebRequest.Get(release.SetupUrl))
            {
                request.downloadHandler = new DownloadHandlerFile(file) { removeFileOnAbort = true };
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Fail(report, request.error);
                    yield break;
                }
            }
            try
            {
                int pid = Process.GetCurrentProcess().Id;
                Process.Start(new ProcessStartInfo(file, $"--update --game \"{Paths.GameRootPath}\" --wait {pid}") { UseShellExecute = false, CreateNoWindow = true });
                State = Step.Ready;
                MarketplacePlugin.Log.LogInfo($"Update {release.Version} downloaded; it installs when the game closes");
                report(Loc.T("update_ready"));
            }
            catch (Exception e) { Fail(report, e.Message); }
        }

        private static void Fail(Action<string> report, string why)
        {
            State = Step.Failed;
            MarketplacePlugin.Log.LogWarning("Update download failed: " + why);
            report(Loc.T("update_failed", why));
        }
    }
}

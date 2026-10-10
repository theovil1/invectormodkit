using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace InvectorModKit.CustomSongs
{
    [BepInPlugin(Guid, "Custom Songs", "0.1.0")]
    [BepInDependency(ModKit.Guid)]
    [ModKitMod(AffectsGameplay = true)]
    public sealed class CustomSongsPlugin : BaseUnityPlugin
    {
        public const string Guid = "com.invectormodkit.customsongs";

        internal static ManualLogSource Log;
        internal static SongLibrary Library;
        private static readonly ConcurrentQueue<string> _workerLog = new ConcurrentQueue<string>();

        private ConfigEntry<string> _folder;

        private void Awake()
        {
            Log = Logger;
            _folder = Config.Bind("General", "SongsFolder", "", "Folder with your songs. Empty = 'CustomSongs' next to the game executable.");
            string root = string.IsNullOrWhiteSpace(_folder.Value) ? Path.Combine(Paths.GameRootPath, "CustomSongs") : _folder.Value;
            Directory.CreateDirectory(root);
            WriteReadme(root);

            PreloadNative(Path.Combine(Paths.PluginPath, "InvectorModKit", "onnxruntime.dll"));
            Library = new SongLibrary(root)
            {
                VocalModelPath = Path.Combine(Path.GetDirectoryName(Info.Location), "vocals.fp16.onnx"),
                GeneratorPath = Path.Combine(Paths.PluginPath, "InvectorModKit", "chartgen", "InvectorModKit.ChartGen.exe"),
            };
            Library.Scan();
            Logger.LogInfo($"{Library.Songs.Count} custom song(s) in {root}");

            new Harmony(Guid).PatchAll(typeof(Patches));
            StartCoroutine(GameAssets.LoadTemplates(Logger));
            StartCoroutine(PrepareSongs());
        }

        /// <summary>How often the songs folder is checked for added or deleted songs (and whenever the game gets focus back).</summary>
        private const float FolderCheckSeconds = 2f;
        private int _order;
        private bool _checkFolderNow;
        private static volatile bool _matchRequested;

        internal static void RequestMarketplaceMatch() => _matchRequested = true;

        private void OnApplicationFocus(bool focused)
        {
            if (focused) _checkFolderNow = true; // typically back from dropping a file in Explorer
        }

        /// <summary>
        /// 1) list every song right away (metadata and length only), 2) prepare charts and previews one by
        /// one in the background, the song the player is waiting for first, 3) keep watching the folder so
        /// songs can be added or removed without restarting the game.
        /// </summary>
        private IEnumerator PrepareSongs()
        {
            yield return List(Library.Songs.ToList());
            float nextCheck = Time.realtimeSinceStartup + FolderCheckSeconds;
            while (true)
            {
                // Heavy analysis (and the garbage collections it causes) would make the game stutter
                // mid-song, so nothing new starts while a song is being played.
                while (InGame) yield return new WaitForSecondsRealtime(0.5f);
                if (_checkFolderNow || Time.realtimeSinceStartup >= nextCheck)
                {
                    _checkFolderNow = false;
                    yield return CheckFolder();
                    nextCheck = Time.realtimeSinceStartup + FolderCheckSeconds;
                }
                if (_matchRequested)
                {
                    _matchRequested = false;
                    yield return MatchMarketplace();
                }
                var next = Library.Songs.Where(s => s.State == SongState.Pending).OrderByDescending(s => s.Priority).FirstOrDefault();
                if (next == null) { yield return new WaitForSecondsRealtime(0.25f); continue; }
                var task = RunLowPriority(() => Library.Prepare(next, msg => _workerLog.Enqueue(msg)));
                while (!task.IsCompleted) { FlushWorkerLog(); yield return null; }
                FlushWorkerLog();
                if (next.State != SongState.Ready) continue;
                try
                {
                    GameAssets.ApplyChart(next);
                    Logger.LogInfo($"Ready: {next} ({next.Chart.Bpm:0.##} BPM, lead-in {next.LeadIn:0.00}s, gain {next.Chart.Gain:+0.0;-0.0} dB{(next.MarketplaceId != null ? ", marketplace chart" : "")})");
                    if (next.Reloading)
                    {
                        next.Reloading = false;
                        if (next.PreviewItem != null && SongAudio.HasClip(next.PreviewItem)) SongAudio.Release(next.PreviewItem); // the excerpt may have moved
                        RefreshSongSelect();
                    }
                    else if (next.MarketplaceId != null) RefreshSongSelect(); // its name comes from the marketplace record
                }
                catch (System.Exception e) { Fail(next, "could not apply chart", e); }
            }
        }

        /// <summary>
        /// Songs already charted that match a marketplace chart waiting for its audio get that chart (ChartGen
        /// writes it), then are prepared again by the main loop.
        /// </summary>
        private IEnumerator MatchMarketplace()
        {
            var candidates = Library.Songs.Where(s => s.State == SongState.Ready && s.MarketplaceId == null).ToList();
            var matched = new List<CustomSong>();
            var task = RunLowPriority(() =>
            {
                foreach (var song in candidates)
                    if (Library.TryMatchMarketplace(song, msg => _workerLog.Enqueue(msg))) lock (matched) matched.Add(song);
            });
            while (!task.IsCompleted) { FlushWorkerLog(); yield return null; }
            FlushWorkerLog();
            if (task.IsFaulted) Logger.LogWarning("Marketplace check failed: " + task.Exception.GetBaseException().Message);
            foreach (var song in matched)
            {
                song.Chart = null;
                song.HasMetadata = false;
                song.State = SongState.Pending;
            }
        }

        /// <summary>Reads the songs' metadata and builds their (still chartless) levels, so song select can list them.</summary>
        private IEnumerator List(List<CustomSong> songs)
        {
            var info = RunLowPriority(() => { foreach (var s in songs) Library.ReadInfo(s, msg => _workerLog.Enqueue(msg)); });
            while (!info.IsCompleted) { FlushWorkerLog(); yield return null; }
            FlushWorkerLog();

            while (!GameAssets.Ready) yield return null;
            foreach (var song in songs.Where(s => s.State != SongState.Failed))
            {
                try { GameAssets.BuildShell(song, _order++); }
                catch (System.Exception e) { Fail(song, "could not build level", e); }
            }
        }

        private IEnumerator CheckFolder()
        {
            var known = Library.Songs.ToList();
            (List<CustomSong> Added, List<CustomSong> Removed) changes = default;
            List<CustomSong> edited = null;
            var scan = RunLowPriority(() =>
            {
                changes = Library.FindChanges(known);
                edited = Library.ChangedCharts(known);
            });
            while (!scan.IsCompleted) yield return null;
            if (scan.IsFaulted)
            {
                Logger.LogWarning("Could not check the songs folder: " + scan.Exception.GetBaseException().Message);
                yield break;
            }
            foreach (var song in changes.Removed)
            {
                Library.Songs.Remove(song);
                Logger.LogInfo($"Removed: {song} (its file is gone)");
            }
            if (changes.Added.Count > 0)
            {
                yield return List(changes.Added);
                Library.Songs.AddRange(changes.Added);
                foreach (var song in changes.Added.Where(s => s.State != SongState.Failed))
                    Logger.LogInfo($"New song: {song} ({(File.Exists(song.ChartPath) ? "has a chart" : "chart being generated")})");
            }
            if (changes.Added.Count > 0 || changes.Removed.Count > 0) RefreshSongSelect();
            // Charts edited outside the game (chart editor, text editor): prepared again first, without a restart.
            foreach (var song in edited.Where(s => !changes.Removed.Contains(s)))
            {
                Logger.LogInfo($"Chart changed: {song}, reloading it");
                song.Chart = null;
                song.HasMetadata = false;
                song.Reloading = true;
                song.Priority = long.MaxValue;
                song.State = SongState.Pending;
            }
        }

        /// <summary>
        /// Song select reads the song list when it opens: reopen it the way the game does when a DLC gets
        /// installed. Not in multiplayer, where reopening would drop the joined players; the change shows
        /// there the next time it opens.
        /// </summary>
        private static void RefreshSongSelect()
        {
            if (!(ModKit.CurrentState is HelloThere.Avicii.StateSelectSongSingleplayer select)) return;
            // Stay on the song the player is looking at (the game would go back to the first one).
            int index = select.buttonsWithEvents == null ? -1 : System.Array.IndexOf(select.buttonsWithEvents, select.selectedLevelButton);
            if (index >= 0)
            {
                HelloThere.Avicii.StateSelectSongSingleplayer.PLAYER_PREFS_KEY_SELECTED_BUTTON_INDEX = index;
                select.selectPreviousSong = true;
            }
            select.OnDLCChanged();
        }

        private static bool InGame
        {
            get
            {
                var state = ModKit.CurrentState;
                return state is HelloThere.Avicii.StateGame || state is HelloThere.Avicii.StateGamePause || state is global::StateGameEnd;
            }
        }

        /// <summary>A background thread below normal priority, so the game's own threads always come first.</summary>
        private static Task RunLowPriority(System.Action work)
        {
            var done = new TaskCompletionSource<bool>();
            var thread = new System.Threading.Thread(() =>
            {
                try { work(); done.SetResult(true); }
                catch (System.Exception e) { done.SetException(e); }
            }) { IsBackground = true, Priority = System.Threading.ThreadPriority.BelowNormal, Name = "CustomSongs worker" };
            thread.Start();
            return done.Task;
        }

        [System.Runtime.InteropServices.DllImport("kernel32", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern System.IntPtr LoadLibrary(string path);

        /// <summary>Native DLLs next to a plugin are not on Windows' search path; loading it first lets P/Invoke find it.</summary>
        private void PreloadNative(string path)
        {
            if (!File.Exists(path)) { Logger.LogWarning("Missing " + path + " (vocal separation disabled)"); return; }
            if (LoadLibrary(path) == System.IntPtr.Zero) Logger.LogWarning($"Could not load {path} (error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()})");
        }

        private void Fail(CustomSong song, string what, System.Exception e)
        {
            song.State = SongState.Failed;
            song.Error = e.Message;
            Logger.LogError($"{song}: {what}: {e}");
        }

        /// <summary>Waits until a song is prepared, moving it to the front of the queue.</summary>
        internal static IEnumerator WaitUntilPrepared(CustomSong song)
        {
            song.Priority = System.DateTime.UtcNow.Ticks;
            while (song.State == SongState.Pending || song.State == SongState.Processing) yield return null;
        }

        private void FlushWorkerLog()
        {
            while (_workerLog.TryDequeue(out var msg)) Logger.LogInfo(msg);
        }

        private static void WriteReadme(string root)
        {
            const string header = "Invector ModKit - Custom Songs";
            string path = Path.Combine(root, "README.txt");
            string text =
@"Invector ModKit - Custom Songs
==============================

Drop songs here (.mp3, .ogg or .wav), even while the game is running: they appear in
song select under the 'Custom Songs' header within a few seconds (in multiplayer, the
next time song select opens). Deleting a song's file removes it the same way.

  * Loose file:   CustomSongs/Artist - Title.mp3
  * Own folder:   CustomSongs/Any Name/song.mp3 (+ optional cover.jpg / cover.png and
                  song.json: { ""title"": ""..."", ""artist"": ""..."" })

The first time a song is seen, a chart is generated automatically for all four
difficulties (about 20 seconds) and saved next to the audio as <name>.chart.json. You can
edit that file; remove its ""generator"" block so your edits are never overwritten.

Playing with custom songs installed is a 'modded' session: Steam leaderboards,
achievements and stats are switched off, and results are kept in
BepInEx/config/InvectorModKit/modded-scores.json instead of your save.
";
            // Ours is kept up to date; a README.txt of the player's own is left alone.
            if (File.Exists(path) && (!File.ReadAllText(path).StartsWith(header) || File.ReadAllText(path) == text)) return;
            File.WriteAllText(path, text);
        }
    }
}

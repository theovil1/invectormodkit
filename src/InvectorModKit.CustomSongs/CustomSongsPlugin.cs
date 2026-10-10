using System.Collections;
using System.Collections.Concurrent;
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
            };
            Library.Scan();
            Logger.LogInfo($"{Library.Songs.Count} custom song(s) in {root}");

            new Harmony(Guid).PatchAll(typeof(Patches));
            StartCoroutine(GameAssets.LoadTemplates(Logger));
            StartCoroutine(PrepareSongs());
        }

        /// <summary>
        /// 1) list every song right away (metadata and length only), 2) prepare charts and previews one by
        /// one in the background, the song the player is waiting for first.
        /// </summary>
        private IEnumerator PrepareSongs()
        {
            var songs = Library.Songs.ToList();
            var info = RunLowPriority(() => { foreach (var s in songs) Library.ReadInfo(s, msg => _workerLog.Enqueue(msg)); });
            while (!info.IsCompleted) { FlushWorkerLog(); yield return null; }
            FlushWorkerLog();

            while (!GameAssets.Ready) yield return null;
            int order = 0;
            foreach (var song in songs.Where(s => s.State != SongState.Failed))
            {
                try { GameAssets.BuildShell(song, order++); }
                catch (System.Exception e) { Fail(song, "could not build level", e); }
            }

            while (true)
            {
                // Heavy analysis (and the garbage collections it causes) would make the game stutter
                // mid-song, so nothing new starts while a song is being played.
                while (InGame) yield return new WaitForSecondsRealtime(0.5f);
                var next = songs.Where(s => s.State == SongState.Pending).OrderByDescending(s => s.Priority).FirstOrDefault();
                if (next == null) break;
                var task = RunLowPriority(() => Library.Prepare(next, msg => _workerLog.Enqueue(msg)));
                while (!task.IsCompleted) { FlushWorkerLog(); yield return null; }
                FlushWorkerLog();
                if (next.State != SongState.Ready) continue;
                try
                {
                    GameAssets.ApplyChart(next);
                    Logger.LogInfo($"Ready: {next} ({next.Chart.Bpm:0.##} BPM, lead-in {next.LeadIn:0.00}s, gain {next.Chart.Gain:+0.0;-0.0} dB)");
                }
                catch (System.Exception e) { Fail(next, "could not apply chart", e); }
            }
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
            string path = Path.Combine(root, "README.txt");
            if (File.Exists(path)) return;
            File.WriteAllText(path,
@"Invector ModKit - Custom Songs
==============================

Drop songs here (.mp3, .ogg or .wav), then start the game. They appear in song select
under the 'Custom Songs' header.

  * Loose file:   CustomSongs/Artist - Title.mp3
  * Own folder:   CustomSongs/Any Name/song.mp3 (+ optional cover.jpg / cover.png and
                  song.json: { ""title"": ""..."", ""artist"": ""..."" })

The first time a song is seen, a chart is generated automatically for all four
difficulties and saved next to the audio as <name>.chart.json. You can edit that file;
remove its ""generator"" block so your edits are never overwritten.

Playing with custom songs installed is a 'modded' session: Steam leaderboards,
achievements and stats are switched off, and results are kept in
BepInEx/config/InvectorModKit/modded-scores.json instead of your save.
");
        }
    }
}

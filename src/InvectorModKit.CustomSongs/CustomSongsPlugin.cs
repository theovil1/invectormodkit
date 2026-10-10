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

            Library = new SongLibrary(root);
            Library.Scan();
            Logger.LogInfo($"{Library.Songs.Count} custom song(s) in {root}");

            new Harmony(Guid).PatchAll(typeof(Patches));
            StartCoroutine(GameAssets.LoadTemplates(Logger));
            StartCoroutine(PrepareSongs());
        }

        private IEnumerator PrepareSongs()
        {
            int order = 0;
            foreach (var song in Library.Songs.ToList())
            {
                var task = Task.Run(() => Library.Prepare(song, msg => _workerLog.Enqueue(msg)));
                while (!task.IsCompleted) { FlushWorkerLog(); yield return null; }
                FlushWorkerLog();
                if (song.State != SongState.Ready) continue;
                while (!GameAssets.Ready) yield return null;
                try
                {
                    GameAssets.Build(song, order++);
                    Logger.LogInfo($"Ready: {song} ({song.Chart.Bpm:0.##} BPM, lead-in {song.LeadIn:0.00}s)");
                }
                catch (System.Exception e)
                {
                    song.State = SongState.Failed;
                    song.Error = e.Message;
                    Logger.LogError($"{song}: could not build level: {e}");
                }
            }
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

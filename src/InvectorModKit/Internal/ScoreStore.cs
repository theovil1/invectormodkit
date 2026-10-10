using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using HelloThere.Avicii;
using Newtonsoft.Json;

namespace InvectorModKit.Internal
{
    /// <summary>
    /// In a modded session, results are kept in BepInEx/config/InvectorModKit/modded-scores.json instead
    /// of the game's save, so official progression and Steam-synced data never contain modded runs.
    /// Bests are merged with the game's own rule (LevelScores.ApplyHighscores).
    /// </summary>
    internal static class ScoreStore
    {
        public const string FileName = "modded-scores.json";
        private static Dictionary<string, Entry> _entries;

        public sealed class Entry
        {
            public LevelScores Best;
            public int Plays;
            public DateTime LastPlayed;
        }

        private static string FilePath => Path.Combine(Paths.DataDirectory, FileName);

        /// <summary>A copy of the best result, or null if never played in a modded session.</summary>
        public static LevelScores GetBest(string levelId, Difficulty difficulty)
        {
            Load();
            if (!_entries.TryGetValue(Key(levelId, difficulty), out var e) || e.Best == null) return null;
            var copy = new LevelScores { levelId = levelId };
            copy.ApplyHighscores(e.Best);
            copy.ratingIndex = e.Best.ratingIndex;
            return copy;
        }

        public static void Record(string levelId, Difficulty difficulty, LevelScores result)
        {
            Load();
            string key = Key(levelId, difficulty);
            if (!_entries.TryGetValue(key, out var e)) _entries[key] = e = new Entry();
            e.Plays++;
            e.LastPlayed = DateTime.UtcNow;
            if (e.Best == null) e.Best = new LevelScores { levelId = levelId };
            int previousRating = e.Best.ratingIndex;
            e.Best.ApplyHighscores(result);
            e.Best.ratingIndex = Math.Max(previousRating, result.ratingIndex);
            try
            {
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(_entries, Formatting.Indented));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch (Exception ex) { ModKit.Log.LogError("Could not write " + FilePath + ": " + ex.Message); }
        }

        private static string Key(string levelId, Difficulty d) => levelId + "|" + d;

        private static void Load()
        {
            if (_entries != null) return;
            _entries = new Dictionary<string, Entry>();
            try
            {
                if (File.Exists(FilePath))
                    _entries = JsonConvert.DeserializeObject<Dictionary<string, Entry>>(File.ReadAllText(FilePath)) ?? _entries;
            }
            catch (Exception ex) { ModKit.Log.LogError("Could not read " + FilePath + " (starting a new one): " + ex.Message); }
        }

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(StateGameResult), "ProgressPlayer"),
                prefix: new HarmonyMethod(typeof(ScoreStore), nameof(ProgressPlayerPrefix)));
        }

        /// <summary>Replaces StateGameResult.ProgressPlayer in modded sessions: compute the result, store it here, leave the save alone.</summary>
        private static bool ProgressPlayerPrefix(StateGameResult __instance, PlayerSetting player, ScoreDefinition scoreDefinition, ref bool firstCompletion)
        {
            if (!Integrity.IsModded) return true;
            firstCompletion = false;
            var level = __instance.gameSession.levelDefinition;
            var difficulty = player.playerGameSession.Difficulty;
            if (__instance.scoreThisRound == null)
                __instance.scoreThisRound = player.playerGameSession.PlayerScore.CreateLevelScores(level.Modes.StrumPattern.GetStrums(difficulty), scoreDefinition,
                    level.MusicDefinition.SecondsPerBeat, level.Modes.EndTime, __instance.badges.badgeLimits);
            var s = __instance.scoreThisRound;
            Record(level.levelId, difficulty, s);
            ModKit.Log.LogInfo($"Modded result kept locally: {level.levelId} {difficulty} score {s.score} ({s.percent:P1})");
            return false;
        }
    }
}

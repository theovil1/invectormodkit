using System;
using System.Collections.Generic;
using BepInEx.Logging;
using InvectorModKit.Internal;

namespace InvectorModKit
{
    /// <summary>Entry point of the ModKit API for other mods.</summary>
    public static class ModKit
    {
        public const string Guid = "com.invectormodkit.core";
        public const string Name = "Invector ModKit";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;

        /// <summary>
        /// True once any gameplay-affecting mod is active. Then, for the rest of the session, nothing is
        /// sent to Steam (leaderboards, achievements, stats) and results go to a separate local file
        /// instead of the game's save. It never goes back to false within a session.
        /// </summary>
        public static bool IsModdedSession => Integrity.IsModded;

        /// <summary>Why the session is modded (one line per reason), empty when it is not.</summary>
        public static IReadOnlyList<string> ModdedReasons => Integrity.Reasons;

        /// <summary>Flags the session as modded at runtime (e.g. a dev tool enabling autoplay).</summary>
        public static void MarkModded(string reason) => Integrity.MarkModded(reason);

        /// <summary>Best result recorded for a level during modded sessions (a copy), or null.</summary>
        public static global::LevelScores GetModdedBest(string levelId, global::Difficulty difficulty) => ScoreStore.GetBest(levelId, difficulty);

        /// <summary>Adds or overrides a localized game string (all languages) by its key, e.g. "menu.options".</summary>
        public static void SetString(string key, string text) => StringOverrides.Set(key, text);

        /// <summary>Folder for ModKit's own data (backups, modded scores): BepInEx/config/InvectorModKit.</summary>
        public static string DataDirectory => Paths.DataDirectory;

        /// <summary>The game install folder.</summary>
        public static string GameDirectory => Paths.GameDirectory;
    }
}

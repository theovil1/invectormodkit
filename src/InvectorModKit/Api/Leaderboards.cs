using System;
using System.Collections.Generic;
using HelloThere.Services;

namespace InvectorModKit
{
    /// <summary>
    /// A leaderboard other than Steam's for some levels, shown in the game's own highscore panels (song select,
    /// results). Levels are recognised by their <c>highscoreTablePrefix</c>; the game's board id is
    /// "(prefix + 1)(difficulty + 1)". Callbacks must be invoked on the main thread.
    /// </summary>
    public interface ILeaderboardProvider
    {
        /// <summary>True for the table prefixes this provider serves (never 0: that is an official song's).</summary>
        bool Claims(int tablePrefix);

        /// <summary>True when at least one level currently has one of this provider's boards.</summary>
        bool ClaimsAny();

        /// <param name="percent">The game's rounded percentage (0-100).</param>
        void Submit(int tablePrefix, global::Difficulty difficulty, long score, int percent, Action<Highscores.SubmitResult, HighScoreManager.ServerResult> onComplete);

        void GetGlobalList(int tablePrefix, global::Difficulty difficulty, int start, int count, Action<Highscores.ListResult, HighScoreManager.ServerResult> onComplete);

        void GetFriendList(int tablePrefix, global::Difficulty difficulty, int start, int count, Action<Highscores.ListResult, HighScoreManager.ServerResult> onComplete);
    }

    /// <summary>
    /// Where leaderboard providers register. A board a provider claims never reaches Steam, even in a clean
    /// session; every other board keeps the game's behaviour (and OnlineGuard's rules in a modded session).
    /// </summary>
    public static class Leaderboards
    {
        private static readonly List<ILeaderboardProvider> Providers = new List<ILeaderboardProvider>();

        public static void Register(ILeaderboardProvider provider)
        {
            if (provider != null && !Providers.Contains(provider)) Providers.Add(provider);
        }

        public static void Unregister(ILeaderboardProvider provider) => Providers.Remove(provider);

        /// <summary>The provider serving this table prefix, or null.</summary>
        public static ILeaderboardProvider For(int tablePrefix)
        {
            if (tablePrefix <= 0) return null;
            foreach (var p in Providers)
            {
                try
                {
                    if (p.Claims(tablePrefix)) return p;
                }
                catch (Exception e) { ModKit.Log.LogError("Leaderboard provider failed: " + e); }
            }
            return null;
        }

        public static bool Claims(HelloThere.Avicii.LevelDefinition level) => level != null && For(level.highscoreTablePrefix) != null;

        /// <summary>Some level has a provider's board (song select then keeps its highscores hint).</summary>
        public static bool Any()
        {
            foreach (var p in Providers)
            {
                try
                {
                    if (p.ClaimsAny()) return true;
                }
                catch (Exception e) { ModKit.Log.LogError("Leaderboard provider failed: " + e); }
            }
            return false;
        }

        /// <summary>Splits a board id made by Highscores.GetHighscoreId; false for anything else.</summary>
        public static bool TryParseBoard(int board, out int tablePrefix, out global::Difficulty difficulty)
        {
            tablePrefix = board / 10 - 1;
            int d = board % 10 - 1;
            difficulty = (global::Difficulty)d;
            return board > 0 && d >= 0 && d <= 3 && tablePrefix >= 0;
        }
    }
}

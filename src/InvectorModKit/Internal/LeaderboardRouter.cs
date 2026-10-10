using System;
using HarmonyLib;
using HelloThere.Avicii;
using HelloThere.Services;

namespace InvectorModKit.Internal
{
    /// <summary>
    /// Takes the place of the game's highscore service (Highscores.GetService) and sends each board either to
    /// the provider that claims it or to the original service (Steam). The game's panels then work for claimed
    /// levels exactly as for official ones: song select's highscore panel, the results screen's lists and rank.
    /// </summary>
    internal sealed class LeaderboardRouter : IHighscoreService
    {
        private readonly IHighscoreService _inner;

        /// <summary>The level the game's highscore UI is about (song select's selection, or the results screen's).</summary>
        public static LevelDefinition CurrentLevel;

        private LeaderboardRouter(IHighscoreService inner) => _inner = inner;

        public bool IsEnabled() => SafeInnerEnabled() || Leaderboards.Claims(CurrentLevel);

        private bool SafeInnerEnabled()
        {
            try { return _inner != null && _inner.IsEnabled(); }
            catch (Exception) { return false; }
        }

        public void Submit(int board, long score, string comment, Action<Highscores.SubmitResult, HighScoreManager.ServerResult> onComplete)
        {
            if (Claimed(board, out var provider, out int prefix, out var difficulty))
            {
                int.TryParse(comment, out int percent);
                Guard(onComplete, new Highscores.SubmitResult { success = HighScoreManager.ServerResult.Error },
                    () => provider.Submit(prefix, difficulty, score, percent, onComplete));
            }
            else _inner.Submit(board, score, comment, onComplete);
        }

        public void GetGlobalList(int board, int start, int count, Action<Highscores.ListResult, HighScoreManager.ServerResult> onComplete)
        {
            if (Claimed(board, out var provider, out int prefix, out var difficulty))
                Guard(onComplete, EmptyList(), () => provider.GetGlobalList(prefix, difficulty, start, count, onComplete));
            else _inner.GetGlobalList(board, start, count, onComplete);
        }

        public void GetFriendList(int board, int start, int count, Action<Highscores.ListResult, HighScoreManager.ServerResult> onComplete)
        {
            if (Claimed(board, out var provider, out int prefix, out var difficulty))
                Guard(onComplete, EmptyList(), () => provider.GetFriendList(prefix, difficulty, start, count, onComplete));
            else _inner.GetFriendList(board, start, count, onComplete);
        }

        private static Highscores.ListResult EmptyList() =>
            new Highscores.ListResult { success = HighScoreManager.ServerResult.Error, entries = new System.Collections.Generic.List<Highscores.ListEntry>() };

        private static bool Claimed(int board, out ILeaderboardProvider provider, out int prefix, out global::Difficulty difficulty)
        {
            provider = Leaderboards.TryParseBoard(board, out prefix, out difficulty) ? Leaderboards.For(prefix) : null;
            return provider != null;
        }

        /// <summary>A provider that throws still answers the game, which otherwise waits forever.</summary>
        private static void Guard<T>(Action<T, HighScoreManager.ServerResult> onComplete, T failure, Action call)
        {
            try { call(); }
            catch (Exception e)
            {
                ModKit.Log.LogError("Leaderboard provider failed: " + e);
                onComplete?.Invoke(failure, HighScoreManager.ServerResult.Error);
            }
        }

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(Highscores), nameof(Highscores.GetService)),
                postfix: new HarmonyMethod(typeof(LeaderboardRouter), nameof(GetServicePostfix)));
            harmony.Patch(AccessTools.Method(typeof(HighScoreManager), nameof(HighScoreManager.SetLevel)),
                prefix: new HarmonyMethod(typeof(LeaderboardRouter), nameof(SetLevelPrefix)));
            harmony.Patch(AccessTools.Method(typeof(StateGameResult), nameof(StateGameResult.Begin)),
                prefix: new HarmonyMethod(typeof(LeaderboardRouter), nameof(ResultBeginPrefix)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(LevelDefinition), nameof(LevelDefinition.SupportsHighscores)),
                postfix: new HarmonyMethod(typeof(LeaderboardRouter), nameof(SupportsHighscoresPostfix)));
            harmony.Patch(AccessTools.Method(typeof(StateSelectSongSingleplayer), nameof(StateSelectSongSingleplayer.PopulateInputIndicators)),
                postfix: new HarmonyMethod(typeof(LeaderboardRouter), nameof(IndicatorsPostfix)));
            harmony.Patch(AccessTools.Method(typeof(StateSelectSongSingleplayer), nameof(StateSelectSongSingleplayer.OnSelect)),
                postfix: new HarmonyMethod(typeof(LeaderboardRouter), nameof(OnSelectPostfix)));
        }

        private static void GetServicePostfix(ref IHighscoreService __result)
        {
            if (__result is LeaderboardRouter) return;
            __result = Highscores.service = new LeaderboardRouter(__result);
        }

        private static void SetLevelPrefix(LevelDefinition levelDefinition) => CurrentLevel = levelDefinition;

        private static void ResultBeginPrefix(StateGameResult __instance) => CurrentLevel = __instance.gameSession?.levelDefinition;

        /// <summary>Custom songs share one song package without highscores; a claimed level has them.</summary>
        private static void SupportsHighscoresPostfix(LevelDefinition __instance, ref bool __result)
        {
            if (!__result && Leaderboards.Claims(__instance)) __result = true;
        }

        // Song select builds its key hints once, dropping "highscores" when the service is off; with claimed
        // levels that depends on the song, so the hints are rebuilt when the selection crosses that line.
        private static bool _hintsForClaimed;

        private static void IndicatorsPostfix() => _hintsForClaimed = Leaderboards.Claims(CurrentLevel);

        private static void OnSelectPostfix(StateSelectSongSingleplayer __instance, LevelDefinition levelDefinition)
        {
            bool claimed = Leaderboards.Claims(levelDefinition);
            if (claimed == _hintsForClaimed) return;
            try { __instance.PopulateInputIndicators(); }
            catch (Exception e) { ModKit.Log.LogError("Could not refresh song select hints: " + e.Message); }
        }
    }
}

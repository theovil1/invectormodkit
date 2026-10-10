using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using HelloThere.Avicii;
using HelloThere.Services;
using InvectorModKit.CustomSongs;
using InvectorModKit.Marketplace.Client;
using UnityEngine;
using GameDifficulty = global::Difficulty;

namespace InvectorModKit.Marketplace.Scores
{
    /// <summary>
    /// The marketplace's leaderboards in the game's own highscore panels. A marketplace chart (installed, or
    /// published by this player) has a table prefix of its own; a play is opened on the server when its song
    /// starts and the game's score submission closes it. Lists come from the server, the friends list from
    /// the player's Steam friends.
    /// </summary>
    internal sealed class MarketplaceLeaderboard : ILeaderboardProvider
    {
        public static readonly MarketplaceLeaderboard Instance = new MarketplaceLeaderboard();

        private List<LocalSong> _songs = new List<LocalSong>();
        private float _songsAt = -10f;

        private LocalSong SongFor(int prefix)
        {
            if (prefix <= 0) return null;
            if (Time.unscaledTime - _songsAt > 1f)
            {
                _songs = CustomSongsApi.Songs();
                _songsAt = Time.unscaledTime;
            }
            return _songs.FirstOrDefault(s => s.BoardPrefix == prefix && s.MarketplaceId != null && s.MarketplaceVersion > 0);
        }

        /// <summary>Song-select changes (new marketplace chart, publication) are seen right away.</summary>
        public void Forget() => _songsAt = -10f;

        public bool Claims(int tablePrefix) => SongFor(tablePrefix) != null;

        public bool ClaimsAny()
        {
            SongFor(int.MaxValue);
            return _songs.Any(s => s.BoardPrefix > 0 && s.MarketplaceId != null && s.MarketplaceVersion > 0);
        }

        /// <summary>The server's (and charts') order: Casual, Normal, Hard, Ultra; the game's enum starts with Normal.</summary>
        public static int ServerDifficulty(GameDifficulty d)
        {
            switch (d)
            {
                case GameDifficulty.Casual: return 0;
                case GameDifficulty.Normal: return 1;
                case GameDifficulty.Hard: return 2;
                default: return 3;
            }
        }

        // ----- plays

        private sealed class Play
        {
            public int Prefix;
            public GameDifficulty Difficulty;
            public System.Threading.Tasks.Task<string> Id;
        }

        private static Play _play;
        private static LevelScores _lastResult;

        /// <summary>A solo game on a marketplace chart starts: open its play on the server.</summary>
        [HarmonyPatch(typeof(StateGame), nameof(StateGame.Begin)), HarmonyPostfix]
        private static void GameStarted(StateGame __instance)
        {
            _play = null;
            try
            {
                var session = __instance.gameSession;
                var level = session?.levelDefinition;
                if (level == null || !session.isSingleplayer || !MarketplacePlugin.Session.SignedIn) return;
                var song = Instance.SongFor(level.highscoreTablePrefix);
                if (song == null) return;
                var difficulty = session.FirstPlayer.playerGameSession.Difficulty;
                _play = new Play
                {
                    Prefix = level.highscoreTablePrefix,
                    Difficulty = difficulty,
                    Id = MarketplacePlugin.Api.StartPlayAsync(song.MarketplaceId, song.MarketplaceVersion, ServerDifficulty(difficulty)),
                };
            }
            catch (Exception e) { MarketplacePlugin.Log.LogError("Could not open a marketplace play: " + e); }
        }

        /// <summary>The results screen's own numbers, sent along with the score.</summary>
        [HarmonyPatch(typeof(StateGameResult), nameof(StateGameResult.TrySubmitScore)), HarmonyPrefix]
        private static void ResultReady(StateGameResult __instance) => _lastResult = __instance.scoreThisRound;

        public async void Submit(int tablePrefix, GameDifficulty difficulty, long score, int percent, Action<Highscores.SubmitResult, HighScoreManager.ServerResult> onComplete)
        {
            var failed = new Highscores.SubmitResult { success = HighScoreManager.ServerResult.Error };
            if (!MarketplacePlugin.Session.SignedIn)
            {
                onComplete(new Highscores.SubmitResult { success = HighScoreManager.ServerResult.NotLoggedIn }, HighScoreManager.ServerResult.NotLoggedIn);
                return;
            }
            var play = _play;
            _play = null;
            var song = SongFor(tablePrefix);
            if (play == null || play.Prefix != tablePrefix || play.Difficulty != difficulty || song == null)
            {
                MarketplacePlugin.Log.LogWarning("Marketplace score not sent: no play was opened for this game.");
                onComplete(failed, HighScoreManager.ServerResult.Error);
                return;
            }
            int d = ServerDifficulty(difficulty);
            var stats = _lastResult;
            try
            {
                string playId = await play.Id;
                var result = await MarketplacePlugin.Api.SubmitScoreAsync(new ScoreSubmission
                {
                    PlayId = playId,
                    Score = (int)Math.Min(int.MaxValue, Math.Max(0, score)),
                    Percent = Mathf.Clamp(percent, 0, 100),
                    Misses = stats?.misses ?? 0,
                    Streak = stats?.streak ?? 0,
                    Perfect = stats?.perfect ?? 0,
                    Notes = d < song.Notes.Length ? song.Notes[d] : 0,
                });
                MarketplacePlugin.Log.LogInfo($"Marketplace score for {song.Title} ({difficulty}): rank {result.Rank}/{result.Total}, best {result.BestScore}");
                MainThread.Post(() => onComplete(new Highscores.SubmitResult { success = HighScoreManager.ServerResult.Success, tempRank = (uint)result.Rank }, HighScoreManager.ServerResult.Success));
            }
            catch (Exception e)
            {
                var status = e.GetBaseException() is MarketplaceException m && m.Status == 401 ? HighScoreManager.ServerResult.NotLoggedIn : HighScoreManager.ServerResult.Error;
                MarketplacePlugin.Log.LogWarning("Marketplace score refused or not sent: " + e.GetBaseException().Message);
                MainThread.Post(() => onComplete(new Highscores.SubmitResult { success = status }, status));
            }
        }

        // ----- lists

        public async void GetGlobalList(int tablePrefix, GameDifficulty difficulty, int start, int count, Action<Highscores.ListResult, HighScoreManager.ServerResult> onComplete)
        {
            var song = SongFor(tablePrefix);
            try
            {
                if (song == null) throw new InvalidOperationException("no marketplace chart for table " + tablePrefix);
                var board = await MarketplacePlugin.Api.ScoresAsync(song.MarketplaceId, song.MarketplaceVersion, ServerDifficulty(difficulty), start, count);
                MainThread.Post(() => onComplete(ToList(board.Entries), HighScoreManager.ServerResult.Success));
            }
            catch (Exception e) { Fail(onComplete, e); }
        }

        public async void GetFriendList(int tablePrefix, GameDifficulty difficulty, int start, int count, Action<Highscores.ListResult, HighScoreManager.ServerResult> onComplete)
        {
            var song = SongFor(tablePrefix);
            try
            {
                if (song == null) throw new InvalidOperationException("no marketplace chart for table " + tablePrefix);
                var board = await MarketplacePlugin.Api.FriendScoresAsync(song.MarketplaceId, song.MarketplaceVersion, ServerDifficulty(difficulty), SteamFriends());
                MainThread.Post(() => onComplete(ToList(board.Entries.Skip(start).Take(count)), HighScoreManager.ServerResult.Success));
            }
            catch (Exception e) { Fail(onComplete, e); }
        }

        /// <summary>The top of a board for the marketplace window (null version: the latest).</summary>
        public static System.Threading.Tasks.Task<BoardList> Top(string chartId, int? version, GameDifficulty difficulty, int count) =>
            MarketplacePlugin.Api.ScoresAsync(chartId, version, ServerDifficulty(difficulty), 0, count);

        private static Highscores.ListResult ToList(IEnumerable<BoardEntry> entries) => new Highscores.ListResult
        {
            success = HighScoreManager.ServerResult.Success,
            entries = entries.Select(e => new Highscores.ListEntry
            {
                name = e.Name,
                onlineUserId = AccountId(e.SteamId),
                score = e.Score,
                comment = e.Percent.ToString(),
                rank = (uint)Math.Max(0, e.Rank),
            }).ToList(),
        };

        /// <summary>
        /// The game knows players by their 32-bit Steam account id (SteamGameSystem.GetOnlineUserId), which is how
        /// it spots its own player in a list; the server speaks 64-bit Steam ids.
        /// </summary>
        private static string AccountId(string steamId64) =>
            ulong.TryParse(steamId64, out ulong id) && id > 76561197960265728UL ? (id - 76561197960265728UL).ToString() : steamId64;

        private static void Fail(Action<Highscores.ListResult, HighScoreManager.ServerResult> onComplete, Exception e)
        {
            MarketplacePlugin.Log.LogWarning("Marketplace leaderboard unavailable: " + e.GetBaseException().Message);
            MainThread.Post(() => onComplete(new Highscores.ListResult { success = HighScoreManager.ServerResult.Error, entries = new List<Highscores.ListEntry>() }, HighScoreManager.ServerResult.Error));
        }

        /// <summary>The player's Steam friends (read locally through the game's Steam library).</summary>
        private static List<string> SteamFriends()
        {
            var ids = new List<string>();
            try
            {
                if (!SteamManager.Initialized) return ids;
                int n = Steamworks.SteamFriends.GetFriendCount(Steamworks.EFriendFlags.k_EFriendFlagImmediate);
                for (int i = 0; i < n && ids.Count < 500; i++)
                    ids.Add(Steamworks.SteamFriends.GetFriendByIndex(i, Steamworks.EFriendFlags.k_EFriendFlagImmediate).m_SteamID.ToString());
            }
            catch (Exception e) { MarketplacePlugin.Log.LogWarning("Steam friends unavailable: " + e.Message); }
            return ids;
        }
    }
}

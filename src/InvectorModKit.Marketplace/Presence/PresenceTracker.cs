using System;
using System.Diagnostics;
using HelloThere.Avicii;
using InvectorModKit.Marketplace.Client.Discord;
using UnityEngine;

namespace InvectorModKit.Marketplace.Presence
{
    /// <summary>
    /// Shows the game on the player's Discord profile: once a second, reads where the player is (and the level's song,
    /// difficulty, score and time left) and hands it to <see cref="DiscordPresence"/>, which talks to Discord off the
    /// main thread. While the level editor runs, the game clears its activity so the editor's own shows.
    /// </summary>
    internal sealed class PresenceTracker : MonoBehaviour
    {
        internal static string ApplicationId = "";

        private DiscordPresence _presence;
        private float _nextSample;
        private float _nextEditorCheck;
        private bool _editorRunning;
        /// <summary>The level being played: the pause menu is a state of its own on top of it.</summary>
        private StateGame _lastGame;

        private void Start()
        {
            if (string.IsNullOrEmpty(ApplicationId)) { enabled = false; return; }
            int pid;
            using (var self = Process.GetCurrentProcess()) pid = self.Id;
            _presence = new DiscordPresence(ApplicationId, pid, () => new Win32DiscordPipe(), m => MarketplacePlugin.Log.LogInfo(m), DateTime.UtcNow);
            _presence.Start();
        }

        private void Update()
        {
            if (_presence == null || Time.unscaledTime < _nextSample) return;
            _nextSample = Time.unscaledTime + 1f;
            try
            {
                if (Time.unscaledTime >= _nextEditorCheck)
                {
                    _nextEditorCheck = Time.unscaledTime + 5f;
                    _editorRunning = EditorRunning();
                }
                _presence.Update(Snapshot(), Texts());
            }
            catch (Exception e)
            {
                MarketplacePlugin.Log.LogWarning("Discord presence: " + e.Message);
            }
        }

        private PresenceSnapshot Snapshot()
        {
            if (_editorRunning) return new PresenceSnapshot { Screen = PresenceScreen.Hidden };
            var state = ModKit.CurrentState;
            if (state is StateGameResult) return new PresenceSnapshot { Screen = PresenceScreen.Results };
            if (state is StateGame g) _lastGame = g;
            else if (!(state is StateGamePause)) _lastGame = null;
            var game = _lastGame;
            if (game?.gameSession?.levelDefinition != null) return Level(game, state is StateGamePause);
            if (Ui.MarketplaceScreen.IsOpen) return new PresenceSnapshot { Screen = PresenceScreen.Marketplace };
            if (state is StateSelectSongSingleplayer || state is StateSelectSongMultiplayer) return new PresenceSnapshot { Screen = PresenceScreen.SongSelect };
            return new PresenceSnapshot { Screen = PresenceScreen.Menu };
        }

        private static PresenceSnapshot Level(StateGame game, bool paused)
        {
            var session = game.gameSession;
            var level = session.levelDefinition;
            var music = level.MusicDefinition;
            var player = session.FirstPlayer?.playerGameSession;
            var score = player?.PlayerScore;
            double remaining = 0;
            var handler = game.musicHandler;
            if (handler?.MusicPlayer != null)
                remaining = Math.Max(0, handler.MusicPlayer.SongEndTime - handler.RealPlaybackTime);
            return new PresenceSnapshot
            {
                Screen = paused ? PresenceScreen.Paused : PresenceScreen.Playing,
                Title = music?.SongName ?? "",
                Artist = music?.ArtistName ?? "",
                CustomSong = level.levelId != null && level.levelId.StartsWith("custom."),
                Difficulty = player != null ? player.Difficulty.ToString() : "",
                Score = score?.ScorePoints ?? 0,
                Combo = score?.CurrentPickupStreak ?? 0,
                RemainingSeconds = remaining,
            };
        }

        /// <summary>English for every player (see <see cref="PresenceTexts.English"/>).</summary>
        private static PresenceTexts Texts() => PresenceTexts.English("InvectorModKit " + ModKit.Version, Ui.Community.DiscordUrl);

        private static bool EditorRunning()
        {
            var editors = Process.GetProcessesByName("InvectorLevelEditor");
            foreach (var p in editors) p.Dispose();
            return editors.Length > 0;
        }

        private void OnDestroy() => Stop();

        private void OnApplicationQuit() => Stop();

        private void Stop()
        {
            _presence?.Dispose();
            _presence = null;
        }
    }
}

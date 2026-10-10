using System;
using HelloThere.Applications;
using HelloThere.Avicii;

namespace InvectorModKit
{
    /// <summary>Game lifecycle events. Handlers run on the main thread; exceptions are logged, not rethrown.</summary>
    public static class GameEvents
    {
        /// <summary>The game switched state (boot, title, menus, song select, game, results...). Args: (from, to); from may be null.</summary>
        public static event Action<ApplicationState, ApplicationState> StateChanged;

        /// <summary>A song is about to be played. Arg: the game session (level, players, difficulty).</summary>
        public static event Action<GameSession> SongStarting;

        /// <summary>A song ended and the results screen is opening. Arg: the game session.</summary>
        public static event Action<GameSession> SongEnded;

        internal static void RaiseStateChanged(ApplicationState from, ApplicationState to) => Raise(StateChanged, h => h(from, to));
        internal static void RaiseSongStarting(GameSession s) => Raise(SongStarting, h => h(s));
        internal static void RaiseSongEnded(GameSession s) => Raise(SongEnded, h => h(s));

        private static void Raise<T>(T handlers, Action<T> invoke) where T : Delegate
        {
            if (handlers == null) return;
            foreach (var d in handlers.GetInvocationList())
            {
                try { invoke((T)d); }
                catch (Exception e) { ModKit.Log?.LogError($"GameEvents handler {d.Method.DeclaringType?.Name}.{d.Method.Name} failed: {e}"); }
            }
        }
    }
}

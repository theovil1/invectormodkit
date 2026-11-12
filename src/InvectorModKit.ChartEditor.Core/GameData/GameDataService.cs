using System;
using System.IO;
using System.Text;
using System.Threading;

namespace InvectorModKit.ChartEditor
{
    /// <summary>None: no game and no cache. Reading: a build runs. Ready: a cache is there. Failed: the last build failed.</summary>
    public enum GameDataState { None, Reading, Ready, Failed }

    /// <summary>
    /// Keeps the editor's copy of the game data current: decides when to build it, says how it went, and logs each
    /// build to game-data.log beside the cache (the editor has no other log). Refresh blocks: the host runs it on a
    /// worker thread.
    /// </summary>
    public sealed class GameDataService
    {
        readonly string root;
        readonly Func<string, string, IProgress<string>, GameDataReport> build;
        int running;

        public GameDataService(string root, Func<string, string, IProgress<string>, GameDataReport> build = null)
        {
            this.root = root;
            this.build = build ?? GameData.Build;
        }

        public string CacheDir => Path.Combine(root, "game");
        public string LogPath => Path.Combine(root, "game-data.log");
        public GameDataState State { get; private set; }

        /// <summary>Raised on the thread running Refresh, only when the state changes.</summary>
        public event Action<GameDataState> Changed;

        bool HasCache => File.Exists(Path.Combine(CacheDir, "index.json"));

        /// <summary>Builds the cache if it is missing or stale; returns at once if another Refresh is running.</summary>
        public void Refresh(string gameDir)
        {
            if (Interlocked.Exchange(ref running, 1) == 1) return;
            try
            {
                if (!GameLocator.IsGameDir(gameDir)) Set(HasCache ? GameDataState.Ready : GameDataState.None);
                else if (GameDataStamp.IsFresh(gameDir, CacheDir)) Set(GameDataState.Ready);
                else Build(gameDir);
            }
            finally
            {
                Interlocked.Exchange(ref running, 0);
            }
        }

        void Build(string gameDir)
        {
            Set(GameDataState.Reading);
            var log = new StringBuilder();
            log.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} reading the game data for the 3D preview from {gameDir}");
            try
            {
                var report = build(gameDir, CacheDir, new LogProgress(log));
                if (report.Busy)
                {
                    log.AppendLine("another editor is building the cache: the current one stays");
                    Set(HasCache ? GameDataState.Ready : GameDataState.None);
                }
                else
                {
                    foreach (string w in report.Warnings) log.AppendLine("warning: " + w);
                    log.AppendLine($"done in {report.Elapsed.TotalSeconds:F1} s, {report.Found.Count} items");
                    Set(GameDataState.Ready);
                }
            }
            catch (Exception e)
            {
                log.AppendLine("failed: " + e);
                Set(GameDataState.Failed);
            }
            finally
            {
                try
                {
                    Directory.CreateDirectory(root);
                    File.WriteAllText(LogPath, log.ToString());
                }
                catch (IOException) { } // the log is a help, not a requirement
                catch (UnauthorizedAccessException) { }
            }
        }

        void Set(GameDataState state)
        {
            if (state == State) return;
            State = state;
            Changed?.Invoke(state);
        }

        /// <summary>Progress lines written straight into the log (Progress&lt;T&gt; would post them to another thread).</summary>
        sealed class LogProgress : IProgress<string>
        {
            readonly StringBuilder log;
            public LogProgress(StringBuilder log) { this.log = log; }
            public void Report(string value) => log.AppendLine(value);
        }
    }
}

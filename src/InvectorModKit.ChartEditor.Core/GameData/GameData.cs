using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace InvectorModKit.ChartEditor
{
    /// <summary>What a build found and what it could not read.</summary>
    public sealed class GameDataReport
    {
        public List<string> Found = new List<string>();
        public List<string> Warnings = new List<string>();
        public TimeSpan Elapsed;

        /// <summary>Another editor was building the cache: nothing was done.</summary>
        public bool Busy;
    }

    public static class GameData
    {
        /// <summary>
        /// Builds the cache for the 3D preview from the installed game into cacheDir (aside, then swapped in). Throws
        /// what the build threw; the previous cache then stays.
        /// </summary>
        public static GameDataReport Build(string gameDir, string cacheDir, IProgress<string> progress)
        {
            GameDataReport report = null;
            var result = new GameDataCache(cacheDir).Rebuild(tmp => report = GameDataBuilder.Write(gameDir, tmp, progress), out var error);
            if (result == RebuildResult.Busy) return new GameDataReport { Busy = true };
            if (result == RebuildResult.Failed) ExceptionDispatchInfo.Capture(error).Throw();
            return report;
        }
    }
}

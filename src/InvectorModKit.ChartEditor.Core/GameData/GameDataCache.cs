using System;
using System.IO;
using System.Threading;

namespace InvectorModKit.ChartEditor
{
    public enum RebuildResult { Built, Busy, Failed }

    /// <summary>
    /// The game data cache folder. A new cache is built aside (dir.tmp) and swapped in with two folder moves (dir to
    /// dir.old, dir.tmp to dir), so a failed or interrupted build never leaves a half-written cache; a lock file keeps
    /// two editors from building at once.
    /// </summary>
    public sealed class GameDataCache
    {
        public GameDataCache(string dir)
        {
            Dir = Path.GetFullPath(dir);
        }

        public string Dir { get; }
        public string TempDir => Dir + ".tmp";
        public string OldDir => Dir + ".old";
        public string LockPath => Dir + ".lock";

        /// <summary>After a stop between the two moves of a swap, puts the old cache back; removes what a build left over.</summary>
        public void Recover()
        {
            if (!Directory.Exists(Dir) && Directory.Exists(OldDir)) Move(OldDir, Dir);
            if (Directory.Exists(OldDir)) Directory.Delete(OldDir, true);
            if (Directory.Exists(TempDir)) Directory.Delete(TempDir, true);
        }

        /// <summary>Runs build into an empty temporary folder and swaps it in; Busy when another editor holds the lock.</summary>
        public RebuildResult Rebuild(Action<string> build, out Exception error)
        {
            error = null;
            Directory.CreateDirectory(Path.GetDirectoryName(Dir));
            FileStream lockFile;
            try
            {
                lockFile = new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            catch (IOException)
            {
                return RebuildResult.Busy;
            }

            using (lockFile)
            {
                try
                {
                    Recover();
                    Directory.CreateDirectory(TempDir);
                    build(TempDir);
                }
                catch (Exception e)
                {
                    error = e;
                    try { if (Directory.Exists(TempDir)) Directory.Delete(TempDir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    return RebuildResult.Failed;
                }

                try
                {
                    if (Directory.Exists(Dir)) Move(Dir, OldDir);
                    Move(TempDir, Dir);
                    if (Directory.Exists(OldDir)) Directory.Delete(OldDir, true);
                }
                catch (Exception e)
                {
                    // Put the old cache back if the new one could not take its place.
                    error = e;
                    try { Recover(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    return RebuildResult.Failed;
                }
                return RebuildResult.Built;
            }
        }

        /// <summary>A folder move, retried a few times: a file being served or scanned can hold it for a moment.</summary>
        static void Move(string from, string to)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    Directory.Move(from, to);
                    return;
                }
                catch (IOException) when (attempt < 5)
                {
                    Thread.Sleep(200);
                }
            }
        }
    }
}

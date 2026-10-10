using System;
using System.IO;
using System.Linq;

namespace InvectorModKit.Internal
{
    /// <summary>
    /// Copies the game's save files at startup. The game silently replaces a save it cannot read with
    /// a fresh one, so a backup is the only way back if anything ever goes wrong.
    /// </summary>
    internal static class SaveBackup
    {
        public static void Run(int keep)
        {
            try
            {
                string root = Paths.GameSaveRoot;
                if (!Directory.Exists(root)) return;
                var saves = Directory.GetFiles(root, "save.dat", SearchOption.AllDirectories);
                if (saves.Length == 0) return;

                string backups = Path.Combine(Paths.DataDirectory, "save-backups");
                string dest = Path.Combine(backups, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
                foreach (string save in saves)
                {
                    string rel = save.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string target = Path.Combine(dest, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(save, target, overwrite: true);
                }
                ModKit.Log.LogInfo($"Backed up {saves.Length} save file(s) to {dest}");

                foreach (var old in new DirectoryInfo(backups).GetDirectories().OrderByDescending(d => d.Name).Skip(Math.Max(1, keep)))
                    old.Delete(recursive: true);
            }
            catch (Exception e)
            {
                ModKit.Log.LogError("Save backup failed: " + e.Message);
            }
        }
    }
}

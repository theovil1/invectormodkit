using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace InvectorModKit.Installer
{
    /// <summary>
    /// Puts the mod in a game folder, updates it, takes it out. The payload is a zip in the game folder's layout:
    /// BepInEx's loader (winhttp.dll, doorstop files, BepInEx/core) and our plugin folders. A BepInEx already there is
    /// kept; the game's files, the player's songs and settings, and other mods are never touched.
    /// </summary>
    public static class ModInstaller
    {
        /// <summary>Our plugin folders under BepInEx/plugins (DevTools is the author's own, never shipped nor removed).</summary>
        public static readonly string[] PluginFolders = { "InvectorModKit", "InvectorModKit.CustomSongs", "InvectorModKit.Marketplace" };

        private const string Staging = ".imk-staging";

        public static string InstalledVersion(string gameDir) => Receipt.Load(gameDir)?.Version;

        /// <summary>Installs <paramref name="payloadZip"/> as <paramref name="version"/>; progress goes from 0 to 1.</summary>
        public static Receipt Install(string gameDir, Stream payloadZip, string version, Action<double> progress)
        {
            if (!GameFolder.IsGame(gameDir)) throw new InstallException("not-game");
            if (IsBepInEx6(gameDir)) throw new InstallException("bepinex6");
            var previous = Receipt.Load(gameDir);
            bool bepinexThere = File.Exists(Full(gameDir, "BepInEx/core/BepInEx.dll"));
            // our loader is installed (and later updated) only where BepInEx is absent or came with us
            bool loaderOurs = !bepinexThere || (previous != null && previous.BepInExOurs);

            string staging = Path.Combine(gameDir, Staging);
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            var written = new List<string>();
            try
            {
                // 1. everything into a staging folder on the same disk (a failed extraction leaves the game as it was)
                var entries = new List<string>();
                using (var zip = new ZipArchive(payloadZip, ZipArchiveMode.Read, leaveOpen: true))
                    foreach (var entry in zip.Entries)
                    {
                        if (entry.FullName.EndsWith("/")) continue;
                        string rel = Normalize(entry.FullName);
                        if (rel == null) continue; // never outside the game folder
                        if (IsLoader(rel) && !loaderOurs) continue;
                        if (IsSetting(rel) && File.Exists(Full(gameDir, rel))) continue; // the player's settings stay
                        string target = Path.Combine(staging, rel.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        entry.ExtractToFile(target, true);
                        entries.Add(rel);
                    }
                // 2. into place
                for (int i = 0; i < entries.Count; i++)
                {
                    string rel = entries[i], target = Full(gameDir, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    try
                    {
                        if (File.Exists(target)) File.Delete(target);
                        File.Move(Path.Combine(staging, rel.Replace('/', Path.DirectorySeparatorChar)), target);
                    }
                    catch (IOException e) when (!(e is InstallException)) { throw new InstallException("in-use", e.Message); }
                    written.Add(rel);
                    progress?.Invoke((i + 1) / (double)entries.Count);
                }
            }
            finally
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
            }

            // 3. what the previous version had and this one does not
            if (previous != null)
                foreach (string rel in previous.Files.Except(written, StringComparer.OrdinalIgnoreCase))
                    if (!IsSetting(rel) && (loaderOurs || !IsLoader(rel))) Delete(gameDir, rel);

            Directory.CreateDirectory(Path.Combine(gameDir, "CustomSongs"));
            var receipt = new Receipt
            {
                Version = version,
                BepInExOurs = loaderOurs,
                // the loader we did not install is not ours to list; settings created by us are (for a full uninstall)
                Files = written.Union(previous?.Files.Where(f => IsSetting(f) && File.Exists(Full(gameDir, f))) ?? Enumerable.Empty<string>()).ToList(),
            };
            receipt.Save(gameDir);
            return receipt;
        }

        /// <summary>
        /// Removes the mod: its files, settings and cache; BepInEx too when it came with the mod and nothing else uses
        /// it. The player's songs (CustomSongs) stay.
        /// </summary>
        public static void Uninstall(string gameDir)
        {
            var receipt = Receipt.Load(gameDir);
            if (receipt != null)
                foreach (string rel in receipt.Files.Where(f => !IsLoader(f))) Delete(gameDir, rel); // the loader goes with BepInEx only
            string bepinex = Path.Combine(gameDir, "BepInEx");
            foreach (string folder in PluginFolders) DeleteDir(Path.Combine(bepinex, "plugins", folder));
            DeleteDir(Path.Combine(bepinex, "cache", "InvectorModKit"));
            DeleteDir(Path.Combine(bepinex, "config", "InvectorModKit"));
            string config = Path.Combine(bepinex, "config");
            if (Directory.Exists(config))
                foreach (string file in Directory.GetFiles(config, "com.invectormodkit.*.cfg")) File.Delete(file);
            string receiptFile = Receipt.PathIn(gameDir);
            if (File.Exists(receiptFile)) File.Delete(receiptFile);

            if (receipt != null && receipt.BepInExOurs && !OthersUse(bepinex))
            {
                DeleteDir(bepinex);
                foreach (string rel in new[] { "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "changelog.txt" })
                    if (File.Exists(Full(gameDir, rel)) && receipt.Files.Contains(rel)) File.Delete(Full(gameDir, rel));
                foreach (string rel in receipt.Files.Where(IsLoader)) Delete(gameDir, rel);
            }
        }

        // ----- rules

        /// <summary>BepInEx and its loader (doorstop), installed only where BepInEx is absent.</summary>
        private static bool IsLoader(string rel) =>
            rel.StartsWith("BepInEx/core/", StringComparison.OrdinalIgnoreCase) || !rel.Contains("/");

        private static bool IsSetting(string rel) => rel.StartsWith("BepInEx/config/", StringComparison.OrdinalIgnoreCase);

        private static bool IsBepInEx6(string gameDir) =>
            File.Exists(Full(gameDir, "BepInEx/core/BepInEx.Core.dll")) || File.Exists(Full(gameDir, "BepInEx/core/BepInEx.Unity.Mono.dll"));

        /// <summary>Other mods' plugins or patchers still need BepInEx.</summary>
        private static bool OthersUse(string bepinex)
        {
            foreach (string sub in new[] { "plugins", "patchers" })
            {
                string dir = Path.Combine(bepinex, sub);
                if (Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any()) return true;
            }
            return false;
        }

        // ----- files

        /// <summary>A zip path as a safe path relative to the game folder ('/' separators), or null.</summary>
        private static string Normalize(string zipPath)
        {
            string rel = zipPath.Replace('\\', '/').TrimStart('/');
            if (rel.Length == 0 || rel.Split('/').Any(p => p == ".." || p == ".") || rel.Contains(":")) return null;
            return rel;
        }

        private static string Full(string gameDir, string rel) => Path.Combine(gameDir, rel.Replace('/', Path.DirectorySeparatorChar));

        /// <summary>Deletes a file and the folders it leaves empty, up to (not including) the game folder.</summary>
        private static void Delete(string gameDir, string rel)
        {
            string path = Full(gameDir, rel);
            if (File.Exists(path)) File.Delete(path);
            string root = Path.GetFullPath(gameDir).TrimEnd(Path.DirectorySeparatorChar);
            for (string dir = Path.GetDirectoryName(path); dir != null && dir.Length > root.Length; dir = Path.GetDirectoryName(dir))
            {
                if (!Directory.Exists(dir) || Directory.EnumerateFileSystemEntries(dir).Any()) break;
                Directory.Delete(dir);
            }
        }

        private static void DeleteDir(string dir)
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}

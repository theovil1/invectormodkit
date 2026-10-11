using System;
using System.IO;
using System.IO.Compression;

namespace InvectorModKit.Installer
{
    /// <summary>
    /// The optional level editor: its zip (everything under InvectorLevelEditor/) unpacked into its own folder
    /// (by default %LOCALAPPDATA%\Programs\InvectorLevelEditor), an older version replaced as a whole.
    /// </summary>
    public static class EditorInstaller
    {
        public const string ExeName = "InvectorLevelEditor.exe";

        public static string DefaultDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "InvectorLevelEditor");

        public static bool IsInstalled(string dir) => File.Exists(Path.Combine(dir, ExeName));

        /// <summary>Unpacks the editor into <paramref name="dir"/>; returns the path of its exe.</summary>
        public static string Install(Stream editorZip, string dir)
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            string root = Path.GetFullPath(dir) + Path.DirectorySeparatorChar;
            using (var zip = new ZipArchive(editorZip, ZipArchiveMode.Read, leaveOpen: true))
                foreach (var entry in zip.Entries)
                {
                    if (entry.FullName.EndsWith("/")) continue;
                    string rel = entry.FullName.Replace('\\', '/');
                    int slash = rel.IndexOf('/');
                    if (slash >= 0 && rel.Substring(0, slash).Equals("InvectorLevelEditor", StringComparison.OrdinalIgnoreCase)) rel = rel.Substring(slash + 1);
                    string target = Path.GetFullPath(Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar)));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue; // never outside its folder
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    entry.ExtractToFile(target, true);
                }
            return Path.Combine(dir, ExeName);
        }

        public static void Uninstall(string dir)
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}

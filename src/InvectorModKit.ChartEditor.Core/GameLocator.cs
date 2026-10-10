using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace InvectorModKit.ChartEditor
{
    /// <summary>Finds Invector: Rhythm Galaxy in the player's Steam libraries.</summary>
    public static class GameLocator
    {
        public const string GameFolder = "Invector Rhythm Galaxy";

        /// <summary>Library folders listed in steamapps/libraryfolders.vdf ("path" entries).</summary>
        public static List<string> LibraryFolders(string vdf)
        {
            var paths = new List<string>();
            foreach (Match m in Regex.Matches(vdf ?? "", "\"path\"\\s*\"((?:[^\"\\\\]|\\\\.)*)\""))
                paths.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
            return paths;
        }

        /// <summary>The game's folder under any Steam library of this Steam install, or null.</summary>
        public static string Find(string steamPath)
        {
            if (string.IsNullOrEmpty(steamPath)) return null;
            var libraries = new List<string> { steamPath };
            string vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf)) libraries.AddRange(LibraryFolders(File.ReadAllText(vdf)));
            foreach (string lib in libraries)
            {
                string dir = Path.Combine(lib.Replace('/', '\\'), "steamapps", "common", GameFolder);
                if (IsGameDir(dir)) return Path.GetFullPath(dir);
            }
            return null;
        }

        public static bool IsGameDir(string dir) => !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, GameFolder + ".exe"));
    }
}

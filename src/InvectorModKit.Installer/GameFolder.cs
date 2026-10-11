using System.Diagnostics;
using InvectorModKit.ChartEditor;

namespace InvectorModKit.Installer
{
    /// <summary>The game's folder: recognising it, finding it through Steam, knowing whether the game is open.</summary>
    public static class GameFolder
    {
        public const string ProcessName = "Invector Rhythm Galaxy";

        public static bool IsGame(string dir) => GameLocator.IsGameDir(dir);

        /// <summary>The game under any library of the Steam install at <paramref name="steamPath"/>, or null.</summary>
        public static string Find(string steamPath) => GameLocator.Find(steamPath);

        public static bool IsRunning() => Process.GetProcessesByName(ProcessName).Length > 0;
    }
}

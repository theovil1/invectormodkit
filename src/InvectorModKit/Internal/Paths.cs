using System.IO;

namespace InvectorModKit.Internal
{
    internal static class Paths
    {
        public static string GameDirectory => BepInEx.Paths.GameRootPath;

        public static string DataDirectory
        {
            get
            {
                string dir = Path.Combine(BepInEx.Paths.ConfigPath, "InvectorModKit");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        /// <summary>LocalLow\Hello There Games\Invector Rhythm Galaxy, with one sub-folder per Steam user holding save.dat.</summary>
        public static string GameSaveRoot => UnityEngine.Application.persistentDataPath;
    }
}

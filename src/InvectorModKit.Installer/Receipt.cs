using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace InvectorModKit.Installer
{
    /// <summary>
    /// What an installation wrote in the game folder (BepInEx/InvectorModKit.install.json): the version, whether BepInEx
    /// came with it, and every file it put there, so an update removes what is gone and an uninstall leaves nothing.
    /// </summary>
    public sealed class Receipt
    {
        public const string RelativePath = "BepInEx/InvectorModKit.install.json";

        [JsonProperty("version")] public string Version;
        [JsonProperty("bepinexOurs")] public bool BepInExOurs;
        /// <summary>Paths relative to the game folder, with '/'.</summary>
        [JsonProperty("files")] public List<string> Files = new List<string>();

        public static string PathIn(string gameDir) => Path.Combine(gameDir, "BepInEx", "InvectorModKit.install.json");

        /// <summary>The receipt of this game folder, or null (never installed, or unreadable).</summary>
        public static Receipt Load(string gameDir)
        {
            string path = PathIn(gameDir);
            if (!File.Exists(path)) return null;
            try { return JsonConvert.DeserializeObject<Receipt>(File.ReadAllText(path)); }
            catch (JsonException) { return null; }
        }

        public void Save(string gameDir)
        {
            string path = PathIn(gameDir);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
        }
    }

    /// <summary>Why an installation cannot go on: "not-game", "bepinex6", "in-use".</summary>
    public sealed class InstallException : IOException
    {
        public string Code { get; }

        public InstallException(string code, string message = null) : base(message ?? code) { Code = code; }
    }
}

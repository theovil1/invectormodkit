using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace InvectorModKit.Installer
{
    /// <summary>
    /// What an installation wrote in the game folder (BepInEx/InvectorModKit.install.json): the version, whether BepInEx
    /// came with it, and every file it put there, so an update removes what is gone and an uninstall leaves nothing.
    /// </summary>
    [DataContract]
    public sealed class Receipt
    {
        public const string RelativePath = "BepInEx/InvectorModKit.install.json";

        [DataMember(Name = "version", Order = 0)] public string Version;
        [DataMember(Name = "bepinexOurs", Order = 1)] public bool BepInExOurs;
        /// <summary>Paths relative to the game folder, with '/'.</summary>
        [DataMember(Name = "files", Order = 2)] public List<string> Files = new List<string>();

        // the framework's own JSON: the installer is a single exe, without Newtonsoft.Json beside it
        private static readonly DataContractJsonSerializer Json = new DataContractJsonSerializer(typeof(Receipt));

        public static string PathIn(string gameDir) => Path.Combine(gameDir, "BepInEx", "InvectorModKit.install.json");

        /// <summary>The receipt of this game folder, or null (never installed, or unreadable).</summary>
        public static Receipt Load(string gameDir)
        {
            string path = PathIn(gameDir);
            if (!File.Exists(path)) return null;
            try
            {
                using (var stream = File.OpenRead(path))
                {
                    var receipt = (Receipt)Json.ReadObject(stream);
                    if (receipt != null && receipt.Files == null) receipt.Files = new List<string>();
                    return receipt;
                }
            }
            catch (SerializationException) { return null; }
        }

        public void Save(string gameDir)
        {
            string path = PathIn(gameDir);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var memory = new MemoryStream())
            {
                Json.WriteObject(memory, this);
                File.WriteAllText(path, Encoding.UTF8.GetString(memory.ToArray()));
            }
        }
    }

    /// <summary>Why an installation cannot go on: "not-game", "bepinex6", "in-use".</summary>
    public sealed class InstallException : IOException
    {
        public string Code { get; }

        public InstallException(string code, string message = null) : base(message ?? code) { Code = code; }
    }
}

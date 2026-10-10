using System;
using System.IO;
using Newtonsoft.Json;

namespace InvectorModKit.ChartEditor
{
    /// <summary>What the editor remembers between sessions (settings.json in its own data folder).</summary>
    public sealed class EditorSettings
    {
        /// <summary>The game's folder (where CustomSongs and the mod live); null until found or chosen.</summary>
        public string GameDir;
        /// <summary>Interface language (a Loc code: en, fr, de…); null follows Windows.</summary>
        public string Lang;
        /// <summary>Marketplace session token.</summary>
        public string Token;
        public string MarketplaceUrl = "https://invector-marketplace.invector-marketplace.workers.dev";

        [JsonIgnore] public string FilePath;

        public string CustomSongsDir => string.IsNullOrEmpty(GameDir) ? null : Path.Combine(GameDir, "CustomSongs");

        public static EditorSettings Load(string dir)
        {
            string path = Path.Combine(dir, "settings.json");
            EditorSettings settings = null;
            try
            {
                if (File.Exists(path)) settings = JsonConvert.DeserializeObject<EditorSettings>(File.ReadAllText(path));
            }
            catch (Exception) { } // unreadable: start over
            settings = settings ?? new EditorSettings();
            settings.FilePath = path;
            return settings;
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(this, Formatting.Indented));
            if (File.Exists(FilePath)) File.Delete(FilePath);
            File.Move(tmp, FilePath);
        }
    }
}

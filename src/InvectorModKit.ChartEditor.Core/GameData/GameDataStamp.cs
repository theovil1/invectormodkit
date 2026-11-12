using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace InvectorModKit.ChartEditor
{
    /// <summary>Where the game keeps its data and whether the cache built from it is still current.</summary>
    public static class GameDataStamp
    {
        public static string DataDir(string gameDir) => Path.Combine(gameDir, "Invector Rhythm Galaxy_Data");
        public static string BundlesDir(string gameDir) => Path.Combine(DataDir(gameDir), "StreamingAssets", "aa", "StandaloneWindows64");
        public static string CatalogPath(string gameDir) => Path.Combine(DataDir(gameDir), "StreamingAssets", "aa", "catalog.json");

        /// <summary>SHA-256 of the addressables catalog (lower case hex); it changes whenever the game's content does.</summary>
        public static string CatalogSha256(string gameDir)
        {
            using (var fs = new FileStream(CatalogPath(gameDir), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(fs);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        public static JObject NewIndex(string gameDir) => new JObject
        {
            ["catalogSha256"] = CatalogSha256(gameDir),
            ["recipeVersion"] = GameDataRecipe.Version,
            ["built"] = DateTime.UtcNow.ToString("o"),
        };

        /// <summary>True when cacheDir\index.json was built from this catalog by this recipe version; any problem means stale.</summary>
        public static bool IsFresh(string gameDir, string cacheDir)
        {
            try
            {
                var index = JObject.Parse(File.ReadAllText(Path.Combine(cacheDir, "index.json")));
                return (int?)index["recipeVersion"] == GameDataRecipe.Version
                    && string.Equals((string)index["catalogSha256"], CatalogSha256(gameDir), StringComparison.Ordinal);
            }
            catch { return false; }
        }
    }
}

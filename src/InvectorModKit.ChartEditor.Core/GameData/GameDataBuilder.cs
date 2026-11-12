using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InvectorModKit.ChartEditor
{
    /// <summary>Runs the recipe against the installed game and writes the cache's files into a folder, index.json last.</summary>
    public static class GameDataBuilder
    {
        public static GameDataReport Write(string gameDir, string outDir, IProgress<string> progress)
        {
            var clock = Stopwatch.StartNew();
            var report = new GameDataReport();
            var items = new JArray();
            using (var reader = new BundleReader(GameDataStamp.BundlesDir(gameDir)))
            {
                var writer = new PrefabWriter(reader, outDir, report);
                foreach (var bundle in GameDataRecipe.Items.Where(i => i.Kind == RecipeKind.Prefab).GroupBy(i => i.Bundle))
                {
                    foreach (var item in bundle)
                    {
                        progress?.Report(item.Key);
                        var root = reader.FindRootGameObject(item.Bundle, item.Name);
                        if (root == null)
                        {
                            NotFound(report, item);
                            continue;
                        }
                        string file = "objects/" + CacheFiles.FileSafe(item.Name) + ".json";
                        WriteJson(outDir, file, writer.Write(root));
                        Found(report, items, item, file);
                    }
                    reader.Release();
                }
            }

            var index = GameDataStamp.NewIndex(gameDir);
            index["items"] = items;
            index["warnings"] = new JArray(report.Warnings);
            WriteJson(outDir, "index.json", index);
            report.Elapsed = clock.Elapsed;
            return report;
        }

        static void Found(GameDataReport report, JArray items, RecipeItem item, string file)
        {
            report.Found.Add(item.Key);
            items.Add(new JObject { ["key"] = item.Key, ["file"] = file });
        }

        static void NotFound(GameDataReport report, RecipeItem item) => report.Warnings.Add($"not found: {item.Key} in {item.Bundle}");

        internal static void WriteJson(string outDir, string relativePath, JToken json)
        {
            string path = Path.Combine(outDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json.ToString(Formatting.None), new UTF8Encoding(false));
        }
    }
}

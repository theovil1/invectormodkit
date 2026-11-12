using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using AssetsTools.NET;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InvectorModKit.ChartEditor
{
    /// <summary>Runs the recipe against the installed game and writes the cache's files into a folder, index.json last.</summary>
    public static class GameDataBuilder
    {
        const int MeshRendererType = 23, AnimationClipType = 74, AnimatorControllerType = 91;

        /// <summary>Animated properties that are not material properties (GameObject, renderer and particle module values).</summary>
        static readonly string[] OtherAttributes =
        {
            "m_IsActive", "m_Enabled", "InitialModule.startColor.maxColor.r", "InitialModule.startColor.maxColor.g",
            "InitialModule.startColor.maxColor.b", "InitialModule.startColor.maxColor.a", "EmissionModule.rateOverTime.scalar",
            "InitialModule.startSize.scalar", "InitialModule.startLifetime.scalar",
        };

        public static GameDataReport Write(string gameDir, string outDir, IProgress<string> progress)
        {
            var clock = Stopwatch.StartNew();
            var report = new GameDataReport();
            var items = new JArray();
            using (var reader = new BundleReader(GameDataStamp.BundlesDir(gameDir)))
            {
                var writer = new PrefabWriter(reader, outDir, report);
                var b = new Run { Reader = reader, Writer = writer, Report = report, Items = items, OutDir = outDir, Progress = progress };

                // Prefabs first: the clips resolve their bindings against the prefabs' transform paths and materials.
                foreach (var bundle in Of(RecipeKind.Prefab).GroupBy(i => i.Bundle))
                {
                    foreach (var item in bundle) b.Prefab(item);
                    reader.Release();
                }
                var scriptables = new JObject();
                var settingsAssets = new JObject();
                foreach (var bundle in GameDataRecipe.Items.Where(i => i.Kind == RecipeKind.Clip || i.Kind == RecipeKind.Controller || i.Kind == RecipeKind.Settings).GroupBy(i => i.Bundle))
                {
                    foreach (var item in bundle)
                    {
                        if (item.Kind == RecipeKind.Clip) b.Clip(item);
                        else if (item.Kind == RecipeKind.Controller) b.Controller(item);
                        else b.Scriptable(item, scriptables, settingsAssets);
                    }
                    reader.Release();
                }
                var settings = new JObject { ["scriptables"] = scriptables, ["walker"] = b.Walker(), ["assets"] = settingsAssets };
                WriteJson(outDir, "settings.json", settings);

                foreach (var item in Of(RecipeKind.Level))
                {
                    b.Level(item);
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

        static IEnumerable<RecipeItem> Of(RecipeKind kind) => GameDataRecipe.Items.Where(i => i.Kind == kind);

        /// <summary>One build's state, shared by the writers of each kind of recipe item.</summary>
        sealed class Run
        {
            public BundleReader Reader;
            public PrefabWriter Writer;
            public GameDataReport Report;
            public JArray Items;
            public string OutDir;
            public IProgress<string> Progress;

            void Found(RecipeItem item, string file)
            {
                Report.Found.Add(item.Key);
                Items.Add(new JObject { ["key"] = item.Key, ["file"] = file });
            }

            void NotFound(RecipeItem item) => Report.Warnings.Add($"not found: {item.Key} in {item.Bundle}");

            public void Prefab(RecipeItem item)
            {
                Progress?.Report(item.Key);
                var root = Reader.FindRootGameObject(item.Bundle, item.Name);
                if (root == null)
                {
                    NotFound(item);
                    return;
                }
                string file = "objects/" + CacheFiles.FileSafe(item.Name) + ".json";
                WriteJson(OutDir, file, Writer.Write(root));
                Found(item, file);
            }

            public void Clip(RecipeItem item)
            {
                Progress?.Report(item.Key);
                var clip = Reader.FindNamed(item.Bundle, AnimationClipType, item.Name);
                if (clip == null)
                {
                    NotFound(item);
                    return;
                }
                string file = "clips/" + CacheFiles.FileSafe(item.Name) + ".json";
                WriteJson(OutDir, file, ClipDecoder.ToJson(item.Name, Muscle(Reader.Read(clip)), Writer.TransformPaths, AttributeNames()));
                Found(item, file);
            }

            IEnumerable<string> AttributeNames()
            {
                string[] suffixes = { "", ".r", ".g", ".b", ".a", ".x", ".y", ".z", ".w" };
                foreach (string p in Writer.MaterialProperties)
                    foreach (string s in suffixes)
                    {
                        yield return "material." + p + s;
                        yield return p + s;
                    }
                foreach (string a in OtherAttributes) yield return a;
            }

            public void Controller(RecipeItem item)
            {
                Progress?.Report(item.Key);
                var controller = Reader.FindNamed(item.Bundle, AnimatorControllerType, item.Name);
                if (controller == null)
                {
                    NotFound(item);
                    return;
                }
                string file = "objects/" + CacheFiles.FileSafe(item.Name) + ".json";
                WriteJson(OutDir, file, Writer.WriteObject(controller, walk: false));
                Found(item, file);
            }

            public void Scriptable(RecipeItem item, JObject scriptables, JObject assets)
            {
                Progress?.Report(item.Key);
                var list = new JArray();
                foreach (var asset in Reader.FindScripts(item.Bundle, item.Class))
                {
                    var o = Writer.WriteObject(asset, walk: false);
                    Merge(assets, o);
                    list.Add(new JObject { ["id"] = o["id"], ["name"] = o["name"], ["fields"] = o["fields"] });
                }
                if (list.Count == 0)
                {
                    NotFound(item);
                    return;
                }
                scriptables[item.Class] = list;
                Found(item, "settings.json");
            }

            /// <summary>The walker prefab's scripts by class, from its file already written.</summary>
            public JObject Walker()
            {
                var walker = new JObject();
                string path = Path.Combine(OutDir, "objects", CacheFiles.FileSafe("PlayerWalkerGlobal") + ".json");
                if (File.Exists(path)) Collect(JObject.Parse(File.ReadAllText(path))["root"], "", walker);
                foreach (string cls in GameDataRecipe.WalkerClasses)
                    if (walker[cls] == null) Report.Warnings.Add($"walker: no {cls}");
                return walker;
            }

            static void Collect(JToken node, string path, JObject walker)
            {
                foreach (var c in node["components"])
                {
                    string cls = (string)c["class"];
                    if ((string)c["type"] != "MonoBehaviour" || !GameDataRecipe.WalkerClasses.Contains(cls)) continue;
                    if (walker[cls] == null) walker[cls] = new JArray();
                    ((JArray)walker[cls]).Add(new JObject { ["id"] = c["id"], ["path"] = path, ["fields"] = c["fields"] });
                }
                foreach (var child in node["children"])
                    Collect(child, path.Length == 0 ? (string)child["name"] : path + "/" + (string)child["name"], walker);
            }

            public void Level(RecipeItem item)
            {
                Progress?.Report(item.Key);
                string world = item.Name, bundle = item.Bundle;
                var editors = Reader.FindScripts(bundle, "SplineEditor").ToList();
                var pairs = Reader.FindScripts(bundle, "Spline")
                    .Select(s => (spline: s, editor: editors.FirstOrDefault(e => SameGameObject(s, e))))
                    .Where(p => p.editor != null).ToList();
                if (pairs.Count == 0)
                {
                    NotFound(item);
                    return;
                }
                var chosen = pairs.OrderByDescending(p => Reader.Read(p.spline)["totalLength"].AsFloat).First();
                if (pairs.Count > 1) Report.Warnings.Add($"level {world}: {pairs.Count} paths, the longest one used");

                var assets = new JObject();
                JObject Fields(AssetRef a)
                {
                    if (a == null) return null;
                    var o = Writer.WriteObject(a, walk: false);
                    Merge(assets, o);
                    return (JObject)o["fields"];
                }
                AssetRef Fed(string cls)
                {
                    var found = Reader.FindScripts(bundle, cls).FirstOrDefault(c => FeedsFrom(c, chosen.editor));
                    if (found == null) Report.Warnings.Add($"level {world}: no {cls}");
                    return found;
                }

                var splineMesh = Fed("SplineMesh");
                var level = LevelJson.Build(Fields(chosen.spline), Fields(chosen.editor), Fields(splineMesh), RoadMaterial(splineMesh, assets),
                    Fields(Fed("Toblerone")), Fields(Fed("TobleroneWalls")));
                level["assets"] = assets;
                string file = "levels/" + CacheFiles.FileSafe(world) + ".json";
                WriteJson(OutDir, file, level);
                Found(item, file);
            }

            bool SameGameObject(AssetRef a, AssetRef b) =>
                a.File == b.File && Reader.Read(a)["m_GameObject"]["m_PathID"].AsLong == Reader.Read(b)["m_GameObject"]["m_PathID"].AsLong;

            bool FeedsFrom(AssetRef component, AssetRef editor)
            {
                var feed = Reader.Read(component)["feed"];
                if (feed.IsDummy) return false;
                var target = Reader.Resolve(component, feed["m_FileID"].AsInt, feed["m_PathID"].AsLong);
                return target != null && Reader.Id(target) == Reader.Id(editor);
            }

            /// <summary>The first material of the road's MeshRenderer (on the SplineMesh's GameObject).</summary>
            JToken RoadMaterial(AssetRef splineMesh, JObject assets)
            {
                if (splineMesh == null) return null;
                var go = Reader.Read(splineMesh)["m_GameObject"];
                var gameObject = Reader.Resolve(splineMesh, go["m_FileID"].AsInt, go["m_PathID"].AsLong);
                if (gameObject == null) return null;
                foreach (var pair in Reader.Read(gameObject)["m_Component"]["Array"].Children)
                {
                    var c = Reader.Resolve(gameObject, pair["component"]["m_FileID"].AsInt, pair["component"]["m_PathID"].AsLong);
                    if (c == null || c.TypeId != MeshRendererType) continue;
                    var renderer = Writer.WriteObject(c, walk: false);
                    Merge(assets, renderer);
                    return renderer["fields"]["m_Materials"]?.FirstOrDefault();
                }
                return null;
            }

            static void Merge(JObject assets, JObject written)
            {
                foreach (var p in ((JObject)written["assets"]).Properties()) assets[p.Name] = p.Value;
            }
        }

        /// <summary>An AnimationClip's muscle clip and bindings, as ClipDecoder takes them.</summary>
        static MuscleClipData Muscle(AssetTypeValueField clip)
        {
            var muscle = clip["m_MuscleClip"];
            var data = muscle["m_Clip"]["data"];
            var streamed = data["m_StreamedClip"];
            var dense = data["m_DenseClip"];
            return new MuscleClipData
            {
                StartTime = Single(muscle["m_StartTime"]),
                StopTime = Single(muscle["m_StopTime"]),
                Streamed = Items(streamed["data"]).Select(f => Convert.ToUInt32(f.Value.AsObject)).ToArray(),
                StreamedCurveCount = (int)Long(streamed["curveCount"]),
                Dense = Items(dense["m_SampleArray"]).Select(Single).ToArray(),
                DenseCurveCount = (int)Long(dense["m_CurveCount"]),
                DenseFrameCount = (int)Long(dense["m_FrameCount"]),
                DenseSampleRate = Single(dense["m_SampleRate"]),
                DenseBeginTime = Single(dense["m_BeginTime"]),
                Constant = Items(data["m_ConstantClip"]["data"]).Select(Single).ToArray(),
                Bindings = Items(clip["m_ClipBindingConstant"]["genericBindings"]).Select(b => new ClipBinding
                {
                    Path = (uint)Long(b["path"]),
                    Attribute = (uint)Long(b["attribute"]),
                    TypeId = (int)Long(b["typeID"]),
                    IsPPtr = Long(b["isPPtrCurve"]) != 0,
                }).ToList(),
            };
        }

        static IEnumerable<AssetTypeValueField> Items(AssetTypeValueField vector) =>
            vector.IsDummy ? Enumerable.Empty<AssetTypeValueField>() : vector["Array"].Children;

        static long Long(AssetTypeValueField f) => f.IsDummy ? 0 : Convert.ToInt64(f.Value.AsObject);

        static float Single(AssetTypeValueField f) => f.IsDummy ? 0 : Convert.ToSingle(f.Value.AsObject);

        internal static void WriteJson(string outDir, string relativePath, JToken json)
        {
            string path = Path.Combine(outDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json.ToString(Formatting.None), new UTF8Encoding(false));
        }
    }
}

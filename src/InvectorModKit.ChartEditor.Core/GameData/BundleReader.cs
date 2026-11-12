using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace InvectorModKit.ChartEditor
{
    /// <summary>One object in a game bundle.</summary>
    public sealed class AssetRef
    {
        public string Bundle;     // file name, like basegame_assets_shared.bundle
        public string ShortName;  // BundleReader.ShortName(Bundle)
        public AssetsFileInstance File;
        public AssetFileInfo Info;
        public long PathId;
        public int TypeId;
    }

    /// <summary>
    /// Reads the game's Addressables bundles with AssetsTools.NET, read only and shared, so the game may be running.
    /// Bundles are loaded when first needed (their serialized files unpacked in memory) and released between recipe
    /// bundles; a reference into another bundle is followed through an index of every bundle's file names (CAB names).
    /// Only the type trees embedded in the bundles are used: no class database ships with the editor.
    /// </summary>
    public sealed class BundleReader : IDisposable
    {
        const int GameObjectType = 1, TransformType = 4, RectTransformType = 224, MonoBehaviourType = 114;

        sealed class Loaded
        {
            public FileStream Stream;
            public BundleFileInstance Bundle;
            public List<AssetsFileInstance> Files = new List<AssetsFileInstance>();
            public Dictionary<string, List<AssetRef>> GameObjectsByName;
        }

        readonly string dir;
        readonly AssetsManager manager = new AssetsManager { UseQuickLookup = true };
        readonly Dictionary<string, string> bundleOfEntry = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Loaded> loaded = new Dictionary<string, Loaded>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> scriptClasses = new Dictionary<string, string>();
        readonly Dictionary<string, string> shaderNames = new Dictionary<string, string>();

        public BundleReader(string bundlesDir)
        {
            dir = Path.GetFullPath(bundlesDir);
            // Some bundles sit in subfolders (the shared MIDI signals, for one): they are named by their relative path.
            foreach (string path in Directory.GetFiles(dir, "*.bundle", SearchOption.AllDirectories))
            {
                using (var stream = OpenShared(path))
                {
                    var bundle = new AssetBundleFile();
                    bundle.Read(new AssetsFileReader(stream));
                    string relative = path.Substring(dir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    foreach (string entry in bundle.GetAllFileNames()) bundleOfEntry[entry] = relative;
                }
            }
        }

        /// <summary>"basegame_assets_shared.bundle" gives "assets_shared"; a leading 32-hex hash and "basegame_" are dropped.</summary>
        public static string ShortName(string bundleFileName)
        {
            string name = Path.GetFileName(bundleFileName);
            if (name.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 7);
            name = Regex.Replace(name, "^[0-9a-fA-F]{32}_", "");
            if (name.StartsWith("basegame_", StringComparison.Ordinal)) name = name.Substring(9);
            return name;
        }

        /// <summary>Opens a game file read only, letting the game (or Steam) keep it open, write it or delete it.</summary>
        public static FileStream OpenShared(string path) =>
            new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        public string Id(AssetRef asset) => asset.ShortName + "-" + asset.PathId;

        Loaded Load(string bundle)
        {
            if (loaded.TryGetValue(bundle, out var done)) return done;
            string path = Path.Combine(dir, bundle);
            var stream = OpenShared(path);
            var result = new Loaded { Stream = stream };
            try
            {
                result.Bundle = manager.LoadBundleFile(stream, path, true);
                int count = result.Bundle.file.BlockAndDirInfo.DirectoryInfos.Count;
                for (int i = 0; i < count; i++)
                    if (result.Bundle.file.IsAssetsFile(i)) result.Files.Add(manager.LoadAssetsFileFromBundle(result.Bundle, i, false));
            }
            catch
            {
                stream.Dispose();
                throw;
            }
            loaded[bundle] = result;
            return result;
        }

        AssetRef Ref(string bundle, AssetsFileInstance file, AssetFileInfo info) => new AssetRef
        {
            Bundle = bundle,
            ShortName = ShortName(bundle),
            File = file,
            Info = info,
            PathId = info.PathId,
            TypeId = info.GetTypeId(file.file),
        };

        /// <summary>Every object of a Unity class (type id) in the bundle's serialized files.</summary>
        public IEnumerable<AssetRef> Assets(string bundle, int typeId)
        {
            foreach (var file in Load(bundle).Files)
                foreach (var info in file.file.GetAssetsOfType(typeId))
                    yield return Ref(bundle, file, info);
        }

        /// <summary>The GameObject of that name at the root of its hierarchy (its Transform has no father); null if none.</summary>
        public AssetRef FindRootGameObject(string bundle, string name)
        {
            var l = Load(bundle);
            if (l.GameObjectsByName == null)
            {
                l.GameObjectsByName = new Dictionary<string, List<AssetRef>>(StringComparer.Ordinal);
                foreach (var go in Assets(bundle, GameObjectType))
                {
                    string n = Read(go)["m_Name"].AsString;
                    if (!l.GameObjectsByName.TryGetValue(n, out var list)) l.GameObjectsByName[n] = list = new List<AssetRef>();
                    list.Add(go);
                }
            }
            if (!l.GameObjectsByName.TryGetValue(name, out var candidates)) return null;
            foreach (var go in candidates)
            {
                var transform = TransformOf(go);
                if (transform != null && Read(transform)["m_Father"]["m_PathID"].AsLong == 0) return go;
            }
            return null;
        }

        /// <summary>A GameObject's Transform (or RectTransform); null if it has none.</summary>
        public AssetRef TransformOf(AssetRef gameObject)
        {
            foreach (var pair in Read(gameObject)["m_Component"]["Array"].Children)
            {
                var c = Resolve(gameObject, pair["component"]["m_FileID"].AsInt, pair["component"]["m_PathID"].AsLong);
                if (c != null && (c.TypeId == TransformType || c.TypeId == RectTransformType)) return c;
            }
            return null;
        }

        /// <summary>MonoBehaviours whose script class has that name (ScriptableObjects included).</summary>
        public IEnumerable<AssetRef> FindScripts(string bundle, string className) =>
            Assets(bundle, MonoBehaviourType).Where(m => ScriptClass(m) == className);

        public AssetTypeValueField Read(AssetRef asset)
        {
            if (!asset.File.file.Metadata.TypeTreeEnabled)
                throw new InvalidDataException($"{asset.File.name} in {asset.Bundle} has no type tree");
            return manager.GetBaseField(asset.File, asset.Info, AssetReadFlags.None);
        }

        /// <summary>The object a reference points at, from the file holding the reference; null if it is not in any bundle.</summary>
        public AssetRef Resolve(AssetRef from, int fileId, long pathId)
        {
            if (pathId == 0) return null;
            AssetsFileInstance file;
            string bundle;
            if (fileId == 0)
            {
                file = from.File;
                bundle = from.Bundle;
            }
            else
            {
                var externals = from.File.file.Metadata.Externals;
                if (fileId < 1 || fileId > externals.Count) return null;
                string entry = EntryName(externals[fileId - 1].PathName);
                if (!bundleOfEntry.TryGetValue(entry, out bundle)) return null;
                file = Load(bundle).Files.FirstOrDefault(f => string.Equals(f.name, entry, StringComparison.OrdinalIgnoreCase));
                if (file == null) return null;
            }
            var info = file.file.GetAssetInfo(pathId);
            return info == null ? null : Ref(bundle, file, info);
        }

        /// <summary>"archive:/CAB-x/CAB-x.resS" gives "CAB-x.resS".</summary>
        static string EntryName(string path) => path.Substring(path.LastIndexOf('/') + 1);

        /// <summary>The class name of a MonoBehaviour's script (m_Script, then the MonoScript's m_ClassName).</summary>
        public string ScriptClass(AssetRef monoBehaviour)
        {
            var script = manager.GetBaseField(monoBehaviour.File, monoBehaviour.Info, AssetReadFlags.SkipMonoBehaviourFields)["m_Script"];
            var target = Resolve(monoBehaviour, script["m_FileID"].AsInt, script["m_PathID"].AsLong);
            if (target == null) return null;
            string id = Id(target);
            if (!scriptClasses.TryGetValue(id, out var name)) scriptClasses[id] = name = Read(target)["m_ClassName"].AsString;
            return name;
        }

        /// <summary>A shader's name as materials show it (m_ParsedForm.m_Name), read once per shader.</summary>
        public string ShaderName(AssetRef shader)
        {
            string id = Id(shader);
            if (!shaderNames.TryGetValue(id, out var name)) shaderNames[id] = name = Read(shader)["m_ParsedForm"]["m_Name"].AsString;
            return name;
        }

        /// <summary>Bytes of a resource file in a bundle (texture and mesh data streamed out of the serialized file).</summary>
        public byte[] ReadResource(AssetRef from, string path, long offset, long size)
        {
            string entry = EntryName(path);
            var bundle = from.File.parentBundle;
            int index = bundle?.file.GetFileIndex(entry) ?? -1;
            if (index < 0)
            {
                if (!bundleOfEntry.TryGetValue(entry, out var other)) throw new FileNotFoundException("resource not in any bundle", entry);
                bundle = Load(other).Bundle;
                index = bundle.file.GetFileIndex(entry);
            }
            bundle.file.GetFileRange(index, out long start, out long length);
            if (offset < 0 || size < 0 || offset + size > length) throw new InvalidDataException($"resource range outside {entry}");
            var reader = bundle.file.DataReader;
            reader.Position = start + offset;
            return reader.ReadBytes(checked((int)size));
        }

        /// <summary>Unloads every bundle (their unpacked data can be large); they are loaded again when needed.</summary>
        public void Release()
        {
            manager.UnloadAll(false);
            foreach (var l in loaded.Values) l.Stream.Dispose();
            loaded.Clear();
        }

        public void Dispose() => Release();
    }
}

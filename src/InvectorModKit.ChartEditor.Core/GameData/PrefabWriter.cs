using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Newtonsoft.Json.Linq;

namespace InvectorModKit.ChartEditor
{
    /// <summary>
    /// Writes a prefab (a root GameObject and its hierarchy) as one JSON document: its tree of nodes with their
    /// components' fields, plus an "assets" map of the other objects they reference. Meshes and textures go to their
    /// own files (meshes/, textures/), materials and sprites are inlined where referenced, script references become
    /// their class name.
    /// </summary>
    public sealed class PrefabWriter
    {
        const int GameObjectType = 1, TransformType = 4, RectTransformType = 224, MaterialType = 21, TextureType = 28,
            MeshType = 43, MonoScriptType = 115, SpriteType = 213, MonoBehaviourType = 114;

        /// <summary>The object types followed into the "assets" map (the spec's reference walk, shaders excepted).</summary>
        static readonly HashSet<int> WalkTypes = new HashSet<int>
        {
            GameObjectType, TransformType, RectTransformType, 33 /* MeshFilter */, 23 /* MeshRenderer */, 137 /* SkinnedMeshRenderer */,
            212 /* SpriteRenderer */, 198 /* ParticleSystem */, 199 /* ParticleSystemRenderer */, 96 /* TrailRenderer */,
            95 /* Animator */, MonoBehaviourType,
        };

        /// <summary>At most this many referenced objects in one document's "assets" map.</summary>
        public const int MaxAssets = 2000;

        readonly BundleReader reader;
        readonly string outDir;
        readonly GameDataReport report;
        readonly HashSet<string> recipeBundles = new HashSet<string>(GameDataRecipe.Items.Select(i => i.Bundle), StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> files = new HashSet<string>(); // ids of meshes and textures written (or that failed)
        readonly HashSet<string> failed = new HashSet<string>();
        readonly Dictionary<string, JObject> inlined = new Dictionary<string, JObject>(); // materials and sprites
        readonly HashSet<string> transformPaths = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> materialProperties = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The document being written: its own objects, and the referenced ones collected for its "assets" map.</summary>
        sealed class Document
        {
            public HashSet<string> Own = new HashSet<string>();
            public JObject Assets = new JObject();
            public Queue<AssetRef> Pending = new Queue<AssetRef>();
            public HashSet<string> Queued = new HashSet<string>();
            public SortedSet<string> MissingFiles = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            public int Missing;
            public bool Capped;
            public bool Walk = true;
        }

        Document doc;

        public PrefabWriter(BundleReader reader, string outDir, GameDataReport report)
        {
            this.reader = reader;
            this.outDir = outDir;
            this.report = report;
        }

        /// <summary>Every path from a node to one of its descendants ("" for the node itself), over all prefabs written.</summary>
        public IEnumerable<string> TransformPaths => transformPaths;

        /// <summary>Every property name of every material written.</summary>
        public IEnumerable<string> MaterialProperties => materialProperties;

        public JObject Write(AssetRef rootGameObject)
        {
            doc = new Document();
            Collect(rootGameObject);
            string name = reader.Read(rootGameObject)["m_Name"].AsString;
            var root = Node(rootGameObject, new List<string>());
            var result = new JObject { ["name"] = name, ["id"] = reader.Id(rootGameObject), ["root"] = root };
            result["assets"] = FinishDocument(name);
            return result;
        }

        /// <summary>
        /// A single object (a ScriptableObject, a level component, an AnimatorController). With walk false, references
        /// to other GameObjects, components and scripts stay plain references instead of filling the "assets" map
        /// (a scene's objects would otherwise pull in the whole scene); materials, meshes and textures are kept either way.
        /// </summary>
        public JObject WriteObject(AssetRef asset, bool walk = true)
        {
            doc = new Document { Walk = walk };
            doc.Own.Add(reader.Id(asset));
            var field = reader.Read(asset);
            string name = field["m_Name"].IsDummy ? null : field["m_Name"].AsString;
            var result = Describe(asset, field);
            result["assets"] = FinishDocument(name ?? reader.Id(asset));
            return result;
        }

        /// <summary>The JSON for a reference from one object to another, by the policy described on the class.</summary>
        public JToken Link(AssetRef from, int fileId, long pathId)
        {
            if (pathId == 0) return JValue.CreateNull();
            var target = reader.Resolve(from, fileId, pathId);
            if (target == null)
            {
                // Unity's own built-in meshes, materials and textures live in the player, not in bundles; their path
                // ids are stable (10210 is the Quad mesh, for instance), so the preview can tell them apart.
                string builtin = BuiltinFile(from, fileId);
                if (builtin != null) return new JObject { ["ref"] = "builtin-" + pathId, ["builtin"] = builtin };
                doc.Missing++;
                doc.MissingFiles.Add(FileName(from, fileId));
                return new JObject { ["ref"] = (fileId == 0 ? from.ShortName : "?") + "-" + pathId, ["missing"] = true };
            }
            string id = reader.Id(target);
            if (doc.Own.Contains(id)) return Ref(id);
            switch (target.TypeId)
            {
                case MeshType: return WriteFile(target, id, WriteMesh);
                case TextureType: return WriteFile(target, id, WriteTexture);
                case MaterialType: return Inline(id, () => Material(target, id));
                case SpriteType: return Inline(id, () => Sprite(target, id));
                case MonoScriptType: return new JValue(reader.Read(target)["m_ClassName"].AsString);
            }
            if (doc.Walk && WalkTypes.Contains(target.TypeId) && recipeBundles.Contains(target.Bundle))
            {
                if (doc.Queued.Contains(id)) return Ref(id);
                if (doc.Queued.Count < MaxAssets)
                {
                    doc.Queued.Add(id);
                    doc.Pending.Enqueue(target);
                    return Ref(id);
                }
                doc.Capped = true;
            }
            return new JObject { ["ref"] = id, ["type"] = TypeName(target.TypeId) };
        }

        static JObject Ref(string id) => new JObject { ["ref"] = id };

        /// <summary>"unity default resources" or "unity_builtin_extra" when a reference points into the player's built-in resources.</summary>
        static string BuiltinFile(AssetRef from, int fileId)
        {
            var externals = from.File.file.Metadata.Externals;
            if (fileId < 1 || fileId > externals.Count) return null;
            string path = externals[fileId - 1].PathName;
            string name = path.Substring(path.LastIndexOfAny(new[] { '/', '\\' }) + 1);
            return name.Equals("unity default resources", StringComparison.OrdinalIgnoreCase)
                || name.Equals("unity_builtin_extra", StringComparison.OrdinalIgnoreCase) ? name : null;
        }

        /// <summary>The serialized file a reference points into, for warnings.</summary>
        static string FileName(AssetRef from, int fileId)
        {
            if (fileId == 0) return from.File.name;
            var externals = from.File.file.Metadata.Externals;
            return fileId >= 1 && fileId <= externals.Count ? externals[fileId - 1].PathName : "file " + fileId;
        }

        static string TypeName(int typeId) => Enum.IsDefined(typeof(AssetClassID), typeId) ? ((AssetClassID)typeId).ToString() : typeId.ToString();

        JObject FinishDocument(string name)
        {
            while (doc.Pending.Count > 0)
            {
                var asset = doc.Pending.Dequeue();
                doc.Assets[reader.Id(asset)] = Describe(asset, reader.Read(asset));
            }
            if (doc.Capped) report.Warnings.Add($"{name}: more than {MaxAssets} referenced objects, the rest left as plain references");
            if (doc.Missing > 0) report.Warnings.Add($"{name}: {doc.Missing} references not found (in {string.Join(", ", doc.MissingFiles)})");
            return doc.Assets;
        }

        /// <summary>{"id", "type", "class"?, "name"?, "fields"} of any object.</summary>
        JObject Describe(AssetRef asset, AssetTypeValueField field)
        {
            var result = new JObject { ["id"] = reader.Id(asset), ["type"] = field.TypeName };
            if (asset.TypeId == MonoBehaviourType) result["class"] = reader.ScriptClass(asset);
            if (!field["m_Name"].IsDummy && !string.IsNullOrEmpty(field["m_Name"].AsString)) result["name"] = field["m_Name"].AsString;
            result["fields"] = TypeTreeJson.Convert(field, (f, p) => Link(asset, f, p));
            return result;
        }

        /// <summary>First pass over the hierarchy: the ids of its GameObjects and their components are the document's own.</summary>
        void Collect(AssetRef gameObject)
        {
            doc.Own.Add(reader.Id(gameObject));
            AssetRef transform = null;
            foreach (var c in Components(gameObject))
            {
                doc.Own.Add(reader.Id(c));
                if (c.TypeId == TransformType || c.TypeId == RectTransformType) transform = c;
            }
            if (transform == null) return;
            foreach (var child in Children(transform)) Collect(child);
        }

        IEnumerable<AssetRef> Components(AssetRef gameObject)
        {
            foreach (var pair in reader.Read(gameObject)["m_Component"]["Array"].Children)
            {
                var c = reader.Resolve(gameObject, pair["component"]["m_FileID"].AsInt, pair["component"]["m_PathID"].AsLong);
                if (c != null) yield return c;
            }
        }

        /// <summary>The child GameObjects of a Transform, in order.</summary>
        IEnumerable<AssetRef> Children(AssetRef transform)
        {
            foreach (var child in reader.Read(transform)["m_Children"]["Array"].Children)
            {
                var t = reader.Resolve(transform, child["m_FileID"].AsInt, child["m_PathID"].AsLong);
                if (t == null) continue;
                var go = reader.Read(t)["m_GameObject"];
                var g = reader.Resolve(t, go["m_FileID"].AsInt, go["m_PathID"].AsLong);
                if (g != null) yield return g;
            }
        }

        JObject Node(AssetRef gameObject, List<string> ancestors)
        {
            var go = reader.Read(gameObject);
            string name = go["m_Name"].AsString;
            var node = new JObject { ["name"] = name, ["id"] = reader.Id(gameObject), ["active"] = go["m_IsActive"].AsBool };

            // paths from each ancestor (and the node itself) down to this node, for animation bindings
            var names = new List<string>(ancestors) { name };
            for (int k = 0; k < names.Count; k++) transformPaths.Add(string.Join("/", names.Skip(k + 1)));

            var components = new JArray();
            AssetRef transform = null;
            foreach (var c in Components(gameObject))
            {
                if (c.TypeId == TransformType || c.TypeId == RectTransformType)
                {
                    transform = c;
                    var t = reader.Read(c);
                    node["position"] = Vector(t["m_LocalPosition"], "x", "y", "z");
                    node["rotation"] = Vector(t["m_LocalRotation"], "x", "y", "z", "w");
                    node["scale"] = Vector(t["m_LocalScale"], "x", "y", "z");
                    continue;
                }
                components.Add(Describe(c, reader.Read(c)));
            }
            node["components"] = components;
            var children = new JArray();
            if (transform != null)
                foreach (var child in Children(transform)) children.Add(Node(child, names));
            node["children"] = children;
            return node;
        }

        static JArray Vector(AssetTypeValueField field, params string[] axes) => new JArray(axes.Select(a => Number(field[a].AsFloat)));

        static JToken Number(float v) => float.IsNaN(v) || float.IsInfinity(v) ? JValue.CreateNull() : new JValue(v);

        JToken Inline(string id, Func<JObject> make)
        {
            if (!inlined.TryGetValue(id, out var json)) inlined[id] = json = make();
            return json.DeepClone();
        }

        JToken WriteFile(AssetRef target, string id, Action<AssetRef, string> write)
        {
            if (files.Add(id))
            {
                try { write(target, id); }
                catch (Exception e) when (e is NotSupportedException || e is InvalidDataException || e is IOException || e is ArgumentException)
                {
                    failed.Add(id);
                    report.Warnings.Add($"{TypeName(target.TypeId)} {id}: {e.Message}");
                }
            }
            return failed.Contains(id) ? new JObject { ["ref"] = id, ["missing"] = true } : Ref(id);
        }

        JObject Material(AssetRef material, string id)
        {
            var f = reader.Read(material);
            var shaderPtr = f["m_Shader"];
            var shader = reader.Resolve(material, shaderPtr["m_FileID"].AsInt, shaderPtr["m_PathID"].AsLong);
            var props = f["m_SavedProperties"];

            var textures = new JObject();
            foreach (var pair in Pairs(props["m_TexEnvs"]))
            {
                string name = pair["first"].AsString;
                materialProperties.Add(name);
                var env = pair["second"];
                var tex = env["m_Texture"];
                if (tex["m_PathID"].AsLong == 0) continue;
                var link = Link(material, tex["m_FileID"].AsInt, tex["m_PathID"].AsLong) as JObject ?? new JObject();
                link["scale"] = Vector(env["m_Scale"], "x", "y");
                link["offset"] = Vector(env["m_Offset"], "x", "y");
                textures[name] = link;
            }
            var floats = new JObject();
            foreach (var pair in Pairs(props["m_Floats"]))
            {
                materialProperties.Add(pair["first"].AsString);
                floats[pair["first"].AsString] = Number(pair["second"].AsFloat);
            }
            var colors = new JObject();
            foreach (var pair in Pairs(props["m_Colors"]))
            {
                materialProperties.Add(pair["first"].AsString);
                colors[pair["first"].AsString] = Vector(pair["second"], "r", "g", "b", "a");
            }

            return new JObject
            {
                ["id"] = id,
                ["name"] = f["m_Name"].AsString,
                ["shader"] = shader == null ? null : reader.ShaderName(shader),
                ["keywords"] = new JArray(Keywords(f)),
                ["renderQueue"] = f["m_CustomRenderQueue"].IsDummy ? -1 : f["m_CustomRenderQueue"].AsInt,
                ["colors"] = colors,
                ["floats"] = floats,
                ["textures"] = textures,
            };
        }

        /// <summary>A material's shader keywords: m_ValidKeywords (Unity 2021.2 and later) or the older space-separated m_ShaderKeywords.</summary>
        static IEnumerable<string> Keywords(AssetTypeValueField material)
        {
            var valid = material["m_ValidKeywords"];
            if (!valid.IsDummy) return valid["Array"].Children.Select(k => k.AsString);
            var old = material["m_ShaderKeywords"];
            return old.IsDummy ? Enumerable.Empty<string>() : old.AsString.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        static IEnumerable<AssetTypeValueField> Pairs(AssetTypeValueField map) => map.IsDummy ? Enumerable.Empty<AssetTypeValueField>() : map["Array"].Children;

        JObject Sprite(AssetRef sprite, string id)
        {
            var f = reader.Read(sprite);
            var owner = sprite;
            var tex = f["m_RD"]["texture"];
            var rect = f["m_RD"]["textureRect"];

            // A packed sprite has no texture of its own: its sprite atlas maps its render data key to a page and a rect.
            var atlasPtr = f["m_SpriteAtlas"];
            if (tex["m_PathID"].AsLong == 0 && !atlasPtr.IsDummy)
            {
                var atlas = reader.Resolve(sprite, atlasPtr["m_FileID"].AsInt, atlasPtr["m_PathID"].AsLong);
                if (atlas != null)
                {
                    var key = TypeTreeJson.Convert(f["m_RenderDataKey"], (a, b) => null);
                    foreach (var pair in reader.Read(atlas)["m_RenderDataMap"]["Array"].Children)
                    {
                        if (!JToken.DeepEquals(TypeTreeJson.Convert(pair["first"], (a, b) => null), key)) continue;
                        owner = atlas;
                        tex = pair["second"]["texture"];
                        rect = pair["second"]["textureRect"];
                        break;
                    }
                }
            }

            return new JObject
            {
                ["id"] = id,
                ["name"] = f["m_Name"].AsString,
                ["texture"] = Link(owner, tex["m_FileID"].AsInt, tex["m_PathID"].AsLong),
                ["rect"] = Vector(rect, "x", "y", "width", "height"),
                ["pivot"] = Vector(f["m_Pivot"], "x", "y"),
                ["pixelsPerUnit"] = Number(f["m_PixelsToUnits"].AsFloat),
            };
        }

        /// <summary>The bytes of a TypelessData or byte vector field.</summary>
        static byte[] Bytes(AssetTypeValueField field)
        {
            if (field.IsDummy) return new byte[0];
            if (field.Value != null && field.Value.ValueType == AssetValueType.ByteArray) return field.AsByteArray ?? new byte[0];
            if (field.Children.Count == 1 && field.Children[0].TemplateField.IsArray) return Bytes(field.Children[0]);
            return new byte[0];
        }

        static long Long(AssetTypeValueField field) => field.IsDummy ? 0 : Convert.ToInt64(field.Value.AsObject);

        /// <summary>Data kept outside the serialized file (m_StreamData), or the inline bytes.</summary>
        byte[] Data(AssetRef asset, byte[] inline, AssetTypeValueField streamData)
        {
            if (inline.Length > 0 || streamData.IsDummy) return inline;
            long size = Long(streamData["size"]);
            return size == 0 ? inline : reader.ReadResource(asset, streamData["path"].AsString, Long(streamData["offset"]), size);
        }

        void WriteMesh(AssetRef mesh, string id)
        {
            var f = reader.Read(mesh);
            if (!f["m_MeshCompression"].IsDummy && f["m_MeshCompression"].AsInt != 0)
                throw new NotSupportedException($"{f["m_Name"].AsString}: compressed mesh not supported");
            var vd = f["m_VertexData"];
            var data = new MeshData
            {
                VertexCount = (int)Long(vd["m_VertexCount"]),
                Channels = vd["m_Channels"]["Array"].Children.Select(c => new VertexChannel
                {
                    Stream = c["stream"].AsInt, Offset = c["offset"].AsInt, Format = c["format"].AsInt, Dimension = c["dimension"].AsInt,
                }).ToList(),
                IndexBytes = Bytes(f["m_IndexBuffer"]),
                Index32 = !f["m_IndexFormat"].IsDummy && f["m_IndexFormat"].AsInt == 1,
                SubMeshes = f["m_SubMeshes"]["Array"].Children.Select(s => new SubMeshData
                {
                    FirstByte = (int)Long(s["firstByte"]), IndexCount = (int)Long(s["indexCount"]), Topology = s["topology"].AsInt, BaseVertex = (int)Long(s["baseVertex"]),
                }).ToList(),
                BoundsCenter = Vector(f["m_LocalAABB"]["m_Center"], "x", "y", "z").Select(v => v.Type == JTokenType.Null ? 0f : (float)v).ToArray(),
                BoundsExtent = Vector(f["m_LocalAABB"]["m_Extent"], "x", "y", "z").Select(v => v.Type == JTokenType.Null ? 0f : (float)v).ToArray(),
            };
            data.VertexBytes = Data(mesh, Bytes(vd["m_DataSize"]), f["m_StreamData"]);
            var decoded = MeshDecoder.Decode(data);
            string path = Path.Combine(outDir, "meshes", id + ".bin");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var stream = File.Create(path)) MeshDecoder.Write(decoded, stream);
        }

        void WriteTexture(AssetRef texture, string id)
        {
            var f = reader.Read(texture);
            string name = f["m_Name"].AsString;
            var settings = f["m_TextureSettings"];
            var data = new TextureData
            {
                Format = f["m_TextureFormat"].AsInt,
                Width = f["m_Width"].AsInt,
                Height = f["m_Height"].AsInt,
                MipCount = f["m_MipCount"].IsDummy ? 1 : f["m_MipCount"].AsInt,
                ColorSpace = f["m_ColorSpace"].IsDummy ? 1 : f["m_ColorSpace"].AsInt,
                Filter = settings["m_FilterMode"].AsInt,
                WrapU = settings["m_WrapU"].AsInt,
                WrapV = settings["m_WrapV"].AsInt,
                Bytes = Data(texture, Bytes(f["image data"]), f["m_StreamData"]),
            };
            if (TextureWriter.IsCrunched(data.Format)) report.Warnings.Add($"texture {name} ({id}): format {data.Format} crunched, not supported");
            else if (!TextureWriter.Supported.Contains(data.Format)) report.Warnings.Add($"texture {name} ({id}): format {data.Format} not supported");
            string path = Path.Combine(outDir, "textures", id + ".tex");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var stream = File.Create(path)) TextureWriter.Write(data, stream);
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using HelloThere.Avicii;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace InvectorModKit.CustomSongs
{
    /// <summary>
    /// The game's look for the chart editor (InvectorChartEditor): the menus' sprites (with their 9-slice borders),
    /// key and controller icons, badges, world and ship icons, and the menu and world backgrounds rendered without
    /// the interface. Written to BepInEx/cache/InvectorModKit/editor-skin on this computer only, while the player is
    /// in the menus, a few sprites per frame; nothing leaves the game folder.
    /// </summary>
    internal sealed partial class EditorSkin : MonoBehaviour
    {
        public static string Dir => Path.Combine(Paths.CachePath, "InvectorModKit", "editor-skin");
        private const int Version = 1;

        private JObject _manifest;
        private readonly HashSet<string> _failed = new HashSet<string>(); // not retried this session
        private bool _running;
        private float _next;

        private void Start()
        {
            string path = Path.Combine(Dir, "manifest.json");
            try
            {
                if (File.Exists(path)) _manifest = JObject.Parse(File.ReadAllText(path));
            }
            catch (Exception) { }
            if (_manifest == null || (int?)_manifest["version"] != Version || (string)_manifest["game"] != Application.version)
                _manifest = new JObject { ["version"] = Version, ["game"] = Application.version, ["sprites"] = new JObject(), ["backgrounds"] = new JObject() };
            foreach (var key in new[] { "models", "ships" })
                if (!(_manifest[key] is JObject)) _manifest[key] = new JObject();
        }

        private void Update()
        {
            if (_running || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 4f;
            var state = ModKit.CurrentState;
            if (state is StateGameResult || state is StateGameResultCampaign)
            {
                if (WantsPickups) StartCoroutine(ExportPickups());
            }
            else if (state is StateMainMenu || state is StateSelectSongSingleplayer)
            {
                if (WantsShip(out var ship)) StartCoroutine(ExportShip(ship));
                else StartCoroutine(Export(state));
            }
        }

        private static bool Wanted(Sprite s)
        {
            if (s == null || s.texture == null || string.IsNullOrEmpty(s.name)) return false;
            string n = s.name;
            if (n.StartsWith("cover", StringComparison.OrdinalIgnoreCase) || n.EndsWith("(Clone)")) return false; // album art is not part of the look
            return s.rect.width >= 4 && s.rect.height >= 4 && s.texture.width <= 4096 && s.texture.height <= 4096;
        }

        private IEnumerator Export(object state)
        {
            _running = true;
            var sprites = (JObject)_manifest["sprites"];
            var todo = Resources.FindObjectsOfTypeAll<Sprite>().Where(Wanted).GroupBy(s => s.name).Select(g => g.First())
                .Where(s => sprites[s.name] == null && !_failed.Contains(s.name)).ToList();
            var readable = new Dictionary<Texture, Texture2D>();
            int done = 0;
            try
            {
                Directory.CreateDirectory(Path.Combine(Dir, "sprites"));
                foreach (var sprite in todo)
                {
                    try { ExportSprite(sprite, readable, sprites); }
                    catch (Exception e)
                    {
                        _failed.Add(sprite.name);
                        CustomSongsPlugin.Log.LogDebug($"Editor skin: {sprite.name}: {e.Message}");
                    }
                    if (++done % 8 == 0) yield return null; // a few per frame: no stutter
                }
            }
            finally
            {
                foreach (var t in readable.Values) Destroy(t);
            }
            yield return null;
            string background = state is StateMainMenu ? "menu" : state is StateSelectSongSingleplayer select ? "world-" + select.selectedWorldIndex : null;
            if (background != null && _manifest["backgrounds"][background] == null)
            {
                try
                {
                    Directory.CreateDirectory(Path.Combine(Dir, "backgrounds"));
                    if (CaptureBackground(Path.Combine(Dir, "backgrounds", background + ".jpg")))
                        _manifest["backgrounds"][background] = "backgrounds/" + background + ".jpg";
                }
                catch (Exception e) { CustomSongsPlugin.Log.LogDebug("Editor skin background: " + e.Message); }
            }
            if (done > 0 || background != null)
            {
                SaveManifest();
                if (done > 0) CustomSongsPlugin.Log.LogInfo($"Editor skin: {done} sprite(s) looked at for the chart editor");
            }
            _running = false;
        }

        private void SaveManifest()
        {
            try { File.WriteAllText(Path.Combine(Dir, "manifest.json"), _manifest.ToString()); }
            catch (Exception e) { CustomSongsPlugin.Log.LogWarning("Editor skin: " + e.Message); }
        }

        /// <summary>The sprite's pixels as a PNG (its texture is copied once through the GPU: game textures are not readable).</summary>
        private static void ExportSprite(Sprite sprite, Dictionary<Texture, Texture2D> readable, JObject manifest)
        {
            var texture = sprite.texture;
            if (!readable.TryGetValue(texture, out var copy))
            {
                var rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var previous = RenderTexture.active;
                Graphics.Blit(texture, rt);
                RenderTexture.active = rt;
                copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, false);
                copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                copy.Apply();
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                readable[texture] = copy;
            }
            Rect r;
            try { r = sprite.textureRect; }
            catch (Exception) { r = sprite.rect; } // tightly packed: its bounding rectangle
            int x = Mathf.RoundToInt(r.x), y = Mathf.RoundToInt(r.y), w = Mathf.RoundToInt(r.width), h = Mathf.RoundToInt(r.height);
            var pixels = copy.GetPixels(x, y, w, h);
            var image = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
            image.SetPixels(pixels);
            image.Apply();
            string file = "sprites/" + Safe(sprite.name) + ".png";
            File.WriteAllBytes(Path.Combine(Dir, file), image.EncodeToPNG());
            Destroy(image);
            var b = sprite.border; // left, bottom, right, top
            manifest[sprite.name] = new JObject { ["file"] = file, ["w"] = w, ["h"] = h, ["border"] = new JArray(b.x, b.y, b.z, b.w) };
        }

        /// <summary>The 3D scene the cameras see, without the interface layer, as a JPEG.</summary>
        private static bool CaptureBackground(string path)
        {
            int ui = LayerMask.NameToLayer("UI");
            var cameras = Camera.allCameras.Where(c => c.enabled && c.targetTexture == null).OrderBy(c => c.depth).ToList();
            if (cameras.Count == 0) return false;
            var rt = RenderTexture.GetTemporary(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            foreach (var cam in cameras)
            {
                int mask = cam.cullingMask;
                cam.targetTexture = rt;
                if (ui >= 0) cam.cullingMask = mask & ~(1 << ui);
                try { cam.Render(); }
                finally
                {
                    cam.targetTexture = null;
                    cam.cullingMask = mask;
                }
            }
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false, false);
            image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(path, image.EncodeToJPG(88));
            Destroy(image);
            return true;
        }

        private static string Safe(string name)
        {
            var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
            return new string(name.Select(c => invalid.Contains(c) || c == ' ' ? '_' : c).ToArray());
        }
    }
}

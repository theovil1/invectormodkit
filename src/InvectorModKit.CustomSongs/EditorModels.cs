using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HelloThere.Avicii;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace InvectorModKit.CustomSongs
{
    /// <summary>
    /// The game's 3D pickups and ship as pictures for the chart editor's preview: each model is copied far below the
    /// level, on a layer of its own, and photographed by a camera that sees nothing else, once on black and once on
    /// white so that its glow keeps its transparency. Pickups are taken on the results screen (the level's entity
    /// database is loaded, nothing is played), the selected ship in the menus.
    /// </summary>
    internal sealed partial class EditorSkin
    {
        private const int ShotSize = 320;

        private static readonly EntityType[] ModelTypes =
        {
            EntityType.PickupLeft, EntityType.PickupRight, EntityType.PickupDown, EntityType.PickupUp,
            EntityType.PickupTurnLeft, EntityType.PickupTurnRight, EntityType.PickupAir,
            EntityType.PickupStrum, EntityType.PickupStrumHold, EntityType.PickupStrumHoldFiller, EntityType.PickupStrumEnd,
        };

        private static readonly (string Mode, Func<ShipMount, ShipVariant> Get)[] ShipForms =
        {
            ("List", m => m.ShipList), ("Bounce", m => m.ShipThumper), ("Freefly", m => m.ShipFreefly),
        };

        private bool _pickupsTried, _shipTried;

        private bool WantsPickups => !_pickupsTried && ((JObject)_manifest["models"]).Count == 0;

        private bool WantsShip(out Ship ship)
        {
            ship = null;
            if (_shipTried || !ShipHandler.IsLoaded) return false;
            try { ship = ShipHandler.SelectedShip; }
            catch (Exception) { return false; }
            return ship != null && ship.Mount != null && _manifest["ships"][ship.shipId] == null;
        }

        private IEnumerator ExportPickups()
        {
            _running = _pickupsTried = true;
            var database = Resources.FindObjectsOfTypeAll<EntityDatabase>().FirstOrDefault(d => d.Pickups != null && d.Pickups.Length > 0);
            if (database == null)
            {
                _pickupsTried = false; // not loaded yet: next results screen
                _running = false;
                yield break;
            }
            // one variant per type, mode and look, the keyboard one when there is a choice
            var chosen = database.Pickups.Where(p => p != null && ModelTypes.Contains(p.entityType))
                .GroupBy(p => (p.entityType, p.mode, p.CustomId))
                .Select(g => g.FirstOrDefault(p => ((int)p.controlType & (int)ControlType.Keyboard) != 0) ?? g.First())
                .ToList();
            var models = (JObject)_manifest["models"];
            Directory.CreateDirectory(Path.Combine(Dir, "models"));
            using (var studio = new Studio())
            {
                foreach (var prefab in chosen)
                {
                    string name = prefab.entityType + "." + prefab.mode + (prefab.CustomId != 0 ? "." + prefab.CustomId : "");
                    if (models[name] != null) continue;
                    Pickup pickup = null;
                    try
                    {
                        pickup = Instantiate(prefab, studio.Root);
                        pickup.pos = new TrackEntity { type = prefab.entityType, modeType = prefab.mode, lane = 1 };
                        Prepare(pickup);
                        studio.Place(pickup.gameObject);
                    }
                    catch (Exception e)
                    {
                        CustomSongsPlugin.Log.LogDebug($"Editor skin: {name}: {e.Message}");
                    }
                    if (pickup == null) continue;
                    for (int i = 0; i < 6; i++) yield return null; // its animators settle on their first pose
                    try
                    {
                        var shot = studio.Shoot(pickup.gameObject, 20f);
                        if (shot != null)
                        {
                            string file = "models/" + Safe(name) + ".png";
                            shot.Save(Path.Combine(Dir, file));
                            var entry = shot.Describe(file);
                            entry["type"] = prefab.entityType.ToString();
                            entry["mode"] = prefab.mode.ToString();
                            models[name] = entry;
                        }
                    }
                    catch (Exception e)
                    {
                        CustomSongsPlugin.Log.LogDebug($"Editor skin: {name}: {e.Message}");
                    }
                    Destroy(pickup.gameObject);
                    yield return null;
                }
            }
            SaveManifest();
            CustomSongsPlugin.Log.LogInfo($"Editor skin: {models.Count} pickup model(s) for the chart editor");
            _running = false;
        }

        /// <summary>What the game does when it spawns a pickup, minus what needs a player: keyboard icons, turn shape.</summary>
        private static void Prepare(Pickup pickup)
        {
            try { pickup.UpdateIcon(ControlType.Keyboard, forceSpawnControlType: true); }
            catch (Exception) { }
            try { pickup.stateHandler?.Setup(); }
            catch (Exception) { }
            if (Pickup.IsTurnType(pickup.entityType) && pickup.stateHandler is PickupStatesTurn turn)
            {
                try { turn.SetTurnMode(pickup.mode == ModeType.Bounce ? PickupStatesTurn.TurnMode.Bounce : PickupStatesTurn.TurnMode.Toblerone); }
                catch (Exception) { }
            }
        }

        private IEnumerator ExportShip(Ship ship)
        {
            _running = _shipTried = true;
            var entry = new JObject();
            Directory.CreateDirectory(Path.Combine(Dir, "ships"));
            ShipMount mount = null;
            using (var studio = new Studio())
            {
                try
                {
                    mount = Instantiate(ship.Mount, studio.Root);
                    studio.Place(mount.gameObject);
                }
                catch (Exception e)
                {
                    CustomSongsPlugin.Log.LogDebug("Editor skin ship: " + e.Message);
                }
                if (mount != null)
                {
                    foreach (var (mode, get) in ShipForms)
                    {
                        ShipVariant variant = null;
                        try
                        {
                            foreach (var (_, other) in ShipForms) other(mount)?.gameObject.SetActive(false);
                            variant = get(mount);
                            variant?.TransformIn();
                            if (variant != null) studio.Place(variant.gameObject, keepTransform: true);
                        }
                        catch (Exception e) { CustomSongsPlugin.Log.LogDebug($"Editor skin ship {mode}: {e.Message}"); }
                        if (variant == null) continue;
                        for (int i = 0; i < 20; i++) yield return null;
                        foreach (var (view, pitch, yaw) in new[] { ("back", 14f, 0f), ("side", 22f, -62f) })
                        {
                            try
                            {
                                var shot = studio.Shoot(variant.gameObject, pitch, yaw);
                                if (shot == null) continue;
                                string file = $"ships/{Safe(ship.shipId)}-{mode}-{view}.png";
                                shot.Save(Path.Combine(Dir, file));
                                entry[mode + "-" + view] = shot.Describe(file);
                            }
                            catch (Exception e) { CustomSongsPlugin.Log.LogDebug($"Editor skin ship {mode}: {e.Message}"); }
                        }
                    }
                    Destroy(mount.gameObject);
                }
            }
            if (entry.Count > 0)
            {
                _manifest["ships"][ship.shipId] = entry;
                _manifest["ship"] = ship.shipId;
                SaveManifest();
                CustomSongsPlugin.Log.LogInfo($"Editor skin: ship {ship.shipId} for the chart editor");
            }
            _running = false;
        }

        /// <summary>A picture of one model with transparency, cropped, and how big it is in the world.</summary>
        private sealed class Shot
        {
            public Texture2D Image;
            public float UnitsPerPixel;
            public Vector2 Pivot; // the model's origin, in pixels from the top left

            public void Save(string path)
            {
                File.WriteAllBytes(path, Image.EncodeToPNG());
                UnityEngine.Object.Destroy(Image);
            }

            public JObject Describe(string file) => new JObject
            {
                ["file"] = file, ["w"] = Image.width, ["h"] = Image.height,
                ["upp"] = Math.Round(UnitsPerPixel, 6), ["pivot"] = new JArray(Math.Round(Pivot.x, 1), Math.Round(Pivot.y, 1)),
            };
        }

        /// <summary>A camera and a light far below everything, that see one layer nobody else uses.</summary>
        private sealed class Studio : IDisposable
        {
            private readonly GameObject _root;
            private readonly Camera _camera;
            private readonly Light _light;
            private readonly int _layer;

            public Transform Root => _root.transform;

            public Studio()
            {
                _layer = FreeLayer();
                _root = new GameObject("InvectorModKit editor studio");
                _root.transform.position = new Vector3(0, -20000, 0);
                var eye = new GameObject("camera");
                eye.transform.SetParent(_root.transform, false);
                _camera = eye.AddComponent<Camera>();
                _camera.enabled = false;
                _camera.cullingMask = 1 << _layer;
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.orthographic = true;
                _camera.allowHDR = false;
                _camera.useOcclusionCulling = false;
                var sun = new GameObject("light");
                sun.transform.SetParent(_root.transform, false);
                sun.transform.rotation = Quaternion.Euler(38, -32, 0);
                _light = sun.AddComponent<Light>();
                _light.type = LightType.Directional;
                _light.cullingMask = 1 << _layer;
                _light.intensity = 1.15f;
                _light.shadows = LightShadows.None;
                _light.enabled = false; // only while shooting: the scene's sky must not take it for its sun
            }

            private static int FreeLayer()
            {
                for (int i = 31; i >= 8; i--)
                    if (string.IsNullOrEmpty(LayerMask.LayerToName(i))) return i;
                return 31;
            }

            public void Place(GameObject model, bool keepTransform = false)
            {
                if (!keepTransform)
                {
                    model.transform.localPosition = Vector3.zero;
                    model.transform.localRotation = Quaternion.identity;
                }
                foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = _layer;
                foreach (var a in model.GetComponentsInChildren<Animator>(true)) a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }

            /// <summary>From behind and above (pitch, in degrees) like the game's camera, turned by yaw.</summary>
            public Shot Shoot(GameObject model, float pitch, float yaw = 0f)
            {
                var renderers = model.GetComponentsInChildren<Renderer>()
                    .Where(r => r.enabled && r.gameObject.activeInHierarchy && !(r is ParticleSystemRenderer) && !(r is TrailRenderer) && !(r is LineRenderer))
                    .ToList();
                if (renderers.Count == 0) return null;
                var bounds = renderers[0].bounds;
                foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
                float radius = Mathf.Max(bounds.extents.magnitude, 0.01f);
                _camera.transform.rotation = Quaternion.Euler(pitch, yaw, 0);
                _camera.transform.position = bounds.center - _camera.transform.forward * radius * 3f;
                _camera.orthographicSize = radius * 1.04f;
                _camera.nearClipPlane = radius * 0.5f;
                _camera.farClipPlane = radius * 6f;

                var rt = RenderTexture.GetTemporary(ShotSize, ShotSize, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB, 4);
                var onBlack = Take(rt, Color.black);
                var onWhite = Take(rt, Color.white);
                RenderTexture.ReleaseTemporary(rt);

                // alpha from how much the background shows through; colour un-premultiplied
                var pixels = new Color32[onBlack.Length];
                int minX = ShotSize, minY = ShotSize, maxX = -1, maxY = -1;
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color32 b = onBlack[i], w = onWhite[i];
                    float through = ((w.r - b.r) + (w.g - b.g) + (w.b - b.b)) / (3f * 255f);
                    float a = Mathf.Clamp01(1f - through);
                    if (a < 0.01f) continue;
                    pixels[i] = new Color32((byte)Mathf.Min(255, b.r / a), (byte)Mathf.Min(255, b.g / a), (byte)Mathf.Min(255, b.b / a), (byte)(a * 255));
                    int x = i % ShotSize, y = i / ShotSize;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
                if (maxX < 0) return null;
                minX = Math.Max(0, minX - 2); minY = Math.Max(0, minY - 2);
                maxX = Math.Min(ShotSize - 1, maxX + 2); maxY = Math.Min(ShotSize - 1, maxY + 2);
                int w2 = maxX - minX + 1, h2 = maxY - minY + 1;
                var crop = new Color32[w2 * h2];
                for (int y = 0; y < h2; y++) Array.Copy(pixels, (minY + y) * ShotSize + minX, crop, y * w2, w2);
                var image = new Texture2D(w2, h2, TextureFormat.RGBA32, false, false);
                image.SetPixels32(crop);
                image.Apply();
                var origin = _camera.WorldToViewportPoint(model.transform.position);
                return new Shot
                {
                    Image = image,
                    UnitsPerPixel = 2f * _camera.orthographicSize / ShotSize,
                    Pivot = new Vector2(origin.x * ShotSize - minX, h2 - (origin.y * ShotSize - minY)),
                };
            }

            private Color32[] Take(RenderTexture rt, Color background)
            {
                _camera.backgroundColor = new Color(background.r, background.g, background.b, 0f);
                _camera.targetTexture = rt;
                _light.enabled = true;
                try { _camera.Render(); }
                finally
                {
                    _light.enabled = false;
                    _camera.targetTexture = null;
                }
                var previous = RenderTexture.active;
                RenderTexture.active = rt;
                var read = new Texture2D(ShotSize, ShotSize, TextureFormat.RGBA32, false, false);
                read.ReadPixels(new Rect(0, 0, ShotSize, ShotSize), 0, 0);
                read.Apply();
                RenderTexture.active = previous;
                var pixels = read.GetPixels32();
                UnityEngine.Object.Destroy(read);
                return pixels;
            }

            public void Dispose() => UnityEngine.Object.Destroy(_root);
        }
    }
}

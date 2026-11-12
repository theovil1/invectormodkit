# Level editor: reading the game's data for the 3D preview (part 1 of 3)

Date: 2026-11-12. Status: design approved section by section in chat, spec waiting for the owner's review.

## Context

The level editor's 3D preview (`tools/ChartEditor/ui/preview3d.js`) is a hand-drawn stand-in that looks little like
the game. A throwaway prototype rebuilt the game's view from the game's own data and code: camera rig, ship
steering and barrel rolls in Freefly, the real models and textures, particle systems emulated from their modules,
trails, hold walls, the pickups' level halo and the level paths with their bends. It matches recorded game frames
closely. The prototype read the game with Python scripts on one PC; players have no such scripts.

Bringing it into the editor is split into three parts, each with its own spec, plan and implementation:

1. **Game data (this spec):** the editor reads, on the player's PC, what the preview needs from the installed game.
2. **The new preview:** a real-time (60 fps) three.js renderer built from the prototype, fed by part 1.
3. **Preview settings in the editor:** faithful or calm effects, decor, world, score tier, XBOX or PlayStation look.

## Goal

The editor holds an up-to-date local copy of the game data the new preview needs, without running the game, without
writing into the game's folder, and without anything from the game in the repository.

Success criteria:

- On a PC with the game installed, the first start of the editor builds the cache in the background while the
  editor stays usable, and the status bar says so; later starts reuse it at once.
- After a game update (the game's Addressables catalog changes) or a new recipe version, the cache is rebuilt.
- The integration test, run on the owner's PC, finds every recipe item and matches the values measured during the
  prototype (listed under Testing).

## Owner's decisions

| Topic | Decision |
|---|---|
| Order of the three parts | Game data first, then the preview, then the settings |
| How the game is read | The editor reads the bundles itself with AssetsTools.NET (NuGet, MIT, no dependency) |
| Ship | The Beginner ship only |
| Decor | Track paths only (bends, climbs, banking, Freefly areas) for the 10 worlds; the bright prototype "lab" decor stays around the track; no game world decor |
| Where | A local cache outside the game's folder; nothing from the game in the repository |

## Non-goals

- Rendering anything: part 2. Part 1 only adds a status message to the interface.
- The shader formulas (Sonic Ether additive and emissive gains, hold wall shader): they were read from the
  compiled shaders during the prototype and are written into part 2's code; they are not extracted.
- Other ships, world decor, songs, audio.
- Crunched textures (formats 28, 29), if any recipe item turns out to use one: the integration test reports it and
  the item falls back (see Errors); supporting them is a later decision.

## Architecture

```
 InvectorLevelEditor.exe (net48)                         Game folder (read only)
 ┌───────────────────────────────────────────────┐      ┌───────────────────────────────────────┐
 │ MainForm: at start, if the cache is missing or │ read │ ...\StreamingAssets\aa\catalog.json      │
 │ stale, runs GameData.Build on a background     │─────>│ ...\StandaloneWindows64\*.bundle         │
 │ thread, posts status messages to the UI;       │      └───────────────────────────────────────┘
 │ serves the cache to the UI under /game/        │
 │                                                │ write  %LOCALAPPDATA%\InvectorModKit\LevelEditor\game\
 │ InvectorModKit.ChartEditor.Core (netstandard)  │─────>  index.json, objects\, meshes\, textures\,
 │   GameData: recipe, reader, writers, stamp     │        clips\, levels\, settings.json
 └───────────────────────────────────────────────┘
```

New code, in `src/InvectorModKit.ChartEditor.Core/GameData/` (tested), with the host wiring in `MainForm.cs`:

| Unit | Does | Depends on |
|---|---|---|
| `GameDataRecipe` | The list of what to read: bundle file, object name or component class, kind | nothing |
| `GameDataStamp` | The cache's freshness: SHA-256 of `aa\catalog.json` plus `RecipeVersion` | file system |
| `BundleReader` | Opens a bundle and its dependencies with AssetsTools.NET, finds objects by name or class, walks references | AssetsTools.NET |
| `TypeTreeJson` | Turns a typetree value (component fields, ScriptableObjects) into JSON; object references become cache ids | AssetsTools.NET value fields |
| `MeshDecoder` | Unity 2021 mesh (vertex channels and streams, 16 or 32 bit indices, submeshes, external stream data) to arrays | nothing (raw bytes in) |
| `ClipDecoder` | AnimationClip muscle data (streamed, dense and constant curves, generic bindings) to sampled tracks | nothing (raw values in) |
| `TextureWriter` | Texture2D raw data (mip chain, from the bundle or its `.resS`) to a cache file, unchanged | nothing |
| `GameDataCache` | Builds into a temporary folder, writes `index.json`, swaps it with the live folder in one move | file system |
| `GameData.Build` | Runs the recipe, collects warnings, reports progress | all of the above |

The host calls `GameData.Build(gameDir, cacheDir, progress)` on a background thread when `GameDataStamp` says the
cache is missing or stale, then posts one of three status messages. `Serve` maps `/game/<path>` to the cache folder.
The interface only shows the message in part 1.

## What is read (the recipe)

Bundles are under `Invector Rhythm Galaxy_Data\StreamingAssets\aa\StandaloneWindows64\`.

| Bundle | Objects |
|---|---|
| `basegame_assets_shared.bundle` | Pickups: `XBOX Pickup Left X/Right B/Down A/Up Y OPTIMIZED`, `Keyboard PS4 Pickup Left/Right/Up/Down OPTIMIZED`; turns `Pickup 4 Left OPTIMIZED`, `Pickup 5 Right OPTIMIZED`; strums `Pickup Strum, List/Bounce OPTIMIZED`; holds `Pickup StrumHold Start, List/Bounce OPTIMIZED`, `PickupStrumHoldEnd, List/Bounce, All Platforms`; beat lines `BeatLine, List`, `BeatLine, Bounce`; Freefly gate `PickupAir, All Platforms OPTIMIZED`. Clips: `RollRightLong`, `coaster@RollRightLong2Turns`, `coaster@RollRightLong3Turns`, `Airpickup Spawn Legacy`, `Airpickup ActivateEffect Legacy`; the `Coaster Animator` controller (roll states and their RandomAnimation conditions) |
| `basegame_assets_ship_beginner.bundle` | `Ship Beginner` (model, trails, `ShipPickupStreakSettings`, `ShipRecolorororerer`) |
| `basegame_assets_levelmodule.bundle` | `PlayerWalkerGlobal` (Walker, CameraController, Steering, PlacePickupsOnPath, PickupEffectTrigger, GameplayEffectsTrigger, the PickupParticles, StrumParticles and PickupParticlesLand systems); ScriptableObjects `MaterialsDatabase`, `ShipColorScheme`, the `GeneratorFreefly` and spawn settings |
| `basegame_scenes_level<world>.bundle`, world in city, darkvalley, desert, ice, inferno, oblivion, ruins, space, valley, void | Components `Spline`, `SplineEditor` (fly areas), `SplineMesh` (road mesh, material, scale), `Toblerone`, `TobleroneWalls` (both, with the hold wall material) |

Everything an object references is read with it, through a reference walk limited to these types: GameObject,
Transform, MeshFilter, MeshRenderer, SkinnedMeshRenderer, SpriteRenderer, ParticleSystem, ParticleSystemRenderer,
TrailRenderer, Animator, MonoBehaviour (with its script class name), Mesh, Material (with its shader's name), Texture2D,
Sprite. Shaders themselves are not read.

## Cache layout and formats

```
game\
  index.json          stamp (catalog SHA-256, RecipeVersion), date, items found, warnings
  objects\<name>.json one per prefab: tree of {name, active, position, rotation, scale, components[], children[]}
  meshes\<id>.bin     JSON header line + little-endian arrays
  textures\<id>.tex   JSON header line + raw texture bytes as stored by the game
  clips\<name>.json   sampled tracks
  levels\<world>.json path, fly areas, road, tunnel and hold wall settings
  settings.json       the levelmodule ScriptableObjects and the walker's components, by class name
```

- **Ids:** `<bundle short name>-<path id>`, stable for a given game version; references in JSON are `{"ref": id}`.
  File names (`<name>`, `<world>`) are the object or world name made file-safe (letters, digits, `-`, `_`).
- **Components:** `{"type": "ParticleSystem", "fields": {...}}`, or for scripts `{"type": "MonoBehaviour", "class":
  "PickupStatesPickup", "fields": {...}}`. Fields keep Unity's names and nesting. Byte arrays over 4 KB are left out
  (marked `"omitted": n`), as they are mesh or texture data already written elsewhere.
- **Materials:** `{"name", "shader", "colors": {...}, "floats": {...}, "textures": {"_MainTex": {"ref", "scale",
  "offset"}}}`, inlined where referenced.
- **Meshes:** header `{"vertexCount", "attributes": {"position": 3, "normal": 3, "uv0": 2, "color": 4, ...}, "index":
  "u16"|"u32", "submeshes": [[start, count]], "bounds"}`; data as Float32 per attribute then the indices. Positions
  stay in Unity's left-handed axes; part 2 converts them.
- **Textures:** header `{"format": Unity TextureFormat number, "width", "height", "mips", "colorSpace": 0|1,
  "filter", "wrap", "sprite"?: {"rect", "pivot", "pixelsPerUnit"}}`, then the bytes. Part 2 uploads them as WebGL
  compressed textures (S3TC for DXT1 and DXT5, BPTC for BC6H and BC7) or plain ones (RGBA32, RGB24, Alpha8).
- **Clips:** `{"length", "fps": 60, "tracks": [{"path", "attribute", "values": [...]}]}`; transform paths and
  material property names are resolved from their CRC32 hashes against the clip's prefab, unknown ones kept as
  numbers.
- **Levels:** `{"start", "length", "pos", "rot", "acc", "fly", "circle", "road": {"mesh", "material", "scale"},
  "tunnel": {...}, "holdWalls": {...}}`.

Expected size: a few megabytes.

## Freshness, errors and messages

- **Stamp:** the cache is rebuilt when `index.json` is missing, its catalog SHA-256 differs from the game's current
  `catalog.json`, or its `RecipeVersion` is older than the code's.
- **Safe reading:** game files are opened read only with sharing, so the game may be running. The new cache is
  built in `game.tmp\` and swapped with `game\` in one move; an interrupted build never leaves a half-written cache.
- **Missing item** (renamed by a game update, say): written to `index.json` warnings; part 2 draws a simple shape in
  its place; the rest is unaffected.
- **Failure or no game found:** the previous cache stays if there is one, otherwise none; the preview stays the
  current one; the status bar says so; details go to the editor's log.
- **Messages** (status bar, `ui/i18n.js`, all 11 languages, no em dash):
  - `gameDataReading`: "Reading the game's files for the 3D preview..."
  - `gameDataReady`: "3D preview ready"
  - `gameDataFailed`: "Couldn't read the game's files: the 3D preview stays simple"

## Testing

Test first, as the repository asks. In `tests/InvectorModKit.ChartEditor.Tests` (xUnit, net8.0, runs in CI):

- `MeshDecoder`: synthetic vertex buffers with several channel formats (float32, float16, unorm8), two streams,
  16 and 32 bit indices, submeshes.
- `ClipDecoder`: a hand-built streamed clip (frames of cubic keys), a dense clip and constant curves; sampling at
  known times; CRC32 path and attribute resolution.
- `TypeTreeJson`: nested fields, arrays, strings, object references to ids, omitted byte arrays.
- `GameDataStamp`: fresh, stale catalog, older recipe.
- `GameDataCache`: build into the temporary folder, swap, interrupted build leaves the old cache.
- `GameDataRecipe`: every entry has a bundle and a name or class.
- The existing i18n test checks the three new rows.

Integration test, skipped unless the game is installed (its folder found as the editor finds it): runs the whole
build into a temporary folder and checks, against the prototype's measurements:

- every recipe item found, no warning;
- fly areas: 17 in void, 27 in space; void path length 2562.75 from start 210, space 4391.99 from 1400;
- roll clips 1.567, 1.933 and 2.6 s long; three turn variants;
- Beginner tier-4 streak trails: TrailBig time .1, width .2 to .1; TrailWide 0.06 s, 0.3 wide, upright;
- `MaterialsDatabase`: XBOX left maps to the blue particle material, PlayStation left to the pink one;
- texture formats of the recipe's textures are all supported (reports any crunched one);
- build time logged (target: under a minute on the owner's PC).

## Dependencies and rules

- `AssetsTools.NET` 3.0.5 (MIT, netstandard2.0, no dependency) in `InvectorModKit.ChartEditor.Core`: approved by the
  owner for this purpose.
- Nothing from the game is committed: tests use synthetic data or read the installed game at run time.
- No change to the game's folder, the player's save, the chart format or the workflows.

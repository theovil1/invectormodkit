# Level editor game data (part 1 of 3) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The level editor keeps an up-to-date local copy of the game data the future 3D preview needs, read from the installed game with AssetsTools.NET, without running the game, writing into its folder, or committing anything from it.

**Architecture:** Pure, unit-tested decoders and writers (meshes, animation clips, textures, typetree to JSON, cache swap, freshness stamp) in `InvectorModKit.ChartEditor.Core/GameData/`, driven by a `GameDataBuilder` that walks a fixed recipe through `BundleReader` (AssetsTools.NET). A `GameDataService` decides when to rebuild and reports a state; the WinForms host runs it in the background, serves the cache under `/game/` and posts the state to the page, which shows it in the status bar.

**Tech Stack:** C# netstandard2.0 (Core) and net48 (host), AssetsTools.NET 3.0.5, Newtonsoft.Json 12.0.3, xUnit 2.5.3 on net8.0, ES modules + `node --test` for the interface.

**Spec:** `docs/superpowers/specs/2026-11-12-editor-game-data-design.md` (read it with this plan).

## Global Constraints

- `AssetsTools.NET` 3.0.5 is the only new package (approved); no other NuGet or npm package, no workflow change.
- Nothing from the game is committed: unit tests use synthetic data; the integration test reads the installed game at run time and is skipped when it is not found.
- No write into the game's folder, the player's save, or the chart format; game files are opened read only with `FileShare.ReadWrite | FileShare.Delete`.
- Cache folder: `%LOCALAPPDATA%\InvectorModKit\LevelEditor\game\` (built in `game.tmp\`, swapped in).
- Ids are `<bundle short name>-<path id>`; JSON references are `{"ref": id}`; file names are made of letters, digits, `-` and `_`.
- Status texts (keys `gameDataReading`, `gameDataReady`, `gameDataFailed`) in all 11 languages of `ui/i18n.js`.
- Never use em dashes in any text (code comments, UI, docs, commits).
- Commits: Conventional Commits in English, scope `editor`; no `Co-Authored-By`, no "generated with", no mention of an AI tool.
- All new C# types live in namespace `InvectorModKit.ChartEditor` (the folder `GameData/` only groups files), like the rest of Core.

### Spec refinements decided by this plan

These settle details the spec leaves open; implement them as written here.

1. Mesh `.bin` and texture `.tex` headers are one line of JSON padded with spaces so that the header including its `\n` is a multiple of 4 bytes (the binary part can be viewed as `Float32Array`/`Uint32Array` without copying).
2. Sprites are inlined where they are referenced (`{"id", "name", "texture": {"ref"}, "rect", "pivot", "pixelsPerUnit"}`); texture files carry no sprite block, so a texture shared by several sprites is written once.
3. Texture header `"wrap"` is `[u, v]`.
4. Clip tracks are `{"path", "type", "attribute", "size", "values"}`, values flattened frame after frame (`size` numbers per frame).
5. The cache is stale when its `recipeVersion` differs from the code's (not only when older), so an older editor never reads a newer format.
6. The editor has no log yet: details go to `%LOCALAPPDATA%\InvectorModKit\LevelEditor\game-data.log`, rewritten by each build.
7. `index.json` lists every recipe item with the file it was written to; part 2 looks files up there instead of rebuilding names.
8. JSON never holds `NaN` or `Infinity` (written as `null`) and integers beyond 2^53 are written as strings (path ids are 64-bit).
9. Two editors cannot build at once: a lock file `game.lock`; the second one keeps the current cache.
10. The build also starts when the game folder is set later in the settings.
11. `gameDataReady` is shown only after `gameDataReading` in the same session; a fresh cache at start shows nothing.

## Review Focus

1. **The game is running, or Steam is updating it, while the editor reads:** files open read only with sharing; any failure keeps the previous cache. Pinned by `BundleReaderTests.OpensAFileAnotherProcessHoldsForWriting` (Task 7) and `GameDataCacheTests.FailedBuildKeepsTheOldCache` (Task 6).
2. **Two editors started together:** one builds, the other keeps the current cache without an error. Pinned by `GameDataCacheTests.SecondBuilderIsBusy` (Task 6) and `GameDataServiceTests.BusyKeepsReadyWhenACacheExists` (Task 10).
3. **The PC stops between the two folder moves of the swap:** the next start restores the old cache. Pinned by `GameDataCacheTests.RecoverRestoresTheOldCacheAfterACrashMidSwap` (Task 6).
4. **Values the browser cannot parse** (`NaN`, `Infinity`, 64-bit path ids rounded by JavaScript): pinned by `TypeTreeJsonTests.NonFiniteFloatsBecomeNull` and `TypeTreeJsonTests.LargeIntegersBecomeStrings` (Task 5), and the integration check that every JSON file parses strictly (Task 8).
5. **A damaged `index.json`** (truncated by a disk error, edited by hand): treated as stale and rebuilt, never a crash. Pinned by `GameDataStampTests.CorruptIndexIsStale` (Task 1).

---

## File Structure

| File | Responsibility |
|---|---|
| `src/InvectorModKit.ChartEditor.Core/GameData/GameDataRecipe.cs` | What to read: items (bundle, kind, name or class), worlds, level classes, `Version` |
| `.../GameData/GameDataStamp.cs` | Game paths, catalog SHA-256, new index object, freshness check |
| `.../GameData/CacheFiles.cs` | File-safe names, padded header line write and read |
| `.../GameData/TextureWriter.cs` | Texture data to a `.tex` file; supported and crunched formats |
| `.../GameData/Crc32.cs` | CRC-32 (IEEE) of UTF-8 strings, for clip bindings |
| `.../GameData/MeshDecoder.cs` | Unity 2021 vertex and index buffers to float arrays; `.bin` writer |
| `.../GameData/ClipDecoder.cs` | Muscle clip curves (streamed, dense, constant) sampled at 60 fps to JSON |
| `.../GameData/TypeTreeJson.cs` | AssetsTools value fields to JSON |
| `.../GameData/GameDataCache.cs` | Lock, temporary folder, swap, recovery |
| `.../GameData/BundleReader.cs` | AssetsTools.NET access: CAB index, open, find, resolve, resources, script and shader names |
| `.../GameData/PrefabWriter.cs` | A prefab's tree, its components and everything they reference, as one JSON object plus mesh and texture files |
| `.../GameData/LevelJson.cs` | A world's level JSON from its components |
| `.../GameData/GameDataBuilder.cs` | Runs the recipe into a folder, writes `index.json`, collects warnings |
| `.../GameData/GameData.cs` | `GameData.Build` (builder inside the cache swap) and `GameDataReport` |
| `.../GameData/GameDataService.cs` | When to build, state, log; used by the host |
| `tools/ChartEditor/MainForm.cs` | Background refresh, `host.gameData`, `/game/` serving |
| `tools/ChartEditor/ui/app.js`, `ui/i18n.js` | Status messages |
| `tools/release/THIRD-PARTY-NOTICES.txt` | AssetsTools.NET licence line |
| `tests/InvectorModKit.ChartEditor.Tests/GameData/*.cs` | One test class per unit, `Fields.cs` (typetree builders), `GameInstall.cs` (`[GameFact]`, game folder), `GameDataBuildFixture.cs`, `GameDataIntegrationTests.cs` |

Test command for C# (from the worktree root): `dotnet test tests/InvectorModKit.ChartEditor.Tests --filter "FullyQualifiedName~<Class>"`.

---

### Task 1: Package, recipe and freshness stamp

**Files:**
- Modify: `src/InvectorModKit.ChartEditor.Core/InvectorModKit.ChartEditor.Core.csproj` (add `<PackageReference Include="AssetsTools.NET" Version="3.0.5" />`)
- Modify: `tools/release/THIRD-PARTY-NOTICES.txt` (under "Level editor only": `AssetsTools.NET, MIT (https://github.com/nesrak1/AssetsTools.NET)`)
- Create: `src/InvectorModKit.ChartEditor.Core/GameData/GameDataRecipe.cs`, `GameDataStamp.cs`
- Test: `tests/InvectorModKit.ChartEditor.Tests/GameData/GameDataRecipeTests.cs`, `GameDataStampTests.cs`

**Interfaces:**
- Produces:
  - `enum RecipeKind { Prefab, Clip, Controller, Settings, Level }`
  - `sealed class RecipeItem { string Bundle; RecipeKind Kind; string Name; string Class; string Key }` (`Name` for Prefab, Clip, Controller and Level (the world); `Class` for Settings; `Key` = `Kind + ":" + (Name ?? Class)`)
  - `static class GameDataRecipe { const int Version = 1; IReadOnlyList<RecipeItem> Items; IReadOnlyList<string> Worlds; IReadOnlyList<string> LevelClasses; IReadOnlyList<string> WalkerClasses }`
  - `static class GameDataStamp { string DataDir(string gameDir); string BundlesDir(string gameDir); string CatalogPath(string gameDir); string CatalogSha256(string gameDir); JObject NewIndex(string gameDir); bool IsFresh(string gameDir, string cacheDir) }`

Recipe content (exact strings, from the spec):
- `basegame_assets_shared.bundle`, Prefab: `XBOX Pickup Left X OPTIMIZED`, `XBOX Pickup Right B OPTIMIZED`, `XBOX Pickup Down A OPTIMIZED`, `XBOX Pickup Up Y OPTIMIZED`, `Keyboard PS4 Pickup Left OPTIMIZED`, `Keyboard PS4 Pickup Right OPTIMIZED`, `Keyboard PS4 Pickup Up OPTIMIZED`, `Keyboard PS4 Pickup Down OPTIMIZED`, `Pickup 4 Left OPTIMIZED`, `Pickup 5 Right OPTIMIZED`, `Pickup Strum, List/Bounce OPTIMIZED`, `Pickup StrumHold Start, List/Bounce OPTIMIZED`, `PickupStrumHoldEnd, List/Bounce, All Platforms`, `BeatLine, List`, `BeatLine, Bounce`, `PickupAir, All Platforms OPTIMIZED`; Clip: `RollRightLong`, `coaster@RollRightLong2Turns`, `coaster@RollRightLong3Turns`, `Airpickup Spawn Legacy`, `Airpickup ActivateEffect Legacy`; Controller: `Coaster Animator`.
- `basegame_assets_ship_beginner.bundle`, Prefab: `Ship Beginner`.
- `basegame_assets_levelmodule.bundle`, Prefab: `PlayerWalkerGlobal`; Settings: `MaterialsDatabase`, `ShipColorScheme`, `GeneratorFreefly`, `GeneratorSpawnSettings`.
- Level, one per world in `city, darkvalley, desert, ice, inferno, oblivion, ruins, space, valley, void`, bundle `basegame_scenes_level<world>.bundle`.
- `LevelClasses = Spline, SplineEditor, SplineMesh, Toblerone, TobleroneWalls`; `WalkerClasses = Walker, CameraController, Steering, PlacePickupsOnPath, PickupEffectTrigger, GameplayEffectsTrigger`.

- [ ] **Step 1: Write the failing tests**

```csharp
public class GameDataRecipeTests
{
    [Fact]
    public void EveryItemHasABundleAndANameOrClass()
    {
        foreach (var i in GameDataRecipe.Items)
        {
            Assert.EndsWith(".bundle", i.Bundle);
            Assert.False(string.IsNullOrEmpty(i.Kind == RecipeKind.Settings ? i.Class : i.Name), i.Key);
        }
        Assert.Equal(GameDataRecipe.Items.Count, GameDataRecipe.Items.Select(i => i.Key).Distinct().Count());
    }

    [Fact]
    public void TenWorldsEachWithTheirSceneBundle()
    {
        Assert.Equal(new[] { "city", "darkvalley", "desert", "ice", "inferno", "oblivion", "ruins", "space", "valley", "void" }, GameDataRecipe.Worlds);
        foreach (var w in GameDataRecipe.Worlds)
            Assert.Contains(GameDataRecipe.Items, i => i.Kind == RecipeKind.Level && i.Name == w && i.Bundle == $"basegame_scenes_level{w}.bundle");
    }

    [Theory]
    [InlineData(RecipeKind.Prefab, "Ship Beginner")]
    [InlineData(RecipeKind.Prefab, "PickupAir, All Platforms OPTIMIZED")]
    [InlineData(RecipeKind.Clip, "coaster@RollRightLong3Turns")]
    [InlineData(RecipeKind.Controller, "Coaster Animator")]
    [InlineData(RecipeKind.Settings, "MaterialsDatabase")]
    public void HoldsTheSpecItems(RecipeKind kind, string nameOrClass) =>
        Assert.Contains(GameDataRecipe.Items, i => i.Kind == kind && (i.Name ?? i.Class) == nameOrClass);
}
```

`GameDataStampTests` (a temporary fake game dir with `Invector Rhythm Galaxy_Data\StreamingAssets\aa\catalog.json` holding `"v1"`, and a cache dir; both deleted in `Dispose`):
- `MissingIndexIsStale`: `IsFresh` is false.
- `IndexOfTheSameCatalogIsFresh`: write `NewIndex(game)` to `cache\index.json`; `IsFresh` is true; `NewIndex` holds `catalogSha256` (64 lowercase hex chars), `recipeVersion` = `GameDataRecipe.Version` and `built`.
- `ChangedCatalogIsStale`: after writing the index, rewrite `catalog.json` with `"v2"`; false.
- `OtherRecipeVersionIsStale`: index with `recipeVersion` = `Version + 1`; false.
- `CorruptIndexIsStale`: `index.json` = `{"catalogSha2`; false, no exception.
- `MissingCatalogIsStale`: delete `catalog.json`; false, no exception.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/InvectorModKit.ChartEditor.Tests --filter "FullyQualifiedName~GameDataRecipeTests|FullyQualifiedName~GameDataStampTests"`
Expected: build errors (types not defined).

- [ ] **Step 3: Add the package and notices line, implement `GameDataRecipe` and `GameDataStamp`**

`DataDir` = `<game>\Invector Rhythm Galaxy_Data`; `BundlesDir` = `DataDir\StreamingAssets\aa\StandaloneWindows64`; `CatalogPath` = `DataDir\StreamingAssets\aa\catalog.json`. The SHA-256 reads the catalog with `FileShare.ReadWrite | FileShare.Delete`. `IsFresh` catches every exception and returns false.

- [ ] **Step 4: Run the tests to verify they pass**

Same command. Expected: all pass. Also run the whole test project once: no other test changes.

- [ ] **Step 5: Commit**

```bash
git add src/InvectorModKit.ChartEditor.Core tests/InvectorModKit.ChartEditor.Tests/GameData tools/release/THIRD-PARTY-NOTICES.txt
git commit -m "feat(editor): list the game data the 3D preview needs and when it is stale"
```

---

### Task 2: Cache file names, header lines and textures

**Files:**
- Create: `src/InvectorModKit.ChartEditor.Core/GameData/CacheFiles.cs`, `TextureWriter.cs`
- Test: `tests/InvectorModKit.ChartEditor.Tests/GameData/CacheFilesTests.cs`, `TextureWriterTests.cs`

**Interfaces:**
- Produces:
  - `static class CacheFiles { string FileSafe(string name); void WriteHeaderLine(Stream output, JObject header); JObject ReadHeaderLine(Stream input) }`
  - `sealed class TextureData { int Format, Width, Height, MipCount, ColorSpace, Filter, WrapU, WrapV; byte[] Bytes; }`
  - `static class TextureWriter { IReadOnlyCollection<int> Supported; bool IsCrunched(int format); void Write(TextureData texture, Stream output) }`

- [ ] **Step 1: Write the failing tests**

- `FileSafe`: `"Pickup Strum, List/Bounce OPTIMIZED"` gives `"Pickup_Strum__List_Bounce_OPTIMIZED"`; `"coaster@RollRightLong2Turns"` gives `"coaster_RollRightLong2Turns"`; `"void"` stays `"void"`.
- `HeaderLineIsPaddedToFourBytes`: for headers of several lengths (`{"a":1}`, `{"ab":1}`, `{"abc":1}`, `{"abcd":1}`), the bytes written end with `\n`, their count is a multiple of 4, and `ReadHeaderLine` on the same stream returns an equal `JObject` and leaves the stream position just after the `\n`.
- `TextureHeaderAndBytes`: `Write` a `TextureData { Format = 10, Width = 8, Height = 4, MipCount = 2, ColorSpace = 1, Filter = 1, WrapU = 0, WrapV = 1, Bytes = 40 bytes 0..39 }`; reading back the header gives `format` 10, `width` 8, `height` 4, `mips` 2, `colorSpace` 1, `filter` 1, `wrap` `[0, 1]`, and the rest of the stream equals `Bytes`.
- `SupportedAndCrunchedFormats`: `Supported` is exactly `{1, 3, 4, 10, 12, 24, 25}` (Alpha8, RGB24, RGBA32, DXT1, DXT5, BC6H, BC7); `IsCrunched` is true for 28, 29, 64, 65 and false for 10.

- [ ] **Step 2: Run to verify they fail** (`--filter "FullyQualifiedName~CacheFilesTests|FullyQualifiedName~TextureWriterTests"`; expected: build errors)

- [ ] **Step 3: Implement**

`FileSafe` replaces every character outside `[A-Za-z0-9_-]` by `_`. `WriteHeaderLine` writes compact UTF-8 JSON, then spaces, then `\n`, so the total is a multiple of 4. `ReadHeaderLine` reads bytes up to `\n` (no buffering past it).

- [ ] **Step 4: Run to verify they pass** (same filter; expected: pass)

- [ ] **Step 5: Commit** (`feat(editor): write cache files with an aligned JSON header`)

---

### Task 3: Mesh decoder

**Files:**
- Create: `src/InvectorModKit.ChartEditor.Core/GameData/Crc32.cs`, `MeshDecoder.cs`
- Test: `tests/InvectorModKit.ChartEditor.Tests/GameData/MeshDecoderTests.cs`

**Interfaces:**
- Consumes: `CacheFiles.WriteHeaderLine` (Task 2).
- Produces:
  - `static class Crc32 { uint Compute(string text) }` (IEEE, UTF-8 bytes)
  - `sealed class VertexChannel { int Stream, Offset, Format, Dimension; }`
  - `sealed class SubMeshData { int FirstByte, IndexCount, Topology, BaseVertex; }`
  - `sealed class MeshData { int VertexCount; List<VertexChannel> Channels /* 14 entries, Unity order */; byte[] VertexBytes; byte[] IndexBytes; bool Index32; List<SubMeshData> SubMeshes; float[] BoundsCenter, BoundsExtent; }`
  - `sealed class DecodedMesh { int VertexCount; List<KeyValuePair<string, float[]>> Attributes; Dictionary<string, int> Sizes; uint[] Indices; bool Index32; List<int[]> SubMeshes /* [start, count] */; float[] BoundsCenter, BoundsExtent; }`
  - `static class MeshDecoder { DecodedMesh Decode(MeshData mesh); float ReadComponent(int format, byte[] data, int offset); void Write(DecodedMesh mesh, Stream output) }`

Unity 2021 rules the decoder follows:
- Channel index to name: 0 `position`, 1 `normal`, 2 `tangent`, 3 `color`, 4 to 11 `uv0` to `uv7`; 12 and 13 (blend weights and indices) are skipped. A channel whose `Dimension & 0xF` is 0 is absent.
- Formats (size in bytes): 0 Float32 (4), 1 Float16 (2), 2 UNorm8 (1, /255), 3 SNorm8 (1, /127 clamped to -1), 4 UNorm16 (2, /65535), 5 SNorm16 (2, /32767 clamped to -1), 6 UInt8, 7 SInt8, 8 UInt16, 9 SInt16, 10 UInt32, 11 SInt32 (integers as plain floats).
- Stream `s` stride = sum of `size(format) * dimension` over its channels; streams follow each other, each start rounded up to 16 bytes: `start[s] = align16(start[s-1] + stride[s-1] * VertexCount)`. A component is at `start[stream] + v * stride[stream] + channel.Offset + k * size`.
- Indices are little endian u16 or u32; a submesh's indices start at `FirstByte / indexSize`, and its `BaseVertex` is added to each of them. `Index32` in the result is true if the source was 32 bit or any index exceeds 65535. A topology other than 0 (triangles) throws `NotSupportedException` (the builder turns it into a warning).
- `Write`: header `{"vertexCount", "attributes": {name: size, ...} in channel order, "index": "u16"|"u32", "submeshes": [[start, count]], "bounds": {"center": [3], "extent": [3]}}` through `CacheFiles.WriteHeaderLine`, then each attribute as little-endian Float32, then all indices as u16 or u32.

- [ ] **Step 1: Write the failing tests**

- `Crc32OfTheCheckString`: `Crc32.Compute("123456789") == 0xCBF43926u`.
- `TwoStreamsWithFloatHalfAndUnorm8`: 3 vertices; stream 0: position Float32 x3 at offset 0, normal Float16 x3 at offset 12 (stride 18, 54 bytes); stream 1 starts at byte 64: color UNorm8 x4 at offset 0, uv0 Float32 x2 at offset 4 (stride 12). Positions (1,2,3), (4,5,6), (7,8,9); every normal is halves `0x3C00, 0x3800, 0xC000` = (1, 0.5, -2); every color bytes (255, 0, 51, 255) = (1, 0, 0.2, 1) within 1e-6; every uv (0.25, 0.75). Asserts: attribute order `position, normal, color, uv0`; sizes 3, 3, 4, 2; the decoded arrays equal those values.
- `SixteenBitIndicesAndTwoSubmeshes`: index bytes for u16 `[0,1,2, 2,1,0]`, submeshes (FirstByte 0, count 3) and (FirstByte 6, count 3); result `Indices` `[0,1,2,2,1,0]`, `SubMeshes` `[[0,3],[3,3]]`, `Index32` false.
- `ThirtyTwoBitIndicesWithBaseVertex`: 4 vertices (position only), u32 `[0,1,2]`, one submesh with `BaseVertex` 1; `Indices` `[1,2,3]`, `Index32` true.
- `NormalizedFormats`: `ReadComponent(3, {0x80}, 0) == -1`, `ReadComponent(3, {0x7F}, 0) == 1`, `ReadComponent(4, {0xFF,0xFF}, 0) == 1`, `ReadComponent(5, {0x00,0x80}, 0) == -1`.
- `WritesHeaderThenFloatsThenIndices`: `Write` the two-stream mesh with its u16 indices; the header line length is a multiple of 4, its `attributes` is `{"position":3,"normal":3,"color":4,"uv0":2}` in that order, `index` is `"u16"`, and the remaining byte count is `3 * 12 * 4 + 6 * 2` = 156.

- [ ] **Step 2: Run to verify they fail** (`--filter "FullyQualifiedName~MeshDecoderTests"`)

- [ ] **Step 3: Implement `Crc32` and `MeshDecoder`** (half floats by hand: netstandard2.0 has no `System.Half`)

- [ ] **Step 4: Run to verify they pass**

- [ ] **Step 5: Commit** (`feat(editor): decode the game's mesh buffers`)

---

### Task 4: Animation clip decoder

**Files:**
- Create: `src/InvectorModKit.ChartEditor.Core/GameData/ClipDecoder.cs`
- Test: `tests/InvectorModKit.ChartEditor.Tests/GameData/ClipDecoderTests.cs`

**Interfaces:**
- Consumes: `Crc32.Compute` (Task 3).
- Produces:
  - `sealed class ClipBinding { uint Path; uint Attribute; int TypeId; bool IsPPtr; }`
  - `sealed class MuscleClipData { float StartTime, StopTime; uint[] Streamed; int StreamedCurveCount; float[] Dense; int DenseCurveCount, DenseFrameCount; float DenseSampleRate, DenseBeginTime; float[] Constant; List<ClipBinding> Bindings; }`
  - `static class ClipDecoder { int CurveCount(ClipBinding binding); float[] Sample(MuscleClipData clip, float time); JObject ToJson(string name, MuscleClipData clip, IEnumerable<string> paths, IEnumerable<string> attributes, int fps = 60) }`

Rules:
- Curves are numbered streamed first, then dense, then constant; bindings take them in order. `CurveCount`: for `TypeId` 4 (Transform), attribute 1 position 3, 2 rotation 4, 3 scale 3, 4 euler 3; every other binding 1.
- Streamed data (the `uint[]` read as bytes): frames of `{float time; uint keyCount; keyCount x {uint curve; float c0, c1, c2, c3}}`. A curve at time `t` uses its last key at or before `t`: `d = t - keyTime`, value `((c0*d + c1)*d + c2)*d + c3`; before its first key it takes that key's `c3`. Frames with a non-finite time hold no usable key and are skipped.
- Dense: sample `i` of curve `c` is `Dense[i * DenseCurveCount + c]` at `DenseBeginTime + i / DenseSampleRate`, linear between samples, clamped at both ends.
- Constant: `Constant[c]` at all times.
- `ToJson`: `{"name", "length": StopTime - StartTime, "fps", "tracks": [...]}`, samples at `StartTime + k / fps` for `k = 0 .. round(length * fps)`, the last one clamped to `StopTime`. One track per binding: `"path"` is the candidate whose CRC32 equals `Path` (else the number), `"type"` the TypeId, `"attribute"` `position`/`rotation`/`scale`/`euler` for Transform bindings, else the candidate whose CRC32 equals `Attribute` (else the number), `"size"` = `CurveCount`, `"values"` flattened. PPtr bindings add `"pptr": true` and keep raw values.

- [ ] **Step 1: Write the failing tests**

A test clip: streamed curves 2 (frame t=0: curve 0 `(0,0,2,1)`, curve 1 `(0,0,0,5)`; frame t=1: curve 0 `(1,0,0,3)`; frame t=+Infinity with no key), dense 1 curve (3 frames, rate 2, begin 0, samples `[0, 10, 20]`), constant `[7]`; bindings: `{Path = Crc32("Root/Ring"), TypeId = 4, Attribute = 3}` (3 curves: 0, 1 and the dense one) and `{Path = Crc32("Root"), TypeId = 23, Attribute = Crc32("material._TintColor.a")}`; start 0, stop 1.5.

- `SamplesStreamedDenseAndConstant`: `Sample(clip, 0.5f)` is `[2, 5, 10, 7]`; `Sample(clip, 1.5f)` is `[3.125, 5, 20, 7]`; `Sample(clip, 0.25f)[2] == 5`.
- `TransformAttributesTakeSeveralCurves`: `CurveCount` is 3, 4, 3, 3 for Transform attributes 1 to 4, and 1 for `TypeId` 23.
- `ToJsonResolvesPathsAndAttributes`: `ToJson("Test", clip, new[] { "", "Root", "Root/Ring" }, new[] { "material._TintColor.a" })`: `length` 1.5, `fps` 60; track 0 has path `"Root/Ring"`, type 4, attribute `"scale"`, size 3, 91 x 3 values, values 90 to 92 (frame 30) `[2, 5, 10]`; track 1 has path `"Root"`, attribute `"material._TintColor.a"`, size 1, all values 7.
- `UnknownHashesStayNumbers`: same clip, no candidates: track 0 `path` is the number `Crc32("Root/Ring")`, track 1 `attribute` is the number `Crc32("material._TintColor.a")`.

- [ ] **Step 2: Run to verify they fail** (`--filter "FullyQualifiedName~ClipDecoderTests"`)
- [ ] **Step 3: Implement `ClipDecoder`**
- [ ] **Step 4: Run to verify they pass**
- [ ] **Step 5: Commit** (`feat(editor): sample the game's animation clips`)

---

### Task 5: Typetree to JSON

**Files:**
- Create: `src/InvectorModKit.ChartEditor.Core/GameData/TypeTreeJson.cs`
- Test: `tests/InvectorModKit.ChartEditor.Tests/GameData/Fields.cs` (test-side builders), `TypeTreeJsonTests.cs`

**Interfaces:**
- Produces: `static class TypeTreeJson { const int MaxBytes = 4096; JToken Convert(AssetTypeValueField field, Func<int, long, JToken> pptr) }`. `pptr(fileId, pathId)` is called for every PPtr with a non-zero path id; its result is used as is.
- Test helper `Fields` (built the way AssetsTools.NET 3 builds them, `AssetTypeTemplateField` plus `AssetTypeValueField`): `Obj(string name, params AssetTypeValueField[] children)`, `Int(name, int)`, `Long(name, long)`, `UInt8(name, byte)`, `Float(name, float)`, `Bool(name, bool)`, `Str(name, string)`, `Array(name, params AssetTypeValueField[] items)` (a `vector` with an `Array` child), `Bytes(name, byte[])` (an array of UInt8), `PPtr(name, string type, int fileId, long pathId)` (type name `PPtr<type>`, children `m_FileID` Int32 and `m_PathID` Int64).

Rules: objects become `JObject` keyed by Unity's field names; arrays (a field marked as array, or an object whose only child is the `Array` field) become `JArray`; byte arrays (UInt8 arrays or `TypelessData`) longer than `MaxBytes` become `{"omitted": count}`, shorter ones arrays of numbers; a field whose type name starts with `PPtr<` becomes `null` when its path id is 0, else `pptr(fileId, pathId)`; Int64 and UInt64 outside +/-9007199254740991 become strings; non-finite Float and Double become `null`; Bool becomes `true`/`false`; other integers stay numbers.

- [ ] **Step 1: Write the failing tests**

- `NestedFieldsKeepUnityNames`: `Obj("Base", Str("m_Name", "Ring"), UInt8("m_Enabled", 1), Float("speed", 2.5f), Obj("pos", Float("x", 1), Float("y", 2), Float("z", 3)), Bool("loop", true))` gives `{"m_Name":"Ring","m_Enabled":1,"speed":2.5,"pos":{"x":1,"y":2,"z":3},"loop":true}`.
- `ArraysBecomeLists`: `Array("list", Int("data", 1), Int("data", 2), Int("data", 3))` inside an object gives `"list": [1,2,3]`.
- `ReferencesGoThroughTheCallback`: `PPtr("m_Material", "Material", 1, -3209297936361821700)` calls the callback once with `(1, -3209297936361821700)` and puts its `{"ref":"assets_shared--3209297936361821700"}` in place; `PPtr("m_Mesh", "Mesh", 0, 0)` gives `null` without calling it.
- `LargeByteArraysAreOmitted`: `Bytes("m_Data", new byte[5000])` gives `{"omitted":5000}`; `Bytes("small", new byte[] {1,2,3})` gives `[1,2,3]`.
- `LargeIntegersBecomeStrings`: `Long("id", 2577891501218760700)` gives the string `"2577891501218760700"`; `Long("n", 42)` gives the number 42.
- `NonFiniteFloatsBecomeNull`: `Float("a", float.NaN)` and `Float("b", float.PositiveInfinity)` give `null`; the whole object serialised with Newtonsoft contains neither `NaN` nor `Infinity`.

- [ ] **Step 2: Run to verify they fail** (`--filter "FullyQualifiedName~TypeTreeJsonTests"`)
- [ ] **Step 3: Implement `TypeTreeJson`**
- [ ] **Step 4: Run to verify they pass**
- [ ] **Step 5: Commit** (`feat(editor): turn the game's object fields into JSON`)

---

### Task 6: Cache folder, lock and swap

**Files:**
- Create: `src/InvectorModKit.ChartEditor.Core/GameData/GameDataCache.cs`
- Test: `tests/InvectorModKit.ChartEditor.Tests/GameData/GameDataCacheTests.cs`

**Interfaces:**
- Produces:
  - `enum RebuildResult { Built, Busy, Failed }`
  - `sealed class GameDataCache { GameDataCache(string dir); string Dir, TempDir /* dir + ".tmp" */, OldDir /* dir + ".old" */, LockPath /* dir + ".lock" */; void Recover(); RebuildResult Rebuild(Action<string> build, out Exception error) }`

`Rebuild`: open `LockPath` with `FileShare.None` and `FileOptions.DeleteOnClose` (an `IOException` there means `Busy`, `error` null); `Recover()`; delete `TempDir` if present and create it; run `build(TempDir)`; if it throws, delete `TempDir`, leave `Dir` untouched, return `Failed` with the exception; else move `Dir` to `OldDir` (when `Dir` exists), move `TempDir` to `Dir`, delete `OldDir`. Each move is retried up to 5 times, 200 ms apart, on `IOException`. `Recover`: if `Dir` is missing and `OldDir` exists, move `OldDir` back to `Dir`; then delete leftover `OldDir` and `TempDir`.

- [ ] **Step 1: Write the failing tests** (each in its own temporary root, deleted in `Dispose`)

- `BuildSwapsTheNewContentIn`: `Dir` holds `a.txt` = `"old"`; `Rebuild(tmp => File.WriteAllText(Path.Combine(tmp, "a.txt"), "new"))` returns `Built`; `Dir\a.txt` reads `"new"`; `TempDir`, `OldDir` and `LockPath` no longer exist.
- `FailedBuildKeepsTheOldCache`: `Dir\a.txt` = `"old"`; the build writes a file then throws `InvalidOperationException("boom")`; result `Failed`, `error.Message == "boom"`, `Dir\a.txt` still `"old"`, `TempDir` gone.
- `FirstBuildWithoutCache`: no `Dir`; a successful build creates it.
- `SecondBuilderIsBusy`: hold `LockPath` open with `FileShare.None` in the test; result `Busy`, `error` null, build not called, `Dir` untouched.
- `RecoverRestoresTheOldCacheAfterACrashMidSwap`: no `Dir`, `OldDir\a.txt` = `"old"`, `TempDir` with a partial file; `Recover()` gives `Dir\a.txt` = `"old"`, and neither `OldDir` nor `TempDir` remain.

- [ ] **Step 2: Run to verify they fail** (`--filter "FullyQualifiedName~GameDataCacheTests"`)
- [ ] **Step 3: Implement `GameDataCache`**
- [ ] **Step 4: Run to verify they pass**
- [ ] **Step 5: Commit** (`feat(editor): build the game data cache aside and swap it in`)

---

### Task 7: Bundle reader

**Files:**
- Create: `src/InvectorModKit.ChartEditor.Core/GameData/BundleReader.cs`
- Create: `tests/InvectorModKit.ChartEditor.Tests/GameData/GameInstall.cs`
- Test: `tests/InvectorModKit.ChartEditor.Tests/GameData/BundleReaderTests.cs`

**Interfaces:**
- Consumes: `GameDataStamp.BundlesDir` (Task 1).
- Produces:
  - `sealed class AssetRef { string Bundle /* file name */; string ShortName; AssetsFileInstance File; AssetFileInfo Info; long PathId; int TypeId; }`
  - `sealed class BundleReader : IDisposable`:
    - `BundleReader(string bundlesDir)`: indexes every `*.bundle` of the folder by the serialized file names (CAB names) its directory lists, reading headers only (`unpackIfPacked: false`).
    - `static string ShortName(string bundleFileName)`: file name without `.bundle`, without a leading 32-hex-digit hash and `_`, without a leading `basegame_`.
    - `static FileStream OpenShared(string path)`: `FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete`.
    - `IEnumerable<AssetRef> Assets(string bundle, int typeId)`: every object of a class in every serialized file of the bundle (loaded on first use, unpacked in memory).
    - `AssetRef FindRootGameObject(string bundle, string name)`: the GameObject of that name whose Transform has no father; null if none.
    - `IEnumerable<AssetRef> FindScripts(string bundle, string className)`: MonoBehaviours whose `m_Script` class name matches.
    - `AssetTypeValueField Read(AssetRef asset)`
    - `AssetRef Resolve(AssetRef from, int fileId, long pathId)`: 0 means the same file; otherwise the external's CAB name, looked up in the index, its bundle loaded on demand; null when not found.
    - `string ScriptClass(AssetRef monoBehaviour)`, `string ShaderName(AssetRef shader)` (`m_ParsedForm.m_Name`, cached per id)
    - `byte[] ReadResource(AssetRef from, string path, long offset, long size)`: `path` is like `archive:/CAB-x/CAB-x.resS`; the bytes come from that entry of the owning bundle.
    - `string Id(AssetRef asset)` = `ShortName + "-" + PathId`
    - `void Release()`: unloads every loaded bundle (called between recipe bundles to bound memory).
- Test helper `GameInstall`: `static string Dir` found in this order and kept only if `GameLocator.IsGameDir`: environment variable `INVECTOR_GAME_DIR`; `EditorSettings.Load(%APPDATA%\InvectorChartEditor).GameDir`; on Windows only, `GameLocator.Find(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string)`. `sealed class GameFactAttribute : FactAttribute` sets `Skip = "Invector Rhythm Galaxy is not installed"` when `Dir` is null.

All bundles rely on their embedded type trees (no class database is shipped); a serialized file without them is reported by `Read` as an `InvalidDataException`.

- [ ] **Step 1: Write the failing tests**

- `ShortNames`: `"basegame_assets_shared.bundle"` gives `"assets_shared"`; `"ab04f3fa61ea81f730e4beaddac78637_unitybuiltinshaders.bundle"` gives `"unitybuiltinshaders"`; `"sharedmaterials_assets_all.bundle"` gives `"sharedmaterials_assets_all"`.
- `OpensAFileAnotherProcessHoldsForWriting`: a temp file held open by the test with `FileAccess.ReadWrite, FileShare.ReadWrite`; `OpenShared` reads its bytes.
- `[GameFact] FindsAPickupAndItsShader`: `FindRootGameObject("basegame_assets_shared.bundle", "XBOX Pickup Left X OPTIMIZED")` is not null; following its components to a `MeshRenderer`, its first material resolves (possibly in another bundle) and `ShaderName` of that material's shader is not empty.

- [ ] **Step 2: Run to verify they fail** (`--filter "FullyQualifiedName~BundleReaderTests"`)
- [ ] **Step 3: Implement `BundleReader` and `GameInstall`** (AssetsTools.NET 3: `AssetsManager.LoadBundleFile(Stream, string, bool)`, `LoadAssetsFileFromBundle`, `GetBaseField`, `AssetBundleFile.BlockAndDirInfo.DirectoryInfos`, `GetFileRange`)
- [ ] **Step 4: Run to verify they pass** (the `GameFact` runs on the owner's PC, skips elsewhere)
- [ ] **Step 5: Commit** (`feat(editor): read the game's bundles with AssetsTools.NET`)

---

### Task 8: Prefabs, materials, meshes and textures into the cache

**Files:**
- Create: `src/InvectorModKit.ChartEditor.Core/GameData/PrefabWriter.cs`, `GameDataBuilder.cs`, `GameData.cs`
- Create: `tests/InvectorModKit.ChartEditor.Tests/GameData/GameDataBuildFixture.cs`
- Test: `tests/InvectorModKit.ChartEditor.Tests/GameData/GameDataIntegrationTests.cs`

**Interfaces:**
- Consumes: Tasks 1 to 7 (`GameDataRecipe`, `GameDataStamp`, `CacheFiles`, `TextureWriter`, `MeshDecoder`, `TypeTreeJson`, `GameDataCache`, `BundleReader`).
- Produces:
  - `sealed class GameDataReport { List<string> Found; List<string> Warnings; TimeSpan Elapsed; bool Busy; }`
  - `static class GameDataBuilder { GameDataReport Write(string gameDir, string outDir, IProgress<string> progress) }`: runs the recipe into `outDir`, writes `index.json` last.
  - `static class GameData { GameDataReport Build(string gameDir, string cacheDir, IProgress<string> progress) }`: `GameDataBuilder.Write` inside `GameDataCache.Rebuild`; `Busy` sets `report.Busy`; `Failed` rethrows the build's exception.
  - `sealed class PrefabWriter`: `PrefabWriter(BundleReader reader, string outDir, GameDataReport report)`; `JObject Write(AssetRef rootGameObject)`; `IEnumerable<string> TransformPaths` (every relative path between a node and its descendants, `""` included, over all prefabs written); `IEnumerable<string> MaterialProperties` (every property name of every material written); `JToken Link(AssetRef from, int fileId, long pathId)` (shared with Task 9).
  - Test fixture `GameDataBuildFixture : IDisposable`: when `GameInstall.Dir` is set, `GameData.Build(GameInstall.Dir, <temp>\game, null)` once; exposes `Dir`, `Report`, `Index` (parsed `index.json`), `Json(string relativePath)`, and deletes the temp folder in `Dispose`. Does nothing when the game is absent.

Prefab file `objects/<FileSafe(name)>.json`: `{"name", "id", "root": node, "assets": {id: object}}`; node `{"name", "id", "active", "position": [x,y,z], "rotation": [x,y,z,w], "scale": [x,y,z], "components": [{"id", "type", "class"? , "fields"}], "children": [...]}` (Transform values are local, in Unity's axes; the Transform itself is not repeated in `components`; `type` is the Unity class name, `class` the script class for MonoBehaviours).

`Link` policy for each PPtr met while converting fields:
- an object of the prefab's own tree (GameObject, Transform, component): `{"ref": id}`;
- Mesh: written once to `meshes/<id>.bin` (`MeshDecoder`, vertex data from `m_StreamData` through `ReadResource` when its size is not 0), `{"ref": id}`; `m_MeshCompression` not 0 or an unsupported topology: a warning and `{"ref": id, "missing": true}`;
- Texture2D: written once to `textures/<id>.tex` (`image data`, or `m_StreamData` through `ReadResource`), `{"ref": id}`; a format outside `TextureWriter.Supported` adds the warning `texture <name> (<id>): format <n> not supported` (crunched ones say `crunched`), the file is still written;
- Material: inlined `{"id", "name", "shader", "keywords", "renderQueue", "colors": {name: [r,g,b,a]}, "floats": {name: v}, "textures": {name: {"ref", "scale": [x,y], "offset": [x,y]}}}` (cached per id, empty texture slots left out);
- Sprite: inlined `{"id", "name", "texture": {"ref"}, "rect": [x,y,w,h] (from `m_RD.textureRect`), "pivot": [x,y], "pixelsPerUnit"}`;
- MonoScript: the class name as a string;
- another object of a walk type (spec list: GameObject, Transform, MeshFilter, MeshRenderer, SkinnedMeshRenderer, SpriteRenderer, ParticleSystem, ParticleSystemRenderer, TrailRenderer, Animator, MonoBehaviour) in a recipe bundle: `{"ref": id}` and its JSON added once to `assets` (walked the same way); in other bundles, and any other type: `{"ref": id, "type": <class name>}` only;
- not found: `{"ref": id, "missing": true}`; one warning per prefab file gives the count.
- `assets` holds at most 2000 objects; past that, plain refs and a warning.

`index.json`: `GameDataStamp.NewIndex(gameDir)` plus `"items": [{"key", "file"}]` for each item found and `"warnings": [...]`. A recipe item not found adds the warning `not found: <Key> in <Bundle>` and is left out of `items`. Progress reports one line per recipe item. `GameDataBuilder` calls `reader.Release()` after each recipe bundle.

- [ ] **Step 1: Write the failing integration tests** (`[GameFact]`, class `GameDataIntegrationTests : IClassFixture<GameDataBuildFixture>`, `ITestOutputHelper` for the log)

- `EveryPrefabIsWritten`: for every recipe Prefab item, `Index.items` has its key and the file exists; no warning starts with `not found: Prefab:`.
- `BeginnerTier4Trails`: in `objects/Ship_Beginner.json`, the node `Beginner_TrailBigL_tier4` has a `TrailRenderer` with `m_Time` 0.1, head width `m_Parameters.widthMultiplier * widthCurve.m_Curve[0].value` 0.2 and tail width (last key) 0.1; the node `Wide Right` under `Beginner_TrailWideR_tier4` has a `TrailRenderer` with `m_Time` 0.06, head and tail widths 0.3, `m_Parameters.alignment` 1 (TransformZ, upright), and its rotation is `[0, 0.7071, 0, 0.7071]`. Tolerance 0.001 everywhere.
- `PickupMeshIsWritten`: the tree of `XBOX Pickup Left X OPTIMIZED` holds a `MeshFilter` whose `m_Mesh` ref names an existing `meshes/<id>.bin` with `vertexCount` > 0 and a file length at least header + `vertexCount * sum(sizes) * 4`.
- `TexturesAreSupported`: every `textures/*.tex` header has a format in `TextureWriter.Supported`; the failure message lists the others by file.
- `JsonIsStrict`: every `*.json` in the cache parses with `JToken.Parse` and its text contains neither `NaN` nor `Infinity`.
- `LogsTheBuild`: writes elapsed seconds, file count and total megabytes to the test output (target under a minute; not asserted).

- [ ] **Step 2: Run to verify they fail** (`--filter "FullyQualifiedName~GameDataIntegrationTests"` on the owner's PC: build errors first, then failures)
- [ ] **Step 3: Implement `PrefabWriter`, `GameDataBuilder` (Prefab items only for now; other kinds are added in Task 9) and `GameData`**
- [ ] **Step 4: Run to verify they pass** (owner's PC; then the whole test project, where these skip if the game is absent)
- [ ] **Step 5: Commit** (`feat(editor): write the game's prefabs, meshes and textures to the cache`)

---

### Task 9: Clips, animator, settings and levels

**Files:**
- Create: `src/InvectorModKit.ChartEditor.Core/GameData/LevelJson.cs`
- Modify: `src/InvectorModKit.ChartEditor.Core/GameData/GameDataBuilder.cs`
- Test: `tests/InvectorModKit.ChartEditor.Tests/GameData/LevelJsonTests.cs`; add to `GameDataIntegrationTests.cs`

**Interfaces:**
- Consumes: `ClipDecoder.ToJson` (Task 4), `PrefabWriter.Link`, `TransformPaths`, `MaterialProperties` (Task 8), `BundleReader` (Task 7).
- Produces: `static class LevelJson { JObject Build(JObject spline, JObject editor, JObject splineMesh, JToken roadMaterial, JObject toblerone, JObject walls) }` (inputs are `TypeTreeJson` outputs with links already applied; the last four may be null).

Level JSON (`levels/<world>.json`): `start` = `spline.StartPosition`; `length` = `spline.totalLength`; `pos` = `spline.pos` as `[[x,y,z]]`; `rot` = `spline.rot` as `[[x,y,z,w]]`; `acc` = `spline.accumulatedLengthsPrecision`; `fly` = `editor.FlyAreas` as is; `circle` = `{"y": CircleY, "radius": CircleRadius, "bottom": BottomLine}`; `road` = `{"mesh": splineMesh.Prefabs[0], "material": roadMaterial, "scale": splineMesh.scale, "offsetY": splineMesh.splineOffsetY}` or null; `tunnel` = toblerone's fields and `holdWalls` = walls' fields, each without `m_GameObject`, `m_Enabled`, `m_Script`, `m_Name`, `feed` (null when absent).

Builder additions:
- Level: in the world's scene bundle, the `Spline` and `SplineEditor` on the same GameObject (several: the longest `totalLength`, with a warning); `SplineMesh`, `Toblerone`, `TobleroneWalls` whose `feed` points at that `SplineEditor`; the road material is the first material of the `MeshRenderer` on the `SplineMesh`'s GameObject. A missing part is null with a warning `level <world>: no <class>`.
- Clip: `AnimationClip` by name in its bundle; `MuscleClipData` from `m_MuscleClip.m_StartTime`, `m_StopTime`, `m_Clip.data.m_StreamedClip` (`data`, `curveCount`), `m_DenseClip` (`m_FrameCount`, `m_CurveCount`, `m_SampleRate`, `m_BeginTime`, `m_SampleArray`), `m_ConstantClip.data`, and `m_ClipBindingConstant.genericBindings` (`path`, `attribute`, `typeID`, `isPPtrCurve`); candidates are `TransformPaths` and, for attributes, each of `MaterialProperties` as `material.<p>` and `<p>`, each with and without `.r .g .b .a .x .y .z .w`, plus `m_IsActive`, `m_Enabled`, `InitialModule.startColor.maxColor.r/g/b/a`, `EmissionModule.rateOverTime.scalar`, `InitialModule.startSize.scalar`, `InitialModule.startLifetime.scalar`. Clips are processed after all prefabs. Written to `clips/<FileSafe(name)>.json`.
- Controller: the `AnimatorController` by name, its whole `TypeTreeJson` (links applied) to `objects/<FileSafe(name)>.json` as `{"name", "id", "fields"}`.
- Settings: `settings.json` = `{"scriptables": {class: [{"id", "name", "fields"}]}, "walker": {class: [{"id", "path", "fields"}]}}`; `scriptables` from `FindScripts` for each Settings item; `walker` from the MonoBehaviours of the `PlayerWalkerGlobal` tree (its prefab file already written) whose class is in `GameDataRecipe.WalkerClasses`, `path` being the node's path in the tree. A missing class is a warning `not found: Settings:<class>` or `walker: no <class>`.

- [ ] **Step 1: Write the failing tests**

`LevelJsonTests` (synthetic `JObject`s shaped like the game's components):
- `MapsTheSplineAndFlyAreas`: spline `{StartPosition: 210, totalLength: 2562.75, pos: [{x:1,y:2,z:3},{x:4,y:5,z:6}], rot: [{x:0,y:0,z:0,w:1}], accumulatedLengthsPrecision: [1,2]}`, editor `{CircleY: 0, CircleRadius: 8, BottomLine: -25.16, FlyAreas: [{z: 0}]}`; result `start` 210, `length` 2562.75, `pos` `[[1,2,3],[4,5,6]]`, `rot` `[[0,0,0,1]]`, `acc` `[1,2]`, `fly` `[{"z":0}]`, `circle` `{"y":0,"radius":8,"bottom":-25.16}`.
- `RoadTunnelAndWalls`: splineMesh `{Prefabs: [{"ref":"x-1"}], scale: {x:13,y:13,z:13}, splineOffsetY: -3.33}`, roadMaterial `{"name":"Road"}`, toblerone `{m_Name: "", feed: {"ref":"x-2"}, size: 1790, interval: 2}`, walls `{m_Script: "TobleroneWalls", size: 1900, generateThreeWalls: 1}`; `road` = `{"mesh":{"ref":"x-1"},"material":{"name":"Road"},"scale":{"x":13,"y":13,"z":13},"offsetY":-3.33}`; `tunnel` = `{"size":1790,"interval":2}`; `holdWalls` = `{"size":1900,"generateThreeWalls":1}`.
- `MissingPartsAreNull`: `splineMesh`, `toblerone`, `walls` null give `road`, `tunnel`, `holdWalls` null.

Added to `GameDataIntegrationTests` (`[GameFact]`):
- `VoidAndSpaceLevels`: `levels/void.json`: `start` 210, `length` 2562.75 (0.01), 17 `fly`; `levels/space.json`: `start` 1400, `length` 4391.99 (0.01), 27 `fly`; for both, `acc` count = 20 x `rot` count and `pos` count = 3 x `rot` count + 1. Every world has its `levels/<world>.json`.
- `RollClips`: `clips/RollRightLong.json`, `clips/coaster_RollRightLong2Turns.json`, `clips/coaster_RollRightLong3Turns.json` have `length` 1.567, 1.933, 2.6 (0.001), at least one track with attribute `rotation`, no NaN; both `Airpickup` clips exist with tracks.
- `CoasterAnimator`: `objects/Coaster_Animator.json` exists and `fields.m_TOS` is not empty.
- `ControlLookMaterials`: in `settings.json`, `scriptables.MaterialsDatabase[0].fields.materialPerControlType`, the entry with `controlType` 2 (XBOX) has a `materialPickupLeft` whose main colour (`colors._TintColor`, else `colors._Color`) is blue (b > r and b > g), and the entry with `controlType` 1 (PlayStation) a pink one (r > g and b > g).
- `WalkerComponents`: `settings.json` `walker` has every class of `GameDataRecipe.WalkerClasses`, and the `PlayerWalkerGlobal` tree has nodes `PickupParticles`, `StrumParticles` and `PickupParticlesLand`, each with a `ParticleSystem`. (These names come from the spec; if the game names one differently, report it to the owner instead of editing the assertion.)
- `NoWarnings`: `Report.Warnings` is empty (the failure message lists them).

- [ ] **Step 2: Run to verify they fail** (`--filter "FullyQualifiedName~LevelJsonTests|FullyQualifiedName~GameDataIntegrationTests"`)
- [ ] **Step 3: Implement `LevelJson` and the builder additions**
- [ ] **Step 4: Run to verify they pass** (owner's PC for the integration tests; log the build time from `LogsTheBuild`)
- [ ] **Step 5: Commit** (`feat(editor): write the game's clips, settings and level paths to the cache`)

---

### Task 10: Game data service

**Files:**
- Create: `src/InvectorModKit.ChartEditor.Core/GameData/GameDataService.cs`
- Test: `tests/InvectorModKit.ChartEditor.Tests/GameData/GameDataServiceTests.cs`

**Interfaces:**
- Consumes: `GameData.Build`, `GameDataReport` (Task 8), `GameDataStamp.IsFresh` (Task 1), `GameLocator.IsGameDir`.
- Produces:
  - `enum GameDataState { None, Reading, Ready, Failed }`
  - `sealed class GameDataService { GameDataService(string root, Func<string, string, IProgress<string>, GameDataReport> build = null /* GameData.Build */); string CacheDir /* root\game */; string LogPath /* root\game-data.log */; GameDataState State; event Action<GameDataState> Changed; void Refresh(string gameDir) }`

`Refresh` (blocking; the host runs it on a worker thread; a call while another runs returns at once):
- no game (`!GameLocator.IsGameDir`): `Ready` if `CacheDir\index.json` exists, else `None`;
- fresh cache: `Ready`;
- else `Reading`, then `build(gameDir, CacheDir, progress)`; result not busy: `Ready`; busy: `Ready` if a cache exists, else `None`; exception: `Failed`.
- `LogPath` is rewritten for each build attempt: start time, game folder, progress lines, warnings, elapsed time, and the exception's full text on failure.
- `Changed` fires only when the state changes.

- [ ] **Step 1: Write the failing tests** (a fake game folder that passes `GameLocator.IsGameDir`, with `catalog.json`; a fake `build` delegate that records calls and writes `GameDataStamp.NewIndex` into the cache dir, or throws, or returns `Busy`)

- `NoGameNoCacheIsNone`: game dir null; state `None`, build not called, no `Changed`.
- `FreshCacheIsReadyWithoutReading`: index already fresh; `Changed` sequence `[Ready]`, build not called.
- `StaleCacheIsRebuilt`: no index; sequence `[Reading, Ready]`, build called once with `CacheDir`; the log holds the game folder.
- `FailureIsReportedAndLogged`: build throws `IOException("disk full")`; sequence `[Reading, Failed]`; the log contains `disk full`.
- `BusyKeepsReadyWhenACacheExists`: a stale index exists, build returns `Busy`; final state `Ready`.
- `ConcurrentRefreshBuildsOnce`: the fake build blocks on a `ManualResetEventSlim`; a second `Refresh` on another thread returns while the first runs; build called once.

- [ ] **Step 2: Run to verify they fail** (`--filter "FullyQualifiedName~GameDataServiceTests"`)
- [ ] **Step 3: Implement `GameDataService`**
- [ ] **Step 4: Run to verify they pass**
- [ ] **Step 5: Commit** (`feat(editor): decide when to rebuild the game data and log it`)

---

### Task 11: Host wiring and status messages

**Files:**
- Modify: `tools/ChartEditor/MainForm.cs` (constructor, `Start`, `OnMessage`, `HostCall`, `Serve`)
- Modify: `tools/ChartEditor/ui/app.js` (init near `on("fonts", ...)`, new method next to `status`)
- Modify: `tools/ChartEditor/ui/i18n.js` (three rows after `gameNotFound`)
- Test: existing `tools/ChartEditor/ui/tests/i18n.test.js`

**Interfaces:**
- Consumes: `GameDataService` (Task 10).
- Produces for part 2: page event `gameData` with data `{"state": "none"|"reading"|"ready"|"failed"}`; host call `host.gameData` returning the same object; cache files under `https://<app host>/game/<path>`.

Host:
- Field `_gameData = new GameDataService(Path.Combine(LocalApplicationData, "InvectorModKit", "LevelEditor"))`; its `Changed` posts `{"event":"gameData","data":{"state":"<lower-case state>"}}` through `Post`.
- `RefreshGameData()`: remembers `_backend.Settings.GameDir` in `_gameDataDir` and runs `_gameData.Refresh(dir)` with `Task.Run`, exceptions swallowed (the service logs). Called at the end of `Start()`, and after each `OnMessage` whose handling left `_backend.Settings.GameDir` different from `_gameDataDir` (the game folder chosen in the settings).
- `HostCall` case `host.gameData`: `{"state": ...}`.
- `Serve`: `path.StartsWith("game/")` maps to `_gameData.CacheDir` like `skin/fonts/` (same allowed-path check; `.bin` and `.tex` use the default `application/octet-stream`).

Page:
- In init: `on("gameData", (d) => this.gameDataChanged(d.state));` then `call("host.gameData").then((d) => this.gameDataChanged(d.state)).catch(() => {});`
- `gameDataChanged(state)`: `reading` shows `t("gameDataReading")`; `failed` shows `t("gameDataFailed")` as an error; `ready` shows `t("gameDataReady")` only if the previous state was `reading`; stores the state in `this.gameDataState`.

Rows (exact copy; the English ones are the spec's):

| key | en | fr | de | it | es | pt-BR |
|---|---|---|---|---|---|---|
| gameDataReading | Reading the game's files for the 3D preview… | Lecture des fichiers du jeu pour l'aperçu 3D… | Spieldateien werden für die 3D-Vorschau gelesen… | Lettura dei file del gioco per l'anteprima 3D… | Leyendo los archivos del juego para la vista previa 3D… | Lendo os arquivos do jogo para a pré-visualização 3D… |
| gameDataReady | 3D preview ready | Aperçu 3D prêt | 3D-Vorschau bereit | Anteprima 3D pronta | Vista previa 3D lista | Pré-visualização 3D pronta |
| gameDataFailed | Couldn't read the game's files: the 3D preview stays simple | Impossible de lire les fichiers du jeu : l'aperçu 3D reste simple | Die Spieldateien konnten nicht gelesen werden: Die 3D-Vorschau bleibt einfach | Impossibile leggere i file del gioco: l'anteprima 3D resta semplice | No se pudieron leer los archivos del juego: la vista previa 3D sigue siendo simple | Não foi possível ler os arquivos do jogo: a pré-visualização 3D continua simples |

| key | pl | ru | ja | ko | zh |
|---|---|---|---|---|---|
| gameDataReading | Odczytywanie plików gry dla podglądu 3D… | Чтение файлов игры для 3D-превью… | 3Dプレビュー用にゲームのファイルを読み込み中… | 3D 미리보기를 위해 게임 파일을 읽는 중… | 正在读取游戏文件以生成 3D 预览… |
| gameDataReady | Podgląd 3D gotowy | 3D-превью готово | 3Dプレビューの準備ができました | 3D 미리보기 준비 완료 | 3D 预览已就绪 |
| gameDataFailed | Nie udało się odczytać plików gry: podgląd 3D pozostaje prosty | Не удалось прочитать файлы игры: 3D-превью останется простым | ゲームのファイルを読み込めませんでした。3Dプレビューは簡易表示のままです | 게임 파일을 읽을 수 없습니다. 3D 미리보기는 단순하게 유지됩니다 | 无法读取游戏文件：3D 预览保持简易模式 |

- [ ] **Step 1: Add the `t("gameData…")` calls to `app.js` first and run the interface tests to see them fail**

Run: `cd tools/ChartEditor/ui; node --test tests/model.test.js tests/i18n.test.js`
Expected: FAIL in "every key the interface uses is defined" listing the three keys.

- [ ] **Step 2: Add the three rows to `i18n.js`, run the same command**

Expected: PASS.

- [ ] **Step 3: Wire `MainForm.cs` and build the host**

Run: `dotnet build tools/ChartEditor/ChartEditor.csproj -c Debug`
Expected: build succeeds, `AssetsTools.NET.dll` is in `tools/ChartEditor/bin/Debug/net48/`.

- [ ] **Step 4: Check in the real editor (owner's PC)**

With `%LOCALAPPDATA%\InvectorModKit\LevelEditor\game` absent, start `tools/ChartEditor/bin/Debug/net48/InvectorLevelEditor.exe`: the status bar shows the reading text, the editor stays usable (open a song, scroll the timeline), then shows "3D preview ready"; the cache folder holds `index.json`, `objects`, `meshes`, `textures`, `clips`, `levels`, `settings.json`, and `game-data.log` sits beside it. Close and start again: no status message, and the `built` date in `index.json` is unchanged. The game's folder is only read (no file in it changes).

- [ ] **Step 5: Run every test and commit**

Run: `dotnet test tests/InvectorModKit.ChartEditor.Tests` and the interface tests. Expected: all pass (integration tests skipped where the game is absent).

```bash
git add tools/ChartEditor/MainForm.cs tools/ChartEditor/ui/app.js tools/ChartEditor/ui/i18n.js
git commit -m "feat(editor): read the game data in the background and say so in the status bar"
```

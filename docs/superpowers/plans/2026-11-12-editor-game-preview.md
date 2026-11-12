# The game's view in the level editor's 3D preview (part 2 of 3) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The editor's preview panel shows what the game shows at the playhead for a perfect player, at 60 frames per second during playback, built from part 1's game data cache.

**Architecture:** Pure, `node --test`-tested modules compute everything that follows from time and the chart (level path, perfect run, ship flight, camera, particle and trail specs read from the cache); thin three.js modules turn their results into a scene; `GamePreview` assembles them behind today's `Preview3D` interface, and `app.js` picks it when the game data is ready. The track is bent on the CPU through a frame table sampled every 0.5 units of the level path, once per chunk; notes, particles and hold walls are placed through the same table each frame.

**Tech Stack:** ES modules, three.js 0.170 (vendored, with EffectComposer, RenderPass, ShaderPass, UnrealBloomPass, OutputPass), WebGL 2, `node --test` (Node 20 in CI).

**Spec:** `docs/superpowers/specs/2026-11-12-editor-game-preview-design.md` (read it with this plan). Part 1's cache format: `docs/superpowers/specs/2026-11-12-editor-game-data-design.md` and `src/InvectorModKit.ChartEditor.Core/GameData/`.

**Reference implementation (owner's PC only, never committed):** the prototype
`C:\Users\Theo\AppData\Local\Temp\claude\e0e90af092ab5007\79b00743-8db5-4d4b-b53a-5a2cb3af8504\scratchpad\compare\proto2.html`
and its `gameasset.js`. Tasks name the prototype functions they port; the expected numbers below come from it.

## Global Constraints

- No new package: three.js 0.170 and the addons already in `tools/ChartEditor/ui/vendor/three/`.
- Pure modules (`math.js`, `curves.js`, `cachefiles.js`, `path.js`, `rig.js`, `run.js`, `ship.js`, `camera.js`, `particlespec.js`, `particles.js`, `fxspec.js`) never import `three`; they work on plain arrays, so `node --test` runs them.
- Axes: the cache keeps Unity's left-handed axes. Positions become `(x, y, -z)`, quaternions `(-x, -y, z, w)`, triangle winding is reversed. Pure modules return three's axes.
- Colours: the game renders in linear space; colours and vertex colours in the cache are sRGB values made linear with `toLinear`.
- Fixed until part 3: faithful effects, XBOX look (left blue, right red, down green, up yellow), the void world, the lab decor. `GamePreview.options.tier` (`null` = perfect run) exists only to compare with the prototype; no interface for it yet.
- Same time, same picture: no module keeps state from one frame to the next.
- Nothing from the game in the repository: tests use synthetic data, or the owner's cache at `%LOCALAPPDATA%\InvectorModKit\LevelEditor\game` and are then skipped when it is absent (`{ skip: !HAS_CACHE }`).
- No em dash anywhere; new interface texts (if any) in the 11 languages of `ui/i18n.js`.
- Commits: Conventional Commits, scope `editor`; no `Co-Authored-By`, no "generated with", no mention of an AI tool.

### Plan decisions (refining the spec)

1. **The level-up pickup is drawn.** The spec left it out ("not in the recipe"), but the game marks an ordinary pickup as the level-up one and shows its own `Level Up Text` node, which every pickup prefab in the cache has. Cost: one node made visible.
2. **CPU bending through a frame table.** `path.js` samples the path every 0.5 units; track chunks are bent once and cached, and per-frame placements (notes, particles, hold walls) read the same table. The prototype bent every vertex every frame.
3. **Particle curves use Unity's Hermite evaluation.** The prototype read particle curve keys linearly. Cost: particle shapes may differ slightly from the prototype's, closer to the game.
4. **Not modelled:** the camera's base shake at tiers 3 and 4 (0.01, 0.02), the walker's per-level speed scale triggers, and the ground speed boost.
5. **CI runs every interface test file** (`node --test` with no file list, in `tools/ChartEditor/ui`), approved by the owner.

## Review Focus

1. **Edits during playback.** The editor calls `setChart` after every edit, up to many times a second while dragging. Expected: no stutter and no growing GPU memory. Pinned by `run.test.js` "prepares a dense chart under 50 ms" (Task 5) and the `renderer.info.memory` check after 100 `setChart` calls (Task 14).
2. **Odd charts.** An empty difficulty, a chart with only Freefly, two Freefly sections in a row, Freefly at time 0, notes before 0 or after the end, a playhead before 0 or past the end. Expected: a sensible frame, never an exception. Pinned by `run.test.js` "odd charts" and `ship.test.js` "odd charts" (Tasks 5, 6).
3. **Long songs.** A 10-minute song runs several laps of the void path (2562.75 long). Expected: the same picture on every lap, no seam. Pinned by `path.test.js` "a lap later is the same frame" (Task 3) and chunk keys by straight distance (Task 11).
4. **A cache missing an item, or a GPU refusing a texture format.** Expected: a simple shape or a plain colour for that item, everything else unchanged. Pinned by `cachefiles.test.js` "unknown format" (Task 2) and the fallback branch in `gamecache.js` (Task 10, checked by renaming one prefab file of a copy of the cache).
5. **Resizing the panel**, down to a few pixels and on high-DPI screens. Expected: no blank or stretched picture, bloom at the right size. Checked in Task 14 (drag the panel splitter to its minimum, Windows scale 150 %).

---

## File Structure

All new files are in `tools/ChartEditor/ui/preview/`; tests are in `tools/ChartEditor/ui/tests/`.

| File | Responsibility | three? |
|---|---|---|
| `math.js` | Vectors as `[x, y, z]`, quaternions as `[x, y, z, w]`, Unity to three conversion, `toLinear`, seeded random | no |
| `curves.js` | Unity AnimationCurve (Hermite), MinMaxCurve and MinMaxGradient evaluation | no |
| `cachefiles.js` | Parses part 1's `.bin` meshes and `.tex` textures | no |
| `path.js` | The level path: frame table, `frameAt(d)`, `place(point, d)` | no |
| `rig.js` | The walker's numbers (camera rig, lanes, heights, transition and fly curves) from `settings.json` and the walker tree | no |
| `run.js` | The perfect run: levels, speed, distance, field of view, note places, visible notes | no |
| `ship.js` | Lanes, Bounce sway, Freefly flight, barrel rolls, the ship's place and turn | no |
| `camera.js` | The camera's place, aim and up, bent along the path | no |
| `particlespec.js` | Unity ParticleSystem fields to an emitter spec | no |
| `particles.js` | Emits a spec's particles at an age, deterministically | no |
| `fxspec.js` | Trail sets and level halo sizes from the ship and pickup prefabs | no |
| `gamecache.js` | Loads the cache, makes geometries, textures, materials and prefab objects | yes |
| `materials.js` | The game's shaders as three materials | yes |
| `track.js` | Tunnel, road and lab decor in bent, cached chunks | yes |
| `notes.js` | Note objects, their places, the level-up text, key icons, selection outline | yes |
| `effects.js` | Particles, trails, halo, landing glow, hold walls | yes |
| `post.js` | Bloom, vignette and chromatic aberration | yes |
| `gamepreview.js` | `GamePreview`: load, prepare, render, stats | yes |
| `../app.js` (modify) | Chooses `GamePreview` or `Preview3D`, falls back on errors | |
| `../../../.github/workflows/ci.yml` (modify) | `node --test` with no file list | |

Test command (from `tools/ChartEditor/ui`): `node --test tests/<name>.test.js`; all: `node --test`.

---

### Task 1: Math and curves

**Files:**
- Create: `tools/ChartEditor/ui/preview/math.js`, `tools/ChartEditor/ui/preview/curves.js`
- Test: `tools/ChartEditor/ui/tests/curves.test.js`
- Modify: `.github/workflows/ci.yml` (the `Level editor tests` step runs `node --test`)

**Interfaces:**
- Produces (`math.js`): `add(a, b)`, `sub(a, b)`, `scale(a, k)`, `dot(a, b)`, `cross(a, b)`, `length(a)`, `normalize(a)`, `lerp(a, b, k)` on `[x, y, z]`; `qmul(a, b)`, `qrotate(q, v)`, `qslerp(a, b, k)`, `qfromAxisAngle(axis, rad)`, `qfromEulerYXZ(x, y, z)` on `[x, y, z, w]`; `fromUnityPoint([x, y, z])`, `fromUnityQuat([x, y, z, w])`; `toLinear(v)`; `seeded(seed) -> () => number in [0, 1)` (Park-Miller, as the prototype's `emit`: `s = (seed * 7919 + 12345) % 2147483647 || 1`, then `s = s * 16807 % 2147483647`); `hash01(i)` (the prototype's); `clamp01(v)`; `smoothstep01(k)`.
- Produces (`curves.js`): `hermite(keys) -> (t) => number`, keys `[{ time, value, inSlope, outSlope }]` as the cache writes Unity keyframes (clamped outside the keys, as the prototype's `curve`); `minMaxCurve(c) -> (u, r) => number` for a cache MinMaxCurve (`minMaxState` 0 constant `scalar`, 1 curve `scalar x maxCurve`, 2 between `minCurve` and `maxCurve` by `r`, both times `scalar`, 3 between `minScalar` and `scalar` by `r`); `minMaxGradient(g) -> (u, r) => [r, g, b, a]` (`minMaxState` 0 `maxColor`, 1 `maxGradient` at `u`, 2 between `minColor` and `maxColor` by `r`, 3 between the two gradients by `r`; gradient keys `key0..7` with `ctime0..7` and `atime0..7` out of 65535, `m_NumColorKeys`, `m_NumAlphaKeys`, linear blending).

- [ ] **Step 1: Write the failing tests** (`tests/curves.test.js`)

- `hermite matches the game's transition curve`: keys `(0, 0, 0, 0)`, `(0.2, 0.19843943, 2.3194094, 2.3194094)`, `(1, 1, 0, 0)` give 0 at 0, 0.19843943 at 0.2, 1 at 1, 1 at 2, 0 at -1, and a value strictly between 0.19843943 and 1 at 0.6.
- `minMaxCurve states`: `{minMaxState: 0, scalar: 2}` gives 2; state 3 with `minScalar` 0.35 and `scalar` 0.5 gives 0.35 at r 0 and 0.425 at r 0.5; state 1 with `scalar` 25 and a curve 0.5 to -0.8 gives 12.5 at u 0; state 2 between curves `[0, 0.5] -> [1, -0.8]` and `[0, -0.5] -> [1, 0.8]` with `scalar` 25 gives 0 at u 0, r 0.5.
- `minMaxGradient`: a two-key gradient white to black, alpha 1 to 0, gives `[0.5, 0.5, 0.5, 0.5]` at u 0.5 (within 1e-3).
- `seeded is deterministic`: two `seeded(42)` give the same first five numbers, all in [0, 1).
- `toLinear`: 0.5 gives 0.2140 (within 1e-4), 0.04 gives 0.04 / 12.92.

- [ ] **Step 2: Run to verify they fail** (`node --test tests/curves.test.js`: module not found)
- [ ] **Step 3: Implement `math.js` and `curves.js`; change the CI step's command to `node --test`**
- [ ] **Step 4: Run to verify they pass** (`node --test tests/curves.test.js`, then `node --test`: every file, including `model.test.js` and `i18n.test.js`, passes)
- [ ] **Step 5: Commit** (`feat(editor): add the preview's vector maths and Unity curves`)

---

### Task 2: Cache file parsers

**Files:**
- Create: `tools/ChartEditor/ui/preview/cachefiles.js`
- Test: `tools/ChartEditor/ui/tests/cachefiles.test.js`

**Interfaces:**
- Produces: `parseMesh(buffer: ArrayBuffer) -> { vertexCount, attributes: { [name]: Float32Array }, sizes: { [name]: number }, indices: Uint16Array | Uint32Array, submeshes: [[start, count]], bounds: { center, extent } }` (part 1's `.bin`: header line, then each attribute as Float32 in header order, then the indices); `parseTexture(buffer) -> { format, width, height, mips, colorSpace, filter, wrap: [u, v], data: Uint8Array }`; `SUPPORTED_FORMATS = new Set([1, 3, 4, 10, 12, 17, 24, 25])`; `textureKind(format) -> "s3tc" | "bptc" | "half" | "plain" | null`.

- [ ] **Step 1: Write the failing tests** (a helper builds buffers exactly as part 1 does: compact JSON, spaces to a multiple of 4 including the `\n`, then the data)

- `parses a two-attribute mesh`: header `{vertexCount: 3, attributes: {position: 3, uv0: 2}, index: "u16", submeshes: [[0, 3]], bounds: {center: [0, 0, 0], extent: [1, 1, 1]}}`, positions 1..9, uvs 0.1..0.6, indices `[0, 1, 2]`: the arrays come back equal, `indices` is a `Uint16Array`.
- `32-bit indices`: `index: "u32"` gives a `Uint32Array`.
- `parses a texture`: header `{format: 10, width: 8, height: 4, mips: 2, colorSpace: 1, filter: 1, wrap: [0, 1]}` and 40 bytes: `data` holds the 40 bytes, `textureKind(10) === "s3tc"`.
- `unknown format`: `textureKind(28)` and `textureKind(99)` are `null`; `textureKind(17) === "half"`, `textureKind(24) === "bptc"`, `textureKind(4) === "plain"`.

- [ ] **Step 2: Run to verify they fail**
- [ ] **Step 3: Implement `cachefiles.js`** (`TextDecoder` up to the first `\n`; typed array views on the same buffer, copied when an offset is not aligned)
- [ ] **Step 4: Run to verify they pass**
- [ ] **Step 5: Commit** (`feat(editor): read the game data cache's mesh and texture files`)

---

### Task 3: Level path

**Files:**
- Create: `tools/ChartEditor/ui/preview/path.js`
- Test: `tools/ChartEditor/ui/tests/path.test.js`

**Interfaces:**
- Consumes: `math.js` (Task 1).
- Produces: `createPath(level, step = 0.5) -> { start, length, frameAt(d) -> { p, right, up, back }, place(point, d) -> [x, y, z], exactAt(d) }` from a `levels/<world>.json` (`start`, `length`, `pos`, `rot`, `acc`). `exactAt` is the prototype's `splineAt` (cubic Bezier walked by arc length, 20 `acc` steps a curve, up from the EaseInOutQuad slerp of the curve's two node rotations, then `LookRotation`), in three's axes; `frameAt` interpolates a table of `exactAt` every `step` units and renormalises; distances wrap modulo `length`. `place([x, y, z], d)` is `p + right x + up y` at `d` (a point on the straight track, x across, y up).

- [ ] **Step 1: Write the failing tests**

- `a straight path`: one curve from (0, 0, 0) to (0, 0, 30) (Unity), identity rotations, `acc` = 20 equal steps of 1.5: `frameAt(15).p` is (0, 0, -15) in three's axes, `up` (0, 1, 0), `back` (0, 0, 1).
- `the table follows the exact curve`: on a two-curve synthetic path with a 90 degree bend and a 30 degree bank, `frameAt(d)` stays within 0.01 of `exactAt(d)` for 200 distances.
- `a lap later is the same frame`: `frameAt(d)` equals `frameAt(d + length)` and `frameAt(d - length)`.
- `the void path` (owner's cache): `start` 210, `length` 2562.75 (0.01), and `frameAt` at 0, 500 and 2000 within 0.01 of `exactAt`.

- [ ] **Step 2: Run to verify they fail**
- [ ] **Step 3: Implement `path.js`** (port `splineAt`; the table holds p, right and up as Float32Arrays)
- [ ] **Step 4: Run to verify they pass**
- [ ] **Step 5: Commit** (`feat(editor): walk the level path for the preview`)

---

### Task 4: The walker's numbers

**Files:**
- Create: `tools/ChartEditor/ui/preview/rig.js`
- Test: `tools/ChartEditor/ui/tests/rig.test.js`

**Interfaces:**
- Produces: `readRig(settings, walkerRoot) -> Rig` from `settings.json` (`walker.CameraController[0].fields`, `walker.Steering`, `walker.PlacePickupsOnPath`) and the `PlayerWalkerGlobal` tree (`objects/PlayerWalkerGlobal.json` root). `Rig` has the prototype's `G` fields: `walkerY`, `pickupY`, `roadY`, `hitAhead`, `camOrigin`, `shipLocal`, `coast`, `roll`, `fly` (`follow`, `offset`, `look`, and for fly `offsetAtPickup`, `pickupDuration`), `camTransition`, `zComp`, `laneX`, `pickupLaneX`, `tri`, plus the curves `transition`, `flyOffset`, `flyControl` as `curves.js` key lists, and `tiers` (below). Each value is read from the cache where the cache has it; the others come from `RIG_CONSTANTS` in the same file, each with a comment naming its source in the game. `TIERS` (in code, from the game's `PlayerLevelPump`): speeds 30, 35, 43, 49; ground FOV 0, 10, 15, 30 over a default FOV of 60; pump limits 5, 5, 5, 30; pump +3 per perfect note.

- [ ] **Step 1: Write the failing tests**

- `falls back to the constants`: `readRig({}, null)` gives every field, and equals `RIG_CONSTANTS` plus `TIERS`.
- `the owner's cache gives the prototype's numbers` (owner's cache): `walkerY` -2.5, `pickupY` -3, `roadY` -3.3333, `hitAhead` 1, `camOrigin` [0, 2.67, -5.39], `shipLocal` [0, 0.12, -2.723], `coast` {follow [1, 0.5, 1], offset [0, 1.5, -3.5], look 10}, `roll` {follow [0, 0, 1], offset [0, 1.5, -2], look 6}, `fly` {follow [0.9, 0.98, 0], offset [0, -2.5, -5], offsetAtPickup [0, -1, -15], pickupDuration 4.5, look 50}, `camTransition` 1, `zComp` 0.05, `laneX` 4.4933, `pickupLaneX` 4.5, `tri` [[0, 7.62], [-6.57, -3.81], [6.57, -3.81]] (all within 0.001); `transition(0.2)` 0.19843943; `flyOffset` keys equal the prototype's `FLY_OFFSET` keys.

- [ ] **Step 2: Run to verify they fail**
- [ ] **Step 3: Implement `rig.js`** (look the fields up in the cache; a value the cache does not hold stays in `RIG_CONSTANTS` and is ledgered as a ruling)
- [ ] **Step 4: Run to verify they pass**
- [ ] **Step 5: Commit** (`feat(editor): read the walker's camera and lane numbers from the game data`)

---

### Task 5: The perfect run

**Files:**
- Create: `tools/ChartEditor/ui/preview/run.js`
- Test: `tools/ChartEditor/ui/tests/run.test.js`

**Interfaces:**
- Consumes: `model.js` (`modeAt`, `sectionIndexAt`, `secondsPerBeat`, `isTurn`), `Rig` (Task 4).
- Produces: `prepareRun(doc, diff, rig, { tier = null } = {}) -> Run` with
  - `notes`: the difficulty's notes sorted by time, each with `mode` (its section's) and `levelUp` (true for the level-up pickups);
  - `levels`: `[{ t, level }]`, the level-ups' times (level 0 from the start);
  - `levelAt(t)`, `speedAt(t)`, `fovAt(t)`, `distAt(t)` (distance travelled since time 0; `distAt(t) = speedAt(0) x t` for t < 0);
  - `noteDist(noteT, t) = distAt(t) + (noteT - t) x speedAt(t) + rig.hitAhead` (the game's `Pickup.UpdatePosition`);
  - `visible(t) -> notes` with `noteT` in `[t - 0.15, t + 3]`, Freefly rings up to `t + 5`, holds until `t` passes their end plus 1 s;
  - `sections` (`{ mode, from, to }`), `modeAt(t)`, `sectionIndex(t)`.

  Rules, from the game: every note adds 3 to the pump (`PlayerScore`'s max-score simulation calls `AddPump(isPerfect: true)` for each); the pump stops at the level's limit (5, 5, 5, 30); when it fills at note time `n`, `b = firstBeat + round((n - firstBeat) / spb) x spb`, then `b + 3 spb` if `b >= n`, else `b + 4 spb`; the level-up pickup is the first pickupable note (not a plain strum: arrows, turns, Freefly rings and hold starts) strictly after that time; taking it raises the level (up to 3) and resets the pump; at level 3 the pump still fills (limit 30) and marks a level-up pickup, which resets the pump without raising the level. A level change at `T` tweens the speed linearly from the old to the new speed over 1 s (`Walker.SetSpeed`) and the FOV likewise (`GameCamera.AnimateGroundValues`, 1 s). `distAt` integrates `speedAt` exactly (piecewise linear). With `tier` set, level, speed and FOV stay at that tier and there is no level-up.

- [ ] **Step 1: Write the failing tests** (synthetic charts built with `model.js`'s `fromFile`; 120 bpm, first beat 0)

- `two perfect notes fill the pump`: notes at 1, 1.5, 2, 2.5, 3, 4 s: the pump fills at 1.5 s; the rounded beat 1.5 is not before 1.5, so 3 beats on: 3.0 s; the level-up pickup is the note at 4 s (strictly after 3.0); `levels` is `[{t: 0, level: 0}, {t: 4, level: 1}]`.
- `a plain strum is not the level-up pickup`: as above with the 4 s note a plain strum and one more arrow at 4.5: the level-up pickup is at 4.5.
- `speed and distance`: with a level-up at 4 s, `speedAt(3.9)` 30, `speedAt(4.5)` 32.5, `speedAt(5)` 35; `distAt(4)` 120; `distAt(5)` 152.5; `distAt(-1)` -30.
- `fov`: `fovAt(0)` 60, `fovAt(5)` 70.
- `the top level`: a chart of 60 arrows a beat apart reaches level 3; 10 notes after that (pump 30) a later note is still marked `levelUp`, and `levelAt` stays 3.
- `notes slide with the speed`: `noteDist(6, 4.5) = distAt(4.5) + 1.5 x 32.5 + 1`.
- `a fixed tier`: `prepareRun(doc, "Hard", rig, { tier: 3 })` gives `speedAt(t)` 49 and `fovAt(t)` 90 everywhere, no level-up notes.
- `odd charts`: an empty difficulty, notes at -1 s and past `doc.end`, a single Freefly section: `prepareRun` returns, `distAt`, `speedAt`, `visible` work at -5, 0 and `doc.end + 5`.
- `prepares a dense chart under 50 ms`: 4 minutes, 1500 notes, sections switching every 20 s: `prepareRun` takes under 50 ms (median of 5 runs).

- [ ] **Step 2: Run to verify they fail**
- [ ] **Step 3: Implement `run.js`** (speed as a sorted list of linear pieces; `distAt` sums whole pieces, cached prefix sums, binary search)
- [ ] **Step 4: Run to verify they pass**
- [ ] **Step 5: Commit** (`feat(editor): simulate a perfect run for the preview`)

---

### Task 6: The ship

**Files:**
- Create: `tools/ChartEditor/ui/preview/ship.js`
- Test: `tools/ChartEditor/ui/tests/ship.test.js`

**Interfaces:**
- Consumes: `Run` (Task 5), `Rig` (Task 4), `createPath` (Task 3), the roll clips (`clips/RollRightLong.json`, `clips/coaster_RollRightLong2Turns.json`, `clips/coaster_RollRightLong3Turns.json`), the level's `fly` areas.
- Produces: `prepareShip(run, rig, level, rolls) -> Ship` with
  - `lane(t, mode) -> { lane, prev, next }` and `laneAfter(note, mode)` (the prototype's `shipLane`, `laneAfter`; List lanes wrap, Bounce lanes clamp);
  - `bounceX(t)`, `flyArea(d)` (the prototype's, on `run.distAt`), `ringAt(t) = flyArea(level.start + run.distAt(t) + rig.hitAhead)`;
  - `flight(si) -> { frames, picks }`, simulated once per Freefly section and cached (the prototype's `flySim`, with `splineZ(now)` replaced by `level.start + run.distAt(now)`);
  - `rollAngles(clip) -> { length, fps, angles }`: a roll clip's ship rotation track (attribute `rotation`) turned into the unwrapped roll angle in degrees, 60 frames a second;
  - `barrelAt(si, t)`, `flyStretch(si, t)`, `flyOffsetAt(t)` (the prototype's);
  - `state(t) -> { si, mode, sl: [x, y, z], rot: [pitch, yaw, roll], barrel }` (the prototype's `shipState`, Unity axes for `sl`, degrees);
  - `world(t, path) -> { pos: [x, y, z], q: [x, y, z, w] }`: the ship model's place and turn in three's axes, bent along the path (the prototype's `shipWorld` followed by `rigidPoint` and `headingQ` at the walker's distance; `shipDY` -1 kept, as the prototype).

- [ ] **Step 1: Write the failing tests**

- `roll clips` (owner's cache): `rollAngles` of the three clips last 1.567, 1.933 and 2.6 s and end at 360, 720 and 1080 degrees in absolute value (within 2).
- `the same time gives the same state`: on a synthetic chart with a Freefly section of 6 rings, `state(t)` for 50 times is identical whether the times are asked in order, in reverse or at random.
- `a ring is taken within 1/30 s of its time`: every pick of `flight(si)` lies in `[ring.t - 1/30, ring.t - 1/30 + 1/60]`.
- `Bounce sway`: a turn right at 2 s moves `bounceX` from 0 to `rig.laneX` between 1.7 and 2 s, smoothstep in between.
- `odd charts`: Freefly from time 0, two Freefly sections in a row, a Freefly section with no ring, `t` before 0 and past the end: `state` and `world` return finite numbers.

- [ ] **Step 2: Run to verify they fail**
- [ ] **Step 3: Implement `ship.js`** (port the prototype's functions on plain arrays; `rolls` maps 1, 2, 3 turns to the clips)
- [ ] **Step 4: Run to verify they pass**
- [ ] **Step 5: Commit** (`feat(editor): fly the ship through the perfect run`)

---

### Task 7: The camera

**Files:**
- Create: `tools/ChartEditor/ui/preview/camera.js`
- Test: `tools/ChartEditor/ui/tests/camera.test.js`

**Interfaces:**
- Consumes: `Run`, `Ship`, `Rig`, the path.
- Produces: `cameraPose(run, ship, rig, path, t) -> { position, target, up, fov }` in three's axes: the prototype's `rigPos` and `pose` (the mode change over `rig.camTransition` with `rig.transition`, the Fly aim pulled half way to the ship, `zComp` from `run.fovAt(t)`), then its `curveScene` part (rigid on the walker's frame, aim at the path point ahead lerped to the ship, sideways slide `x += 0.06 (3 dot - x)`, up = the path's up), `fov = run.fovAt(t)`.

- [ ] **Step 1: Write the failing tests**

- `straight path, Bounce, tier 0`: on a straight synthetic path, at a time well inside a Bounce section with the ship on the middle lane, `position` is the walker's place plus `camOrigin + coast.offset` (axes converted), `target` is 10 ahead of the walker, `up` (0, 1, 0), `fov` 60.
- `a mode change takes camTransition`: List to Bounce at 10 s: the pose at 10.5 s lies strictly between the List pose and the Bounce pose (both computed at 10.5 s), and equals the Bounce pose from 11 s on.
- `bends turn the camera`: on the two-curve test path, `up` equals the path's up at the walker's distance.

- [ ] **Step 2: Run to verify they fail**
- [ ] **Step 3: Implement `camera.js`**
- [ ] **Step 4: Run to verify they pass**
- [ ] **Step 5: Commit** (`feat(editor): place the preview camera like the game's rig`)

---

### Task 8: Particle systems

**Files:**
- Create: `tools/ChartEditor/ui/preview/particlespec.js`, `tools/ChartEditor/ui/preview/particles.js`
- Test: `tools/ChartEditor/ui/tests/particles.test.js`

**Interfaces:**
- Consumes: `curves.js`, `math.js`.
- Produces:
  - `particleSpec(system, renderer, material) -> Spec` from a cache `ParticleSystem` component's `fields`, its `ParticleSystemRenderer` `fields` and its first material: `count` (the burst count of `EmissionModule.m_Bursts[0].countCurve`, or `rateOverTime x lengthInSec`), `life`, `speed`, `size` (`InitialModule`), `shape` (`ShapeModule.type`: 4, 8 cone and cone volume as `cyl` with `radius`, `length`, `m_Scale`; 10 circle; 12 single sided edge as `edge`; 0 sphere; anything else `point`), `vol` and `force` per axis (`minMaxCurve` of `VelocityModule.x/y/z`, `ForceModule.x/y/z`), `gravity` (`InitialModule.gravityModifier.scalar`), `dampen` (`ClampVelocityModule.dampen` when enabled), `color(u, r)` (`ColorModule.gradient` times the start colour when `startColor` matters), `sizeLife` (`SizeModule.curve` when enabled), `world` (`moveWithTransform` 1 is world), `maxSize` (renderer `m_MaxParticleSize`), and `mat` (`{ c: tint made linear, a: tint alpha, e: exp(5 x _EmissionGain), tex: _MainTex ref }`).
  - `emit(spec, frame, age, seed, level, out)` (the prototype's `emit`, with `curves.js` evaluation, integration at 60 steps a second): `frame = { o, q, shapeScale, sizeScale }` in three's axes; `out(texRef, position, rgb, size, maxSize)` per live particle; `count` arrays by level as in the prototype.

- [ ] **Step 1: Write the failing tests**

- `a synthetic edge burst`: a spec with 10 particles, life 1, an edge shape of radius 2, velocity z 5: at age 0.5, 10 particles, x within [-2, 2], z about -2.5 (three's axes), all identical across two calls with the same seed and different with another seed.
- `particles die`: at an age past the longest life, `out` is never called.
- `the pickup swirl as the prototype read it` (owner's cache): `particleSpec` of `XBOX Pickup Left X OPTIMIZED`'s `Swirly Particles` gives count 70, life [0.35, 0.5], size [0.1, 0.15], an edge shape of radius 1, `world` true, velocity x a two-curve 25 x [[0, 0.5], [0.55, 0], [1, -1]] / [[0, -0.5], [0.55, 0], [1, 1]] (key times and values within 0.01), material tint (0.11, 0.431, 1) before linearisation and gain 0.546; the walker's `PickupParticles/Level3` gives count 60 and life [0.33, 0.7]; `PickupParticlesLand` gives gravity -1 and dampen 0.2.
- `a dense moment is cheap`: 10 pickups' worth of the five pickup systems at age 0.3 emit in under 4 ms (median of 5).

- [ ] **Step 2: Run to verify they fail**
- [ ] **Step 3: Implement `particlespec.js` and `particles.js`**
- [ ] **Step 4: Run to verify they pass**
- [ ] **Step 5: Commit** (`feat(editor): emulate the game's particle systems from their modules`)

---

### Task 9: Trails and the level halo

**Files:**
- Create: `tools/ChartEditor/ui/preview/fxspec.js`
- Test: `tools/ChartEditor/ui/tests/fxspec.test.js`

**Interfaces:**
- Produces: `trailSets(shipObject, streakSettings) -> { streak: [Trail[] x 4], freefly: [Trail[] x 4], onSteer: Trail }` from `objects/Ship_Beginner.json` (TrailRenderer nodes: attach point in the ship's OldRoot space, `m_Time`, head and tail width, alignment `view` / `flat` / `upright` / `cross`, texture refs) and the ship's `ShipPickupStreakSettings` lists (`PickupStreakTier1-4`, `FreeFlyTier1-4`); `Trail = { p, time, w0, w1, align, tex, tex2 }` as the prototype's `TRAIL`. `haloLevels(pickupObject) -> [{ w, h, scale, pos, grey } x 3]` from the pickup's `Level Pickup Effect` sprite renderer and its level states, and `haloTint(colour)`.

- [ ] **Step 1: Write the failing tests** (owner's cache)

- `the Beginner's trails`: `streak[3]` has the four trails of the prototype's tier 4 (TrailBig, TrailWide upright, TrailRendererEngineLarge cross, SmallWindTrail), with the prototype's attach points (within 0.002), times, widths and alignments; `streak[0]` is empty; `freefly` has 0, 1, 2 and 3 trails; `onSteer` has time 0.2, widths 0.15 and 0.05.
- `the level halo`: `haloLevels` gives the prototype's `HALO_LEVEL` (w, h within 0.01; scale, pos, grey exact to 0.001).

- [ ] **Step 2: Run to verify they fail**
- [ ] **Step 3: Implement `fxspec.js`**
- [ ] **Step 4: Run to verify they pass**
- [ ] **Step 5: Commit** (`feat(editor): read the ship's trails and the pickup halo from the game data`)

---

### Task 10: Loading the cache into three.js, and the preview's first picture

**Files:**
- Create: `tools/ChartEditor/ui/preview/gamecache.js`, `tools/ChartEditor/ui/preview/materials.js`, `tools/ChartEditor/ui/preview/gamepreview.js`
- Modify: `tools/ChartEditor/ui/app.js` (`startPreview`, `gameDataChanged`)

**Interfaces:**
- Consumes: Tasks 1 to 9.
- Produces:
  - `loadGameCache(renderer, base = "/game/") -> Promise<GameCache>`: `index`, `prefab(name) -> Object3D` (a clone of the built prefab; at-rest nodes hidden as the prototype's `atRest`; missing prefab: a flat arrow `Mesh` in the note's colour), `level(world)`, `settings`, `clip(name)`, `texture(ref)`, `geometry(ref)`. Textures: S3TC and BPTC as `CompressedTexture` when `WEBGL_compressed_texture_s3tc` and `EXT_texture_compression_bptc` exist, format 17 as a half-float `DataTexture`, 1, 3, 4 as `DataTexture`; colour space from the header (1 sRGB); refused or unknown: `null`, and materials then use a plain colour. Geometries: axes and winding converted, vertex colours made linear. Unity built-ins (`{"builtin": ...}` refs): path id 10210 (Quad) is a 1 x 1 `PlaneGeometry`; any other built-in mesh is that plane too, and built-in materials are plain additive white.
  - `materialFor(materialJson, cache, { glow, tint }) -> Material` (the prototype's `materialFor` on the cache's material JSON: Sonic Ether additive with mask, emissive vertex colours, emissive textured `exp(10 x gain)`, the ship's shader, default); `particleMaterial(texture)`, `ribbonMaterial(texture)`, `holdWallMaterial(texture)` (the prototype's shaders).
  - `class GamePreview { constructor(canvas, { skin }); static async create(canvas, opts) ; setChart(doc, diff); setSelection(isSelected); resize(w, h); render(t); dispose(); options = { tier: null }; stats = { frameMs, prepareMs } }`: here with the lab background and lights, the ship (Beginner prefab, scale 2.1 x as the prototype) placed by `ship.world`, the camera by `cameraPose`; track, notes, effects and post come in Tasks 11 to 13.
  - `app.js`: `startPreview` creates `GamePreview` when `this.gameDataState === "ready"` (asked with `host.gameData` before the first preview), else `Preview3D`; `gameDataChanged("ready")` swaps a running `Preview3D` for a `GamePreview`; a `GamePreview.create` rejection or an exception from its `render` disposes it, puts `Preview3D` back and shows `t("gameDataFailed")` as an error, once per session.

- [ ] **Step 1: Check the start**: in the editor built from the worktree, with the cache ready, the preview panel shows the lab background; the console shows `GamePreview` loaded.
- [ ] **Step 2: Implement `gamecache.js` and `materials.js`, the ship and camera in `gamepreview.js`, and the `app.js` switch**
- [ ] **Step 3: Check in the editor**: `dotnet build tools/ChartEditor/ChartEditor.csproj -c Debug`, start the editor, open a song: the Beginner ship flies through the lab along the void path, the camera follows it as in the prototype; scrubbing changes the view; temporarily renaming `objects/Ship_Beginner.json` in the cache (renamed back afterwards) shows the stand-in arrow instead and nothing else breaks; the interface tests still pass (`node --test`).
- [ ] **Step 4: Commit** (`feat(editor): show the game's ship and camera in the preview`)

---

### Task 11: Track and lab decor

**Files:**
- Create: `tools/ChartEditor/ui/preview/track.js`
- Modify: `tools/ChartEditor/ui/preview/gamepreview.js`

**Interfaces:**
- Consumes: `Run`, `Path`, `GameCache`, `Rig`.
- Produces: `createTrack(cache, rig) -> { prepare(run, doc), update(scene, run, path, t), dispose() }`. Chunks are 100 units of straight-track distance, keyed by `Math.floor(distance / 100)` (straight distance, never wrapped, so laps reuse nothing and leave no seam); a chunk holds, for the sections over its distance range: the List corner lines and the lab's smoked walls (the prototype's `buildTunnel`, subdivided every 3 units), the Bounce road meshes from `levels/void.json` `road` (mesh, material, scale, offset; the prototype's `buildRoad` spacing 1.1554 x 13), the lab floor strip, blocks and arches (the prototype's lab decor, beats placed at `run.distAt(beat)`). Vertices are bent once with `path.place`; normals with the path frame. Chunks from 2 behind to 12 ahead of the walker are shown; at most 24 are kept (least recently used freed with `dispose`).

- [ ] **Step 1: Implement `track.js` and add it to `GamePreview`** (`prepare` on `setChart` clears the chunks)
- [ ] **Step 2: Check in the editor**: List shows the white corner lines and smoked walls, Bounce the game's road, the lab blocks and orange arches line the track and follow the bends; a 10-minute song played past one lap shows no seam; `stats.frameMs` stays under 16 ms while playing.
- [ ] **Step 3: Commit** (`feat(editor): draw the tunnel, road and lab decor along the level path`)

---

### Task 12: Notes and selection

**Files:**
- Create: `tools/ChartEditor/ui/preview/notes.js`
- Modify: `tools/ChartEditor/ui/preview/gamepreview.js`

**Interfaces:**
- Consumes: `Run`, `Ship`, `Path`, `GameCache`, `Rig`, the editor skin (`skin.url` of the key pictures, as `preview3d.js` uses them).
- Produces: `createNotes(cache, rig, skin) -> { prepare(run), update(scene, run, ship, path, t), setSelection(isSelected), dispose() }`. One object per note per chart from pools by prefab (`XBOX Pickup Left X/Right B/Down A/Up Y OPTIMIZED`, `Pickup 4 Left`, `Pickup 5 Right`, `Pickup Strum, List/Bounce OPTIMIZED`, `Pickup StrumHold Start, List/Bounce OPTIMIZED`, `PickupStrumHoldEnd, List/Bounce, All Platforms`, `BeatLine, List/Bounce`, `PickupAir, All Platforms OPTIMIZED`), reused across `prepare` calls; placed each frame at `path.place` of `run.noteDist(note.t, t)` with the prototype's lane offsets and List roll (`place`); Freefly rings at `ship.ringAt(note.t)` with the spawn animation's inner scale 9.55; beat lines from `t - 0.2` to `t + 1`, not in Freefly; turns show their Toblerone or Bounce node by mode; the key picture on the arrow's `SymbolIcon` node; a level-up note shows its `Level Up Text` node; the turn guide arrows (`PickupTurnGuide`, `PickupTurnGuideGhost`, crunched textures) get a canvas arrow texture in the turn colour instead; hold end bars at `noteDist(t + len)`. Selection: a selected note's meshes get a back-face copy 1.06 times larger in a white unlit material, from a pool.
- Also checks the List left arrow tint at 45 s of the comparison song against `MaterialsDatabase` (`controlType` 2, `materialPickupLeft`): blue is right; a magenta arrow is a mapping error to fix here.

- [ ] **Step 1: Implement `notes.js` and add it to `GamePreview`**
- [ ] **Step 2: Check in the editor**: arrows in their XBOX colours with key pictures, turns, strums, holds with their end bars, beat lines, Freefly gates; notes slide forward when the level rises; the level-up pickup shows its text; selecting notes in the timeline outlines them in white; 100 edits in a row keep `renderer.info.memory.geometries` steady.
- [ ] **Step 3: Commit** (`feat(editor): place the game's notes in the preview`)

---

### Task 13: Effects and post-processing

**Files:**
- Create: `tools/ChartEditor/ui/preview/effects.js`, `tools/ChartEditor/ui/preview/post.js`
- Modify: `tools/ChartEditor/ui/preview/gamepreview.js`

**Interfaces:**
- Consumes: Tasks 5 to 9, `GameCache`, `materials.js`.
- Produces: `createEffects(cache, rig) -> { prepare(run, ship), update(scene, camera, run, ship, path, t), dispose() }`, which builds its particle specs (`particleSpec`), trail sets (`trailSets`) and halo levels (`haloLevels`) from the cache once: the prototype's `particles` (walker PickupParticles/LevelN, StrumParticles/LevelN, PickupParticlesLand, each pickup's Swirly, Multiplier, flash and Extra Glow, the strum's LineBokehParticles; MaterialSwitcher's last arrow colour), one `Points` per texture with preallocated buffers (`setDrawRange`), positions bent with `path.place`; `levelHalos` (level from `run.levelAt(note.t)`); the landing glow; the trails (`trailSets`, by `run.levelAt(t)`, Freefly sets and OnSteer from `ship.flyStretch`) as ribbons in one preallocated buffer per texture, cut at the near plane, faded over 1 unit; the hold walls (the prototype's `holdWalls`, between `noteDist(t0)` and `noteDist(t0 + len)`, bent, scrolling with `t`). `createPost(renderer, scene, camera) -> { resize(w, h), render() }`: RenderPass, UnrealBloomPass (strength 0.06, radius 0.6, threshold 2), the prototype's vignette (0.45) and chromatic aberration (0.15) ShaderPass, OutputPass; tone mapping off.

- [ ] **Step 1: Implement `effects.js` and `post.js` and add them to `GamePreview`**
- [ ] **Step 2: Check in the editor**: bursts and swirls on taken notes in their colours, the strum lines, the land burst in Bounce, the ship's trails growing with the level, the halo, the hold walls lit along holds, the faint bloom, vignette and colour fringes; the same time shows the same picture after scrubbing away and back.
- [ ] **Step 3: Commit** (`feat(editor): add the game's particles, trails, halo and post-processing to the preview`)

---

### Task 14: Performance and the side-by-side check

**Files:**
- Modify: whichever preview module a measurement points at

- [ ] **Step 1: Measure**: play a dense song (tier 4 reached, Freefly stretches) in the editor at the default panel size; `gamePreview.stats.frameMs` (exposed as `window.gamePreview` in the console) stays under 16.7 ms at the busiest moments and `stats.prepareMs` under 50 ms after an edit; 100 edits in a row keep `renderer.info.memory` steady.
- [ ] **Step 2: Fix what the measurement shows**, each fix with its numbers before and after in the commit message.
- [ ] **Step 3: Resize**: the panel dragged to its minimum and back, and Windows at 150 % scale: the picture fills the panel, bloom at the right size.
- [ ] **Step 4: Side by side**: with `gamePreview.options.tier = 3` (the prototype's fixed tier) and the prototype's song, render 45 s, 70 s and 83 s in both (the prototype at `?t=45` etc. through its server `node serve.mjs <ui folder>`), save the pairs, and show them to the owner, who decides.
- [ ] **Step 5: Run every test** (`node --test` in `tools/ChartEditor/ui`; `dotnet test tests/InvectorModKit.ChartEditor.Tests`) **and commit** (`perf(editor): keep the game preview at 60 frames per second`, if anything changed)

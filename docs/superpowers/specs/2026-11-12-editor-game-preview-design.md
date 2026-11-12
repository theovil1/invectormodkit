# Level editor: the game's view in the 3D preview (part 2 of 3)

Date: 2026-11-12. Status: design approved section by section in chat, spec waiting for the owner's review.

## Context

Part 1 (`2026-11-12-editor-game-data-design.md`, branch `feat/editor-game-data`) gives the editor a local cache of the
game data, served to the page under `/game/`: prefabs with their components, meshes, textures, animation clips, the
10 worlds' level paths and the levelmodule settings. A throwaway prototype (kept on the owner's PC, never in the
repository) rebuilt the game's view from the same data and matched recorded game frames closely, but it renders one
still frame at a time and rebuilds the whole track for each frame.

This part puts that view in the editor's 3D preview panel, in real time. Part 3 adds the preview settings.

## Goal

While editing a chart, the preview panel shows what the game shows at the playhead, for a player who plays it
perfectly, at 60 frames per second during playback.

Success criteria:

- With the cache ready, the preview shows the game's track, ship, notes and effects at the playhead time; moving the
  playhead shows the exact state at that time (the same time always gives the same picture).
- During playback the preview holds 60 frames per second on the owner's PC at the panel's size, at the busiest
  moments (tier 4, dense notes, Freefly).
- After an edit the preview is up to date within one frame: preparing a chart takes under 50 ms for a typical song (4 minutes, about 600 notes).
- Side by side with the prototype at the same times, the owner judges the picture the same.
- Without a cache, or if the page cannot load it, the current preview stays, as today.

## Owner's decisions

| Topic | Decision |
|---|---|
| Speed and tier | A perfect run: the score tier rises as for a player who takes every note perfectly, so speed, camera and trails change through the song as the game's do (a fixed tier is a part 3 setting) |
| Selected notes | Shown as in the game, plus a thin white outline |
| While paused | A still, exact frame at the playhead; redrawn only when something changes |
| How | A prepared scene: per chart, prepare once; per frame, move only what moves; small tested modules |
| Tests in CI | The new interface test files are added to the CI workflow's `node --test` command |

Fixed until part 3: faithful effects, the XBOX look (left blue, right red, down green, up yellow, as the game shows a
keyboard player), the void world's path, the bright prototype "lab" decor around the track.

## Non-goals

- Settings (calm effects, decor, world, fixed tier, PlayStation look): part 3.
- The level-up pickup itself: it is not in part 1's recipe, so it is not drawn; the tier still rises at its time.
  Drawing it means adding it to the recipe (a new RecipeVersion).
- The known differences between the prototype and the game (pickup particle cloud less dense, weaker bloom around
  the ship, tunnel walls without the world behind them), except the List-mode left arrow tint (see Content).
- Game world decor, other ships, audio.

## Architecture

```
 app.js ── startPreview ──> GamePreview (cache ready)  or  Preview3D (today's preview, otherwise)
                              │  setChart(doc, diff) · setSelection(fn) · resize(w, h) · render(t) · dispose()
                              ├─ gamecache.js   /game/ files -> three geometries, textures, materials, prefab objects
                              ├─ materials.js   the game's shaders as three materials
                              ├─ path.js        the level path and the bend of the track
                              ├─ run.js         the perfect run: tier, speed, distance, Freefly flight, rolls, camera
                              ├─ track.js       tunnel, road and lab decor, bent, in cached chunks
                              ├─ notes.js       pickups, strums, holds, beat lines, gates, selection outline
                              ├─ effects.js     particles, trails, level halo, hold walls
                              └─ post.js        bloom, vignette, chromatic aberration
```

All new files are in `tools/ChartEditor/ui/preview/`. `GamePreview` has the same interface as today's `Preview3D`
(`setChart`, `setSelection`, `resize`, `render`, `dispose`), so `app.js` only chooses which one to create: the game
preview when the host reports the game data `ready` (part 1's `host.gameData` and `gameData` event), today's preview
otherwise; it switches to the game preview when the state becomes `ready` during the session. Today's `preview3d.js`
stays unchanged as the fallback.

| Unit | Does | Depends on |
|---|---|---|
| `gamecache.js` | Reads `index.json`, the prefab, level and settings files, `.bin` meshes and `.tex` textures (part 1's formats); builds three objects from prefab trees; maps Unity's built-in resources (`builtin-<path id>`) to three equivalents | three, `materials.js` |
| `materials.js` | Sonic Ether additive particles and trails (`2 x colour x tint x texture x exp(5 x gain)`, near fade), emissive textured (`exp(10 x gain)`), the hold wall shader, a lit material for the ship | three |
| `path.js` | The level's cubic Bezier path walked by arc length, up from its node rotations; places a point given along the straight track onto the path | the level file |
| `run.js` | The perfect run and everything that follows from time alone: tier and speed timeline, walker distance, note places, Freefly flight, barrel rolls, camera rig pose | `path.js`, settings |
| `track.js` | Builds the List tunnel, the Bounce road and the lab decor as bent chunks around the walker, kept in a bounded cache | `path.js`, `materials.js` |
| `notes.js` | Creates one object per note per chart (pooled across charts), places the visible ones each frame, outlines the selected ones | `gamecache.js`, `run.js` |
| `effects.js` | Particle systems emulated from their modules with seeded randomness, trails as ribbons, the level halo, hold walls | `gamecache.js`, `run.js`, `materials.js` |
| `post.js` | The game's post-processing on an HDR target | three addons already vendored |
| `gamepreview.js` | `GamePreview`: loads the cache once, prepares per chart, draws per frame | all of the above |

Values present in the cache (camera rig, steering, fly areas, path, trails, particle modules, materials) are read
from it. Values the game keeps in its code (tier speeds 30/35/43/49 and fields of view 60/70/75/90, pump limits,
transition curves) are written in the preview's code, each with a comment naming where it comes from.

## Data flow

**Once, when the cache is ready:** `GamePreview` loads `index.json`, the prefabs, their meshes and textures (uploaded
to the GPU), the void level and `settings.json`, and keeps them.

**Per chart** (`setChart`, which the editor calls after each edit), under 50 ms for a typical song:

- The perfect run, by the game's rules: each perfect pickup adds 3 to the pump (`PlayerLevelPump`) up to the
  level's limit; the level-up pickup then raises the level; the walker's speed follows the level (`Walker.SetSpeed`,
  a tween) and its speed scale. The exact timings (when the level-up pickup comes, the tween's length) are taken
  from the game's code while writing the plan and pinned by tests.
- The walker's distance along the path, tabulated over the song.
- Each Freefly stretch's flight, simulated at 60 steps a second from the stretch's start (the prototype's
  `Steering.UpdateFreeFly` with the AutoPilot), with its barrel rolls and camera pull-backs.
- The note objects, created once and reused.

**Per frame** (`render(t)`): the walker's place and turn on the path at its distance; the camera rig and the ship;
the visible notes, each `(note time - t) x current speed` ahead of the walker as the game places them
(`Pickup.UpdatePosition`), so notes slide forward when the speed rises; the track chunks around the walker (built on
demand, cached); particles, trails, halo, hold walls; post-processing. Nothing depends on earlier frames.

The editor already redraws the preview only when something changed, or every frame during playback: paused, the
picture is a still frame.

## Content

Everything the prototype shows, read from the cache:

- Track: the List tunnel (triangle and corner lines), the Bounce road, the open Freefly space, bent along the void
  path; the lab decor (far floor, blocks, an arch every 4 bars).
- The Beginner ship: model, steering lean, barrel rolls (1, 2 or 3 turns from the roll clips); the camera rig of each
  mode (`CameraController`), its field of view by tier, the mode transitions, the Freefly pull-back.
- Notes: the XBOX arrows with the editor's keyboard key pictures, turns, strums and holds (List and Bounce prefabs)
  with their hold walls, beat lines, Freefly gates with their spawn animation. The List-mode left arrow tint (blue in
  the game, magenta in the prototype at 45 s) is checked against `MaterialsDatabase` and fixed if it is a mapping
  error.
- Effects: the particle systems (the walker's PickupParticles, StrumParticles and PickupParticlesLand, each pickup's
  effects, the strum's bokeh particles), the ship's trails by tier and in Freefly, the level halo after a taken
  note, bloom, vignette and chromatic aberration.
- Selection: a selected note keeps its look and gets a thin white outline (its meshes drawn again slightly larger,
  back faces only), with no new three.js file.

Fallbacks: a recipe item missing from the cache is drawn as a simple shape in its colour (a flat arrow for a note);
the turn guide arrows, whose textures are crunched, are drawn as flat arrows in the turn colour; Unity's built-in
quad and default particle material are made directly in three.js.

## Errors

- The page cannot load the cache (a missing or damaged file, a GPU without the compressed texture formats): the
  editor keeps today's preview and the status bar shows part 1's `gameDataFailed` message; details go to the console.
- A texture the GPU refuses: that material uses a plain colour.
- An exception while drawing: the editor switches back to today's preview, once, with the same message, instead of a
  black panel.

## Performance

- 60 frames per second on the owner's PC during playback, at the busiest moments.
- No object allocation per frame: geometries, materials and note objects are pooled; all particles share one
  buffer, all trails another; track chunks live in a bounded cache.
- An internal counter of frame time and preparation time, readable from the console, to check both.

## Testing

Test first, as the repository asks. In `tools/ChartEditor/ui/tests/` (`node --test`, no browser):

- `path.js`: a synthetic two-curve path: positions, arc length, up vector, a looped path.
- `run.js`: on a synthetic chart, the tier timeline (pump +3 per perfect pickup, limits, level-up), the speed
  tween, the walker distance, note places by the game's rule, the Freefly flight (same time, same state; a ring is
  taken within 1/30 s of its time), the roll choice.
- `gamecache.js`: parsing synthetic `.bin` and `.tex` buffers in part 1's format, prefab trees to object trees.
- `effects.js`: a particle system with a fixed seed gives the same particles at the same time.
- Tests on the owner's cache (skipped when `%LOCALAPPDATA%\InvectorModKit\LevelEditor\game` is absent, as in CI):
  the void path length 2562.75 from start 210, the prototype's camera and ship numbers.
- Preparation time under 50 ms on a dense synthetic chart (4 minutes, 1500 notes, with Freefly stretches).

Visual check on the owner's PC: the same times rendered by the prototype and by the editor, side by side; the owner
decides. Frame rate checked with the internal counter during playback of a dense song.

CI: the new test files are added to the `node --test` command of `.github/workflows/ci.yml` (approved by the owner).

## Dependencies and rules

- No new package: three.js 0.170 and its addons already vendored in `ui/vendor/three/` (EffectComposer, RenderPass,
  ShaderPass, UnrealBloomPass, OutputPass).
- WebGL 2 with `WEBGL_compressed_texture_s3tc` (DXT1, DXT5) and `EXT_texture_compression_bptc` (BC6H, BC7); RGBA16F
  for format 17.
- Nothing from the game in the repository: tests use synthetic data or the owner's local cache.
- New interface texts, if any, in all 11 languages; no em dash anywhere.

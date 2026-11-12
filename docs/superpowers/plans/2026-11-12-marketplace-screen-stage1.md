# Marketplace Stage 1 (the screen and Discover) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the marketplace window with a full screen whose Discover tab finds, plays an excerpt of, and installs charts, isolated from the menu's input and music.

**Architecture:** Pure decisions (main button state, filters, excerpt window, yt-dlp section arguments, cache eviction, the
new `preview` field) go in `InvectorModKit.Marketplace.Client` with xunit tests. The Unity side (`InvectorModKit.Marketplace/Ui`)
gets a new `MarketplaceScreen` with a `DiscoverTab`, built from small pieces (`UiKit`, `KeyBar`, `ScreenInput`, `MenuAudio`,
`PreviewPlayer`, `PreviewCache`), and keeps today's "My songs" tab inside it until stage 2. The server (separate repository
`theovil1/invectormodkit-tools`) adds `preview` to chart summaries.

**Tech Stack:** C# (netstandard2.0 client, net472 BepInEx plugin, Unity 2021.3 legacy UI `Text`, Rewired, Harmony), xunit;
TypeScript Cloudflare Worker with D1 and vitest for the server.

**Spec:** `docs/superpowers/specs/2026-11-12-marketplace-redesign-design.md` (approved 2026-11-12).

## Global Constraints

- English in code, commits and pull requests; player texts in the 11 languages (en, fr, de, it, es, pt-BR, pl, ru, ja, ko, zh), no em dashes anywhere.
- No mention of AI tools in commits or pull requests; no Co-Authored-By line.
- Conventional Commits titles; branch `feat/marketplace-screen` (this repository), `feat/catalogue-preview-time` (server repository, from its `origin/main`, in its own worktree; that repository has uncommitted work of its own: never touch it).
- The server never stores audio; only the chart is sent.
- Excerpts: start at the chart's preview time, 20 s long, fade in 0.6 s, fade out 1.2 s, then loop; start after resting 0.8 s on a row.
- YouTube excerpt cache: `BepInEx/cache/InvectorModKit/previews`, files older than 7 days deleted, total above 200 MB trimmed oldest first.
- Labels: inner margin of at least 16 px on each side at a 1280 px wide reference, which is 24 units in the screen's 1920 x 1080 reference; shrink to fit (minimum 18 units), then two lines; never outside the button.
- Missing preview time (null, negative or past the end): 30 % of the song, as charts do.
- Builds of the plugins use `-p:DeployToGame=false` except the deploy step of Task 12, which asks the owner first.
- Never commit game data; never push to main; pull requests open ready (not draft).

## Review Focus

- A chart whose preview time is within 20 s of its end: the excerpt must end at the song's end, not past it (Task 3 test `Window_NearTheEndStaysInsideTheSong`).
- A song shorter than 20 s: the excerpt is the whole song (Task 3 test `Window_ShortSongIsWholeSong`).
- A fetch failing after the chart was downloaded (no internet, video gone): the main button must offer RETRY with the reason, not stay on "installing" (Task 5 test `State_FailedFetchOffersRetryWithReason`).
- The player's own chart that is also installed: YOURS wins, never INSTALL or UPDATE (Task 5 test `State_MineWinsOverInstalled`).
- Typing in the search field: F, Space, Tab, Esc must type or end typing, not trigger their screen actions (Task 10 step "typing owns the keys", checked in game in Task 12).

---

### Task 1: The server's chart summaries carry `preview`

**Repository:** `C:\Users\Theo\source\repos\invectormodkit-tools` (new worktree `..\invectormodkit-tools-worktrees\feat-catalogue-preview-time` from `origin/main`).

**Files:**
- Create: `server/migrations/0006_chart_preview.sql` (`ALTER TABLE charts ADD COLUMN preview REAL;`)
- Modify: `server/src/catalog.ts` (`ChartRow.preview`, `toSummary`, `search`, `findChart`), `server/src/publish.ts` (store it), `server/src/types.ts` (`ChartSummary.preview: number`)
- Test: `server/test/catalog.test.ts`, `server/test/publish.test.ts`

**Interfaces:**
- Produces: every summary (`GET /charts`, `GET /charts/:id`, `GET /me/charts`) has `preview` in seconds: the chart's `preview` field, else 30 % of `duration`.

- [ ] **Step 1: Write the failing tests**
  - `publish.test.ts`: `it("stores the chart's preview time")`: publish a chart whose JSON has `"preview": 61.5`, then `GET /charts/<id>` answers `summary.preview === 61.5`.
  - `catalog.test.ts`: `it("lists the preview time")`: a published chart with `"preview": 42` shows `preview: 42` in `GET /charts`.
  - `catalog.test.ts`: `it("fills a missing preview time from the stored chart")`: insert a chart row with `preview` NULL and a stored chart file with `"preview": 33`; the first `GET /charts` lists `33` and the row now holds 33.
  - `catalog.test.ts`: `it("falls back to 30 % of the song")`: a stored chart without `preview`, `duration` 200: listed as `60`.
- [ ] **Step 2: Run them** `cd server && npx vitest run test/catalog.test.ts test/publish.test.ts` Expected: the four fail (`preview` undefined).
- [ ] **Step 3: Implement**: the migration; `publish` writes `preview` (number from the chart, validated `0 <= p <= duration`, else 30 % of duration) in both the INSERT and the UPDATE; `toSummary` returns `preview`; `search` and `findChart` fill rows whose `preview` is NULL from `getChartFile(env, id, version)` (same rule), `UPDATE` them once, before building summaries.
- [ ] **Step 4: Run** `cd server && npm test && npm run typecheck` Expected: all pass, no type error.
- [ ] **Step 5: Commit and open the pull request** (`feat(catalogue): give each chart's preview time`). Deploying the server is the owner's call (`npm run deploy`): ask before running it. The game works without it (Task 3's fallback).

### Task 2: `ChartSummary.Preview` in the client

**Files:**
- Modify: `src/InvectorModKit.Marketplace.Client/Models.cs` (`public double? Preview;` on `ChartSummary`), `src/InvectorModKit.Marketplace.Client/MarketplaceApi.cs` (`Summary` reads `(double?)o["preview"]`)
- Test: `tests/InvectorModKit.Marketplace.Client.Tests/MarketplaceApiTests.cs`

**Interfaces:**
- Produces: `ChartSummary.Preview` (`double?`, null when the server does not send it).

- [ ] **Step 1: Write the failing tests** `Search_ReadsPreviewTime` (summary with `"preview":61.5` gives `61.5`) and `Search_PreviewMissingIsNull` (today's `Summary` constant gives `null`).
- [ ] **Step 2: Run** `dotnet test tests/InvectorModKit.Marketplace.Client.Tests -c Release --filter Preview` Expected: compile error, `Preview` missing.
- [ ] **Step 3: Implement** the field and its parsing.
- [ ] **Step 4: Run** the same command. Expected: 2 passed.
- [ ] **Step 5: Commit** `feat(marketplace): read each chart's preview time`.

### Task 3: `ExcerptWindow`

**Files:**
- Create: `src/InvectorModKit.Marketplace.Client/ExcerptWindow.cs`
- Test: `tests/InvectorModKit.Marketplace.Client.Tests/ExcerptWindowTests.cs`

**Interfaces:**
- Produces: `public readonly struct ExcerptWindow { public double Start, End; public const double Length = 20, FadeIn = 0.6, FadeOut = 1.2, Dwell = 0.8; public static ExcerptWindow For(double? previewTime, double songLength); }`

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Window_StartsAtThePreviewTime() { var w = ExcerptWindow.For(61.5, 240); Assert.Equal(61.5, w.Start); Assert.Equal(81.5, w.End); }
[Fact] public void Window_MissingPreviewIsThirtyPercent() { Assert.Equal(60, ExcerptWindow.For(null, 200).Start); Assert.Equal(60, ExcerptWindow.For(-3, 200).Start); Assert.Equal(60, ExcerptWindow.For(250, 200).Start); }
[Fact] public void Window_NearTheEndStaysInsideTheSong() { var w = ExcerptWindow.For(190, 200); Assert.Equal(180, w.Start); Assert.Equal(200, w.End); }
[Fact] public void Window_ShortSongIsWholeSong() { var w = ExcerptWindow.For(5, 12); Assert.Equal(0, w.Start); Assert.Equal(12, w.End); }
```

- [ ] **Step 2: Run** `dotnet test tests/InvectorModKit.Marketplace.Client.Tests -c Release --filter Window_` Expected: compile error.
- [ ] **Step 3: Implement** `For` (fallback rule, then clamp `Start` to `[0, max(0, songLength - Length)]`, `End = min(songLength, Start + Length)`).
- [ ] **Step 4: Run** the same. Expected: 4 passed.
- [ ] **Step 5: Commit** `feat(marketplace): place excerpts at the chart's preview time`.

### Task 4: yt-dlp section arguments and the excerpt cache rules

**Files:**
- Modify: `src/InvectorModKit.Marketplace.Client/AudioFetch.cs`
- Create: `src/InvectorModKit.Marketplace.Client/ExcerptCache.cs`
- Test: `tests/InvectorModKit.Marketplace.Client.Tests/AudioFetchTests.cs`, `tests/InvectorModKit.Marketplace.Client.Tests/ExcerptCacheTests.cs`

**Interfaces:**
- Produces: `AudioFetch.BuildSectionArgs(string youtubeId, string outputWithoutExtension, string ffmpegLocation, double start, double end) : string`;
  `ExcerptCache.FileName(string youtubeId, double start) : string` (`"<id>-<start rounded to 0.1, invariant>.ogg"`);
  `ExcerptCache.ToDelete(IEnumerable<CachedFile> files, DateTime now) : List<string>` with `public sealed class CachedFile { public string Path; public long Bytes; public DateTime LastUsed; }`, `MaxAge = 7 days`, `MaxBytes = 200 * 1024 * 1024`.

- [ ] **Step 1: Write the failing tests**
  - `SectionArgs_FetchOnlyTheExcerpt`: `BuildSectionArgs("dQw4w9WgXcQ", @"C:\c\x", "tools", 61.5, 81.5)` contains `--download-sections *61.5-81.5`, `-x`, `--audio-format vorbis`, `--no-playlist`, `--ffmpeg-location tools`, and ends with `https://youtu.be/dQw4w9WgXcQ`; numbers use a dot whatever the culture (run with `CultureInfo("fr-FR")`).
  - `Cache_FileNameIsStable`: `FileName("abc", 61.54) == "abc-61.5.ogg"`.
  - `Cache_DeletesOldFiles`: a file last used 8 days ago is listed, one used yesterday is not.
  - `Cache_TrimsOldestBeyondTheLimit`: three 90 MB files used today, yesterday and 2 days ago: only the oldest is listed.
- [ ] **Step 2: Run** `dotnet test tests/InvectorModKit.Marketplace.Client.Tests -c Release --filter "SectionArgs|Cache_"` Expected: compile error.
- [ ] **Step 3: Implement** both (age rule first, then oldest-first until the rest fits in `MaxBytes`).
- [ ] **Step 4: Run** the same. Expected: 4 passed.
- [ ] **Step 5: Commit** `feat(marketplace): fetch and keep short excerpts`.

### Task 5: `ChartState`, the main button's decision

**Files:**
- Create: `src/InvectorModKit.Marketplace.Client/ChartState.cs`
- Test: `tests/InvectorModKit.Marketplace.Client.Tests/ChartStateTests.cs`

**Interfaces:**
- Produces:

```csharp
public sealed class ChartFacts
{
    public bool Mine, Installed, Downloaded, Downloading, Fetching, OtherVersion;
    public double Progress;            // 0..1 of the current step
    public string FetchError;          // null, or why the music could not be fetched
    public int InstalledVersion, LatestVersion;
}
public enum MainKind { Install, Installing, Retry, Installed, Update, Mine }
public sealed class MainButton
{
    public MainKind Kind; public bool Enabled; public int Percent;  // Percent only for Installing
    public string LabelKey, SubKey;                                    // Loc keys
    public string Reason;                                              // Retry only: FetchError, or null with SubKey "other_version"
    public static MainButton For(ChartFacts f);
}
```

Label and sub keys: Install `main_install`/`main_install_sub`; Installing `main_installing`/`main_step_chart` while downloading, `main_step_music` while fetching; Retry `main_retry`/`main_retry_sub` (sub carries the reason) or `other_version`; Installed `main_installed`/`main_installed_sub`; Update `main_update`/`main_update_sub`; Mine `main_mine`/null, not enabled.

- [ ] **Step 1: Write the failing tests** (one assertion group each): `State_NotInstalledOffersInstall`; `State_DownloadingShowsChartStep` (Percent from Progress 0.42 is 42); `State_FetchingShowsMusicStep`; `State_FailedFetchOffersRetryWithReason` (Downloaded, not Fetching, FetchError "Video unavailable": Retry, Reason "Video unavailable"); `State_OtherVersionOffersRetry` (SubKey `other_version`); `State_InstalledSaysWhere`; `State_NewerVersionOffersUpdate` (Installed, InstalledVersion 1, LatestVersion 2); `State_MineWinsOverInstalled` (Mine and Installed: Mine, not enabled).
- [ ] **Step 2: Run** `dotnet test tests/InvectorModKit.Marketplace.Client.Tests -c Release --filter State_` Expected: compile error.
- [ ] **Step 3: Implement** `For` in this order: Mine, Downloading, Fetching, Installed (Update when `LatestVersion > InstalledVersion`), Downloaded (Retry), else Install.
- [ ] **Step 4: Run** the same. Expected: 8 passed.
- [ ] **Step 5: Commit** `feat(marketplace): decide the main button from a chart's state`.

### Task 6: `CatalogueFilter`

**Files:**
- Create: `src/InvectorModKit.Marketplace.Client/CatalogueFilter.cs`
- Test: `tests/InvectorModKit.Marketplace.Client.Tests/CatalogueFilterTests.cs`

**Interfaces:**
- Produces: `public enum Shown { All, NotInstalled, Installed }`; `public static class CatalogueFilter { public static List<ChartSummary> Apply(IEnumerable<ChartSummary> items, ISet<string> installedIds, Shown shown, int? difficulty /* 0 Casual..3 Ultra, null any */); }` keeping the server's order.

- [ ] **Step 1: Write the failing tests** `Filter_AllKeepsOrder`, `Filter_NotInstalledHidesInstalled`, `Filter_InstalledOnly`, `Filter_DifficultyKeepsChartsWithNotesThere` (a chart with only Ultra notes is kept for 3, dropped for 2).
- [ ] **Step 2: Run** `dotnet test tests/InvectorModKit.Marketplace.Client.Tests -c Release --filter Filter_` Expected: compile error.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** the same, then the whole client suite `dotnet test tests/InvectorModKit.Marketplace.Client.Tests -c Release`. Expected: all pass (26 before this plan, plus the new ones).
- [ ] **Step 5: Commit** `feat(marketplace): filter the catalogue`.

### Task 7: What the Custom Songs plugin gives the screen

**Files:**
- Modify: `src/InvectorModKit.CustomSongs/CustomSongsApi.cs` (`LocalSong.PreviewPath`, `LocalSong.PreviewStart`, `CustomSongsApi.PreviewMixerGroup`), `src/InvectorModKit.CustomSongs/GameAssets.cs` (keep the preview template's resolved group)

**Interfaces:**
- Produces: `LocalSong.PreviewPath` (the 25 s excerpt wav song select plays, null until made), `LocalSong.PreviewStart` (seconds), `CustomSongsApi.PreviewMixerGroup` (`UnityEngine.Audio.AudioMixerGroup`, the official preview item's routing, null until resolved).

- [ ] **Step 1: Implement** the three members (Unity-only, no unit test possible: the plugin needs the game's assemblies).
- [ ] **Step 2: Build** `dotnet build src/InvectorModKit.CustomSongs -c Release -p:DeployToGame=false` Expected: 0 warnings, 0 errors.
- [ ] **Step 3: Commit** `feat(customsongs): share excerpts and their routing with the marketplace`.

### Task 8: Find where input still reaches the menus

This is an investigation (superpowers:systematic-debugging), done in the game with the owner's leave to deploy and launch (ask once for Tasks 8 and 12 together; launch with `steam://rungameid/2009450`, never the exe).

**Files:**
- Notes only: append the findings to this plan under "Task 8 findings" and commit them.

- [ ] **Step 1: Reproduce** with today's marketplace (main build of this branch, deployed): open it from the main menu and from song select; press arrows, Enter, Esc, Space, F, Tab, M, Backspace, letters; on a controller A, B, X, Y, LB, RB, LT, RT, d-pad, sticks, Start; click and scroll outside the window. Note each reaction behind (sound, selection, difficulty, world, ship, sort, preview music).
- [ ] **Step 2: Trace** each reaction to its handler in the decompiled game (`Update` of a view or selector reading Rewired, `RewiredStandaloneInputModule` events, `InputIndicatorItem` clicks, the menu music routine).
- [ ] **Step 3: Record** the list (handler, how it reads input, the block to apply) and the Rewired UI actions bound to X, Y, LB and RB on the owner's pad (log each UI action's `ActionElementMap.elementIdentifierName` for the last active joystick).
- [ ] **Step 4: Commit** `docs(marketplace): where the menu still takes input`.

Ruling for Task 10: block at the consumers that leak (Harmony prefixes returning `false` while `MarketplaceScreen.IsOpen`), not by disabling Rewired maps, because the screen reads its own input through the same Rewired actions.

### Task 9: `UiKit` and `KeyBar`, labels that fit and the game's key pictures

**Files:**
- Create: `src/InvectorModKit.Marketplace/Ui/UiKit.cs`, `src/InvectorModKit.Marketplace/Ui/KeyBar.cs`
- Modify: `src/InvectorModKit.Marketplace/Loc.cs` (keys below)

**Interfaces:**
- Produces:
  - `UiKit.Button(Transform parent, string label, ButtonStyle style, Action onClick) : ButtonWithEvents` with `enum ButtonStyle { Main, Secondary, Danger, Chip, Tab }`; the label is anchored with 24-unit side margins, best fit from the style's size down to 18, wrapping to two lines; `UiKit.SetLabel(ButtonWithEvents, string)`; `UiKit.SetSelected(ButtonWithEvents, bool)` for chips and tabs.
  - `UiKit.Text(Transform, string name, TextRole role) : Text` with `enum TextRole { Title, Subtitle, Body, Muted, Tag }` (the game's Exo2 fonts as today: Regular, `Exo2-SemiBoldCondensed`, `Exo2-BoldCondensed`).
  - `KeyBar(Transform parent)` with `Set(params Hint[] hints)` and `struct Hint { string KeyboardSprite; int? UiActionId; string LabelKey; }`: the keyboard picture (`GameUi.KeySprite`) when the last active controller is the keyboard or mouse, else `ControllerIconMapCollection.Instance.GetIcon(player, 1 /* UI */, actionId, AxisRange.Positive)`; refreshed when the last active controller changes.
- Loc keys added (11 languages): `tab_discover`, `filter_all`, `filter_not_installed`, `filter_installed`, `difficulty_any`, `main_install`, `main_install_sub`, `main_installing`, `main_step_chart`, `main_step_music`, `main_retry`, `main_retry_sub`, `folder_advice`, `main_installed`, `main_installed_sub`, `main_update`, `main_update_sub`, `main_mine`, `tag_installed`, `tag_new`, `tag_update`, `excerpt_loading`, `excerpt_unavailable`, `watch_video`, `more`, `retry`, `hint_main`, `hint_listen`, `hint_search`, `hint_tabs`, `hint_back`.

- [ ] **Step 1: Implement** `UiKit`, `KeyBar` and the Loc rows.
- [ ] **Step 2: Check the rows**: every new key has 11 non-empty strings without an em dash (the same parsing script as for the level editor button: count the strings of each row, look for U+2014). Expected: 11 and no dash for each key.
- [ ] **Step 3: Build** `dotnet build src/InvectorModKit.Marketplace -c Release -p:DeployToGame=false` Expected: 0 warnings, 0 errors.
- [ ] **Step 4: Commit** `feat(marketplace): buttons whose labels always fit, and the key bar`.

### Task 10: `ScreenInput` and `MenuAudio`

**Files:**
- Create: `src/InvectorModKit.Marketplace/Ui/ScreenInput.cs`, `src/InvectorModKit.Marketplace/Ui/MenuAudio.cs`
- Modify: `src/InvectorModKit.Marketplace/Ui/EntryPoints.cs` (the input blocks from Task 8, keyed on `MarketplaceScreen.IsOpen`)

**Interfaces:**
- Consumes: Task 8 findings.
- Produces:
  - `ScreenInput` (per frame): `Vector2Int Move`, `bool Ok, Back, Listen, Search, NextTab, PrevTab`, the mouse (hover selects, click presses, wheel scrolls the list under it), and `bool Typing { set; }`: while typing, only text, Backspace, Enter and Esc count (typing owns the keys).
  - `MenuAudio.Silence()` and `MenuAudio.Restore()`: main menu `StateMainMenu.PauseMusic()` / `UnpauseMusic()`; song select `menuSceneRoot.TransitionOutPreviewMusic(0.5f)` / `PlayLastPreviewMusic(0.5f)`; nothing elsewhere; `Restore` only undoes what `Silence` did.

- [ ] **Step 1: Implement** both and the blocks.
- [ ] **Step 2: Build** as in Task 9. Expected: 0 warnings, 0 errors.
- [ ] **Step 3: Commit** `feat(marketplace): the screen owns input and silences the menu`.

### Task 11: `MarketplaceScreen`, `DiscoverTab`, `PreviewPlayer`, `PreviewCache`

**Files:**
- Create: `src/InvectorModKit.Marketplace/Ui/MarketplaceScreen.cs`, `Ui/DiscoverTab.cs`, `Ui/ChartSheet.cs`, `Ui/PreviewPlayer.cs`, `Ui/PreviewCache.cs`
- Modify: `Ui/MySongsTab.cs` (host type `MarketplaceScreen`, laid out in a 1500 x 880 area centred in the body until stage 2), `Ui/AddSong.cs`, `Ui/TextPrompt.cs`, `Ui/EntryPoints.cs`, `MarketplacePlugin.cs` (every `MarketplaceModal` use becomes `MarketplaceScreen`)
- Delete: `Ui/MarketplaceModal.cs` (its `SongRow`, `Confirmation`, `ITab` move to `MarketplaceScreen.cs` or their own files), `Ui/BrowseTab.cs` (its install, audio fetch, report and delete logic moves to `DiscoverTab`)

**Interfaces:**
- Consumes: `ExcerptWindow`, `AudioFetch.BuildSectionArgs`, `ExcerptCache`, `MainButton.For`, `CatalogueFilter.Apply`, `ChartSummary.Preview`, `LocalSong.PreviewPath`, `CustomSongsApi.PreviewMixerGroup`, `UiKit`, `KeyBar`, `ScreenInput`, `MenuAudio`.
- Produces:
  - `MarketplaceScreen.Open(MonoBehaviour host, Action<MarketplaceScreen> then = null)`, `Close()`, `IsOpen`, and for the tabs `Confirm`, `Prompt`, `Status`, `EnsureSignedIn`, `MakeRow` (as `MarketplaceModal` had them).
  - `PreviewPlayer.Play(AudioClip clip, double startInClip, double endInClip)`, `Stop()`: one `AudioSource` on `CustomSongsApi.PreviewMixerGroup` (the plain output when null), fade in 0.6 s, fade out 1.2 s before `endInClip`, then loops from `startInClip`.
  - `PreviewCache.Get(ChartSummary c, Action<AudioClip, double, double> ready, Action failed)`: a local song (matched by `MarketplaceId`) plays its `PreviewPath` from 0 to `min(20, length)`; otherwise the cached or fetched `ExcerptCache.FileName` section (`ExcerptWindow.For(c.Preview, c.Duration)`), loaded with `UnityWebRequestMultimedia.GetAudioClip`; one fetch at a time, the newest request wins; eviction (`ExcerptCache.ToDelete`) when the screen opens.
- Tabs in stage 1: DISCOVER and MY SONGS (today's tab, `tab_mine`), switched with Tab, LB/RB or a click. When the server is unreachable, Discover's list says so with a "Retry" button.
- The layout, in the screen's 1920 x 1080 reference: top bar 114 high (title, tabs, then Discord without glow, Rules, account); body from x 60, list 840 wide, 48 gap, sheet to x 1860; bottom key bar 78 high. The sheet: cover 285 square with the play button, title, artist, author and version, BPM, length, downloads; the four difficulty chips across the sheet's width; the leaderboard (top 5 and the player's line) of the chosen difficulty; the main button full width (102 high) with its sub line; "Watch the video", the report flag, and for the author or an admin "More…" with "Delete the chart…". RETRY shows `folder_advice` and "Open the folder" under it.
- Key bar in Discover: main action (Enter / UI_Ok), Listen (Space / X action), Search (F / Y action), Tabs (Tab / LB and RB actions), Back (Esc / UI_Cancel).

- [ ] **Step 1: Implement** the files above, moving the existing logic rather than rewriting it (install, audio fetch, other version, leaderboard, report, delete, thumbnails, video title, sign-in), feeding `MainButton.For` from the same facts BrowseTab used (`Installs.IsDownloaded`, installed ids from `CustomSongsApi.Songs()`, fetch in progress and its last error, `Session.IsPublishedByMe`, the installed `MarketplaceVersion` against `Version`).
- [ ] **Step 2: Build everything** `dotnet build src/InvectorModKit.Marketplace -c Release -p:DeployToGame=false` and the CI's builds and tests (`dotnet build src/InvectorModKit.ChartGen -c Release -p:DeployToGame=false`, `dotnet build src/InvectorModKit.Setup -c Release`, `dotnet build tools/ChartEditor -c Release`, the four `dotnet test` projects, `node --test` in `tools/ChartEditor/ui`). Expected: 0 warnings in the plugin, all tests pass.
- [ ] **Step 3: Commit** `feat(marketplace): a full screen with the Discover tab`.

### Task 12: Check everything in the game, with screenshots

With the owner's leave (asked with Task 8): deploy this branch's plugins (`dotnet build src/InvectorModKit.Marketplace -c Release`, after backing up the installed plugin folders), launch with `steam://rungameid/2009450`, drive it with computer use, and send the screenshots as they are taken.

- [ ] **Step 1: Isolation.** From the main menu and from song select: every key and controller button of Task 8's list changes nothing behind; the menu music stops on opening and comes back on closing; song select's preview comes back.
- [ ] **Step 2: Excerpts.** An installed song: instant excerpt; a chart not installed: "Loading the excerpt" then the excerpt at its strong part; fade in, fade out, loop; moving on stops it; a chart whose video fails: "Excerpt unavailable".
- [ ] **Step 3: The main button.** Install a chart and watch INSTALL, INSTALLING (chart, then music), INSTALLED; force a failed fetch (rename the bundled yt-dlp for the test, then put it back) and see RETRY with its reason and the folder advice; YOURS on an own chart; UPDATE when the installed version is older (an own chart republished, or a test chart).
- [ ] **Step 4: Controls.** The whole Discover flow with the keyboard only, the mouse only, and the controller if the owner has one plugged in (else say it was not checked).
- [ ] **Step 5: Labels.** Screenshot Discover and the main button states in the 11 languages (the game's language option); no label touches or leaves its button. Fix and repeat until clean.
- [ ] **Step 6: Wrap up.** Push the branch, open the pull request ready (summary, how it was tested with the results above, checklist), verify the merge locally as for earlier pull requests, report to the owner with the screenshots; merge only once the owner agrees.

## Task 8 findings (2026-11-12)

Reproduced in the game (song select, today's marketplace open with M):

- **R changed the ship behind the window** ("Chant d'étoiles" became "Vieux Croiseur" after two presses). Cause:
  `ShipSelector.Update` reads `RewiredInputHelper.GetButtonDownAll(58)` (UI_ChangeShip) itself, outside the state's
  `Update` that the marketplace blocks. Block: a Harmony prefix on `ShipSelector.Update` returning false while the
  screen is open.
- Q and E (worlds, actions 60 and 59), S (sorting, 63), the difficulty and the song list did not move: the state
  `Update` prefixes and `EventSystem.sendNavigationEvents = false` hold.
- The song select's preview music keeps playing behind the window: Task 10's `MenuAudio`.
- `MenuLayout.Update` reads Unity's legacy `Input` ("Fire1", "Fire2", "Vertical") on its own. No instance was seen
  reacting (it belongs to the console menu stack), but it would leak the same way: blocked too, as a precaution.
- Every other input reader in the decompiled game is either a state's `Update` (blocked), gameplay, or icon refresh
  (`UIIndicatorList`).

Keyboard bindings seen in song select: Q / E worlds, R ship, S sorting, Esc back, Enter confirm, arrows difficulty.
No controller was plugged in, so the physical buttons behind the UI actions could not be read. The screen uses the
game's UI actions for the controller: tabs UI_PrevSelection (60) / UI_NextSelection (59), listen UI_ToggleSorting (63),
search UI_ChangeShip (58), main UI_Ok, back UI_Cancel, and draws each one's picture with
`ControllerIconMapCollection`, so the key bar always shows the real button. These actions are read only while a
controller is the last device used; with the keyboard, the screen reads its own keys (Enter, Esc, Space, F, Tab,
arrows), so R or S never trigger a screen action.

# Level editor song export and import Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An Export button that writes one zip with everything the mod needs to play a song elsewhere, and an "Import a zip…" button that puts such a zip into the player's CustomSongs and opens it.

**Architecture:** A new `SongPackage` class in the editor's tested library builds and reads the zip; `EditorBackend` exposes it as `export` and `import`; the WinForms host only shows the Windows file dialogs (`host.saveZip`, `host.openZip`); the interface adds the two buttons and their texts.

**Tech Stack:** C# netstandard2.0 + `System.IO.Compression` (already used by the installer), xUnit on net8.0, WinForms net48 + WebView2, plain ES modules, Node's test runner.

**Spec:** `docs/superpowers/specs/2026-11-13-editor-chart-export-design.md`

## Global Constraints

- Every player-visible text in 11 languages, order en, fr, de, it, es, pt-BR, pl, ru, ja, ko, zh (`ui/i18n.js`).
- No em dashes anywhere (UI, docs, commits). Conventional Commits in English, no AI mention, no `Co-Authored-By`.
- No new NuGet or npm dependency.
- Import limits: more than 50 entries or more than 300 MB unpacked is refused; exactly one audio file (`.mp3`, `.ogg`, `.wav`).
- Zip entries at the root: `<audio file name>`, `<audio name>.chart.json`, `<audio name>.<jpg|png|jpeg>` (optional), `song.json` (`{"title","artist"}`).
- Keep edits away from the lines PR #22 changes (`host.title`/`host.close` cases in `MainForm.cs`, `refreshTitle`/`setDifficulty` in `app.js`, the rows after `notesCount` in `i18n.js`).

## Review Focus

1. Non-ASCII file names (`John Summit, Inéz - crystallized.ogg`, Japanese titles) must survive export then import unchanged.
2. A zip made on a Mac carries `__MACOSX/._Song.mp3` resource forks: still one song, not "several songs".
3. A zip whose declared sizes lie (zip bomb) must stop at the limit and leave no folder behind.
4. Exporting over an existing zip (the player said yes to "replace") gives the new content only.
5. A hand-made zip whose chart is not named like its audio must still import with its chart.

Each has its test in Task 2 or Task 3.

---

### Task 1: `song.json` in the CustomSongs root is ignored, like the game

**Files:**
- Modify: `src/InvectorModKit.ChartEditor.Core/SongFiles.cs`
- Modify: `src/InvectorModKit.ChartEditor.Core/EditorBackend.cs` (`Open`, `WriteSongInfo`)
- Test: `tests/InvectorModKit.ChartEditor.Tests/EditorBackendTests.cs`

**Interfaces:**
- Produces: `SongFiles.Read(string audioPath, string customSongsDir = null)` (skips `song.json` when the audio's folder is `customSongsDir`); `SongFiles.InOwnFolder(string customSongsDir, string audioPath) : bool` (inside the root, not in the root itself).

- [ ] **Step 1: Write the failing test** `Songs_IgnoreASongJsonLeftInTheRoot`: CustomSongs holds `Artist - Loose.wav` and a root `song.json` `{"title":"Wrong","artist":"Wrong"}`; `songs` lists `("Artist", "Loose")`; `open` on that audio gives title `Loose`.
- [ ] **Step 2: Run** `dotnet test tests/InvectorModKit.ChartEditor.Tests --filter Songs_IgnoreASongJsonLeftInTheRoot`. Expected: FAIL (title `Wrong`).
- [ ] **Step 3: Implement** the optional parameter and `InOwnFolder`; `Enumerate(root)` passes `root`, `Open` passes `_settings.CustomSongsDir`, `WriteSongInfo` uses `InOwnFolder`.
- [ ] **Step 4: Run** the whole ChartEditor test project. Expected: all PASS.
- [ ] **Step 5: Commit** `fix(editor): ignore a song.json left in the CustomSongs root, as the game does`

### Task 2: Export a song as a zip

**Files:**
- Create: `src/InvectorModKit.ChartEditor.Core/SongPackage.cs`
- Modify: `src/InvectorModKit.ChartEditor.Core/EditorBackend.cs` (`Handle` case `export`)
- Test: `tests/InvectorModKit.ChartEditor.Tests/SongPackageTests.cs` (new, reuses `Sandbox`)

**Interfaces:**
- Consumes: `SongFiles.InOwnFolder`, `SongFiles.ChartPathFor`, `ChartJson.Serialize`.
- Produces: `SongPackage.Export(string audioPath, Chart chart, string title, string artist, string customSongsDir, string zipPath) : void`; `SongPackage.CoverOf(string audioPath, string customSongsDir) : string` (null without cover); backend method `export` with params `audioPath`, `chart`, `title`, `artist`, `zipPath`, returning `{ "zipPath": … }`.

Cover choice, as the game's `SongLibrary.ReadMetadata`: in its own folder of CustomSongs, the first existing of `cover.png`, `cover.jpg`, `cover.jpeg`, `folder.jpg`, `folder.png`; otherwise the first existing of `<audio name>.jpg`, `.png`, `.jpeg`. Written as `<audio name>` + the image's extension in lower case. The chart is made hand-made (generator and source cleared) before `ChartJson.Serialize`. The zip goes to `zipPath + ".tmp"`, then `File.Copy(tmp, zipPath, true)` and `File.Delete(tmp)`.

- [ ] **Step 1: Write the failing tests** (through `backend.Handle("export", …)`, reading the result with `ZipFile.OpenRead`):
  - `Export_HoldsAudioChartCoverAndSongInfo`: song `CustomSongs/Folder/track.wav` with `cover.jpg` and a sidecar `track.png`; entries are exactly `track.wav`, `track.chart.json`, `track.jpg`, `song.json`; the audio bytes equal the source; the chart deserializes with `Generator == null`, `Source == null` and 2 Hard notes from `SampleChart(stamps: true)`; `song.json` has the editor's title and artist.
  - `Export_LooseSong_TakesTheImageNamedLikeTheAudio`: loose `CustomSongs/Artist - Loose.wav` + `Artist - Loose.png`; entry `Artist - Loose.png` present; no other image.
  - `Export_WithoutCover_HasNoImage`: song outside CustomSongs with a `cover.jpg` next to it: no image entry (folder covers count only inside CustomSongs).
  - `Export_ReplacesAnExistingZip`: a file of 1 MB of junk at `zipPath`; after export the zip opens and has 4 or 3 entries, and no `.tmp` file remains.
- [ ] **Step 2: Run** `dotnet test tests/InvectorModKit.ChartEditor.Tests --filter SongPackage`. Expected: FAIL (`unknown method export`).
- [ ] **Step 3: Implement** `SongPackage.Export`, `CoverOf` and the backend case (`Required` for `audioPath` and `zipPath`, `ReadChart`).
- [ ] **Step 4: Run** the ChartEditor tests. Expected: all PASS.
- [ ] **Step 5: Commit** `feat(editor): export a song with its chart and cover as one zip`

### Task 3: Import a song zip into CustomSongs

**Files:**
- Modify: `src/InvectorModKit.ChartEditor.Core/SongPackage.cs`
- Modify: `src/InvectorModKit.ChartEditor.Core/SongFiles.cs` (`NewFolder`)
- Modify: `src/InvectorModKit.ChartEditor.Core/EditorBackend.cs` (case `import`; `SendToGame` uses `NewFolder`)
- Test: `tests/InvectorModKit.ChartEditor.Tests/SongPackageTests.cs`

**Interfaces:**
- Produces: `SongPackage.Import(string zipPath, string customSongsDir, long maxBytes = SongPackage.MaxBytes) : string` (the imported audio's full path); `SongPackage.MaxEntries = 50`, `SongPackage.MaxBytes = 300L * 1024 * 1024`; `enum ImportRefusal { NotZip, Unsafe, TooBig, NoSong, SeveralSongs }`; `SongPackageException : Exception` with `ImportRefusal Reason`; `SongFiles.NewFolder(string root, string name) : string` (creates `name`, or `name (2)`, `(3)`…, returns its path). Backend `import` with param `zipPath` returns `{ "audioPath": … }` or `{ "refused": "notZip" | "unsafe" | "tooBig" | "noSong" | "severalSongs" }` (enum name in camel case); without a game folder it throws `game not found` like `sendToGame`.

Rules, all checked before anything is written:
- Not a readable zip (`InvalidDataException`) → `NotZip`.
- More than `MaxEntries` entries, or the sum of declared `Length` above `maxBytes` → `TooBig`.
- An entry whose name (with `\` read as `/`) starts with `/`, contains `:` or has a `..` segment → `Unsafe`.
- Skipped: folder entries, anything under `__MACOSX/`, file names starting with `.`, other file types.
- File name = the last segment; a name with an invalid file name character → `Unsafe`.
- Audio count 0 → `NoSong`, more than 1 → `SeveralSongs`.
- Chart: the `.chart.json` named like the audio, else the first one; image: the one named like the audio, else the first `.jpg`/`.png`/`.jpeg`.
- Folder name: `SongFiles.FolderName(artist, title)` from a readable `song.json`, else `SongFiles.FolderName(null, <zip name without extension>)`.

Writing: everything goes into `CustomSongs/.import-<guid>` (the game and the editor skip folders starting with `.`), copied with a running byte count that throws `TooBig` past `maxBytes` even if declared sizes lie; then `Directory.Move` to `SongFiles.NewFolder`'s name (create the name to reserve it, delete the empty folder, move). Any failure deletes the temporary folder. The chart is written as `<audio name>.chart.json`, the image as `<audio name>` + its lower-case extension, `song.json` rewritten with only `title` and `artist`.

- [ ] **Step 1: Write the failing tests** (zips built in the test with `ZipArchive`):
  - `Import_ExportedZip_LandsInItsOwnFolder`: export a song (title `Viva: La Vida`, artist `Coldplay`), import into a second sandbox's CustomSongs; the result is `CustomSongs/Coldplay - Viva_ La Vida/track.wav`, chart and image beside it, `SongFiles.Read(result, root).Title == "Viva: La Vida"`, no `.import-*` folder left.
  - `Import_Twice_GoesToANumberedFolder`: second import gives `… (2)`.
  - `Import_KeepsNonAsciiNames`: audio `John Summit, Inéz - crystallized.wav` and `曲.wav` cases come back with the same file name.
  - `Import_HandMadeZip_RenamesTheChartAndTakesTheFolderInside`: entries `My Song/a.wav`, `My Song/chart.chart.json`, `My Song/cover.jpg`, `__MACOSX/My Song/._a.wav`, `notes.txt`; imported to `CustomSongs/<zip name>/a.wav` with `a.chart.json` and `a.jpg`; `notes.txt` absent.
  - `Import_RefusesUnsafeZips` (theory): entry `../evil.wav` → `Unsafe`; entry `C:/evil.wav` → `Unsafe`; no audio → `NoSong`; two `.wav` → `SeveralSongs`; a text file renamed `.zip` → `NotZip`; 51 entries → `TooBig`. Each leaves CustomSongs with no new folder and nothing written outside it.
  - `Import_StopsAtTheByteLimit`: a valid zip imported with `maxBytes` smaller than its audio → `TooBig`, no folder left.
  - `Import_WithoutGame_IsRefused`: backend with an empty `GameDir` → throws.
  - Existing `SendToGame_CopiesIntoANewFolder_ThenSavesInPlace` stays green after `NewFolder` is shared.
- [ ] **Step 2: Run** `dotnet test tests/InvectorModKit.ChartEditor.Tests --filter Import`. Expected: FAIL (`unknown method import`).
- [ ] **Step 3: Implement** `Import`, `NewFolder`, the backend case (catch `SongPackageException` into `refused`).
- [ ] **Step 4: Run** the ChartEditor tests. Expected: all PASS.
- [ ] **Step 5: Commit** `feat(editor): import a shared song zip into the game's CustomSongs`

### Task 4: Buttons, dialogs and texts

**Files:**
- Modify: `tools/ChartEditor/MainForm.cs` (cases next to `host.openAudio`)
- Modify: `tools/ChartEditor/ui/index.html` (button after `btn-publish`)
- Modify: `tools/ChartEditor/ui/app.js` (`bind`, `renderAll`, `openDialog`, new `exportSong`, `importZip` after `sendToGame`)
- Modify: `tools/ChartEditor/ui/i18n.js` (rows after `publish`, after `sentUpdate`, after `otherFile`)
- Test: `tools/ChartEditor/ui/tests/i18n.test.js`

**Interfaces:**
- Consumes: backend `export`, `import` (Tasks 2, 3); `SongFiles.FolderName`.
- Produces: `host.saveZip { title, artist }` → chosen path or null (`SaveFileDialog`, filter `Zip (*.zip)|*.zip`, file name `FolderName(artist, title) + ".zip"`, the folder last used this session, else the Desktop, replace prompt on); `host.openZip` → path or null (`OpenFileDialog`, same filter, the folder last used, else `%USERPROFILE%\Downloads` when it exists).

Interface behaviour:
- `exportSong()`: no song → nothing; unsaved changes → `save()` first, stop if it fails; ask `host.saveZip`; `busy(t("exporting"))`; `export`; status `t("exported", <zip file name>)`; errors → `t("error", …)`.
- `importZip()`: game not found → `settingsDialog()`; `host.openZip`; `busy(t("importing"))`; `import`; `refused` → status `t("importRefused_" + code)` as an error; else `openSong(audioPath)` and, once that song is open, status `t("imported", <title>)`.
- Open window buttons: Cancel, Import a zip…, Other audio file… (accent stays on the last).
- `btn-export` disabled without a song, like Save.

New rows (11 languages each, same placeholders): `export`, `exporting`, `exported` ("Exported: {0}. Send this file; your friend adds it with Open, then Import a zip…"), `importZip` ("Import a zip…"), `importing`, `imported` ("Imported into the game's Custom Songs: {0}"), `importRefused_notZip`, `importRefused_unsafe`, `importRefused_tooBig`, `importRefused_noSong`, `importRefused_severalSongs`.

- [ ] **Step 1: Write the failing test**: in "every key the interface uses is defined", add the five `importRefused_` codes to `used`.
- [ ] **Step 2: Run** `node --test tests/i18n.test.js` from `tools/ChartEditor/ui`. Expected: FAIL (missing keys).
- [ ] **Step 3: Implement** the rows, the button, the two methods, the host cases.
- [ ] **Step 4: Run** `node --test tests/model.test.js tests/i18n.test.js tests/audio.test.js tests/keys.test.js`, then `dotnet build InvectorModKit.slnx -c Release -p:DeployToGame=false`. Expected: all PASS, build succeeds (the plugin projects may need the game; the editor and libraries must build).
- [ ] **Step 5: Commit** `feat(editor): Export button and Import a zip in the Open window`

### Task 5: Check in the real editor and open the pull request

- [ ] **Step 1:** Run every suite in AGENTS.md (Charting, Marketplace.Client, ChartEditor, Installer, the four Node files). Expected: all green.
- [ ] **Step 2:** Ask Theo before replacing the installed editor (it is a build of PR #22): back up its files, install a build of this branch merged locally with PR #22 (never pushed) so the Discord presence stays, or this branch alone if Theo prefers.
- [ ] **Step 3:** With Theo: export a song with a cover, import the zip, unzip one by hand into CustomSongs, see the songs play in the game (launched through Steam), check the buttons in a long language (de, ru) and the refusal message with a bad zip. Screenshots to Theo.
- [ ] **Step 4:** Push the branch and open a ready pull request (title `feat(editor): share a level as a zip with Export and Import`), body in English, no AI mention.

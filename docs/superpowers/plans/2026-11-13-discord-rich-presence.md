# Discord Rich Presence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show the player's game on their Discord profile: song, artist, difficulty, live score and combo, time
remaining in a level; the current screen and total play time elsewhere; the song, difficulty and note count being
edited in the level editor; two buttons.

**Architecture:** The Discord protocol, the activity builder and the update scheduler live in the tested
`netstandard2.0` marketplace client (`Discord/` folder), so CI runs them. The Marketplace plugin (which already holds
`Loc`, the Discord invite setting and the game-session hooks) samples the game once a second and hands snapshots to a
background worker. The level editor host references the same library and runs its own presence from what its
interface reports; the game steps aside while the editor runs. No new plugin, no new DLL, no installer change.

**Tech Stack:** C# `netstandard2.0` + `net472` plugin, Newtonsoft.Json 12, Win32 named pipes through `kernel32`
P/Invoke, xUnit on `net8.0`.

**Spec:** `docs/superpowers/specs/2026-11-13-discord-rich-presence-design.md`

## Global Constraints

- Discord IPC: pipe `\\.\pipe\discord-ipc-0` to `-9`, frame = int32 LE opcode, int32 LE byte length, UTF-8 JSON.
  Opcodes: 0 handshake `{"v":1,"client_id":"<id>"}`, 1 frame, 2 close, 3 ping, 4 pong.
- Activity update: `{"cmd":"SET_ACTIVITY","args":{"pid":<game pid>,"activity":{...}},"nonce":"<unique>"}`.
- Field limits: `details`, `state`, `large_text`, `small_text` at most 128 characters; button `label` at most 32;
  at most 2 buttons; timestamps in Unix milliseconds.
- Pacing: at most one `SET_ACTIVITY` every 4 s, for every kind of change (score and combo included, after Theo's in-game test).
- Discord closed, missing or restarted: no exception reaches the game, one log line per disconnect at most, reconnect
  attempts with backoff 5 s, 10 s, 20 s, 40 s, then every 60 s.
- Mono's `NamedPipeClientStream` is unreliable in Unity: the real pipe uses `CreateFile`, `PeekNamedPipe`, `ReadFile`,
  `WriteFile`, `CloseHandle` from `kernel32.dll`.
- Every player-facing text in `Loc.cs`, 11 languages in the order en, fr, de, it, es, pt-BR, pl, ru, ja, ko, zh.
- No em dashes anywhere; no AI mention in commits or code (AGENTS.md).
- The application ID is one constant, `DiscordPresence.DefaultApplicationId` (the ID Theo gives after creating the
  Discord application, see Task 4 Step 1), used by the game and the editor.
- Config (Marketplace plugin, section `Discord`): `Presence` (bool, default true), `ApplicationId` (string, default
  `DiscordPresence.DefaultApplicationId`). Editor: `EditorSettings.DiscordPresence` (bool, default true).

## Review Focus

- Discord not running when the game starts, then opened mid-session: presence appears within the backoff window, no log spam. (Task 3 `Reconnects_AfterBackoff`)
- Discord quit mid-level: writes fail, the worker drops the pipe and keeps the game smooth. (Task 3 `WriteFailure_DropsConnection_AndBacksOff`)
- Very long custom song titles or artists, and translations longer than 32 characters for buttons: truncated, never rejected by Discord. (Task 2 `LongTexts_AreTruncated`)
- Pausing then resuming a level: the countdown must not lie while paused, and must not be re-sent every second after resuming because of a few milliseconds of drift. (Task 2 `Paused_HasNoTimestamps`, Task 3 `EndDrift_UnderTwoSeconds_IsNotAChange`)
- Empty Discord invite setting: only the "Get InvectorModKit" button. (Task 2 `EmptyInvite_DropsDiscordButton`)

---

### Task 1: IPC framing and the Win32 pipe

**Files:**
- Create: `src/InvectorModKit.Marketplace.Client/Discord/IpcFrame.cs`
- Create: `src/InvectorModKit.Marketplace.Client/Discord/IDiscordPipe.cs`
- Create: `src/InvectorModKit.Marketplace.Client/Discord/Win32DiscordPipe.cs`
- Test: `tests/InvectorModKit.Marketplace.Client.Tests/DiscordIpcFrameTests.cs`

**Interfaces:**
- Produces:
  - `enum IpcOpcode { Handshake = 0, Frame = 1, Close = 2, Ping = 3, Pong = 4 }`
  - `static class IpcFrame { byte[] Encode(IpcOpcode op, string json); bool TryDecode(byte[] buffer, int count, out IpcOpcode op, out string json, out int consumed); }`
  - `interface IDiscordPipe : IDisposable { bool TryOpen(); void Write(byte[] data); int ReadAvailable(byte[] buffer); bool IsOpen { get; } }`
    (`Write` throws `IOException` when the pipe is gone; `ReadAvailable` returns 0 without blocking when nothing waits)
  - `sealed class Win32DiscordPipe : IDiscordPipe` (tries `discord-ipc-0` to `-9`, first that opens wins)

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Encode_WritesOpcodeLengthAndUtf8()
{
    var bytes = IpcFrame.Encode(IpcOpcode.Frame, "{\"a\":\"é\"}");
    Assert.Equal(new byte[] { 1, 0, 0, 0, 10, 0, 0, 0 }, bytes.Take(8));
    Assert.Equal("{\"a\":\"é\"}", Encoding.UTF8.GetString(bytes, 8, 10));
}
[Fact] public void TryDecode_RoundTrips() // Encode(Handshake, json) then TryDecode gives the same op, json, consumed == bytes.Length
[Fact] public void TryDecode_PartialFrame_ReturnsFalse() // first 8 + 3 bytes of a 10-byte payload: false, consumed 0
[Fact] public void TryDecode_TwoFrames_ConsumesFirstOnly() // two frames back to back: first decoded, consumed == its length
```

- [ ] **Step 2: Run, expect FAIL** `dotnet test tests/InvectorModKit.Marketplace.Client.Tests --filter DiscordIpcFrameTests` (types missing)
- [ ] **Step 3: Implement `IpcFrame`, `IDiscordPipe`, `Win32DiscordPipe`** as above. `Win32DiscordPipe` opens with `GENERIC_READ | GENERIC_WRITE`, `OPEN_EXISTING`; `ReadAvailable` calls `PeekNamedPipe` first and reads only what is there; any Win32 failure in `Write` closes the handle and throws `IOException`.
- [ ] **Step 4: Run, expect PASS**, same command.
- [ ] **Step 5: Commit** `feat(presence): add the Discord IPC framing and pipe`

### Task 2: Activity builder

**Files:**
- Create: `src/InvectorModKit.Marketplace.Client/Discord/PresenceSnapshot.cs`
- Create: `src/InvectorModKit.Marketplace.Client/Discord/PresenceTexts.cs`
- Create: `src/InvectorModKit.Marketplace.Client/Discord/ActivityBuilder.cs`
- Test: `tests/InvectorModKit.Marketplace.Client.Tests/DiscordActivityBuilderTests.cs`

**Interfaces:**
- Produces:
  - `enum PresenceScreen { Menu, SongSelect, Marketplace, Editor, Playing, Paused, Results }`
  - `sealed class PresenceSnapshot { PresenceScreen Screen; string Title; string Artist; bool CustomSong; string Difficulty /* "Casual","Normal","Hard","Ultra" */; int Score; int Combo; double RemainingSeconds; int Notes; }` (`Hidden` screen = clear the activity)
  - `PresenceScreen` also has `Hidden`: `Build` returns `null`, sent as `"activity": null`
  - `sealed class PresenceTexts { string Menu, SongSelect, Marketplace, Editor, Paused, Results, ScoreLine /* "{0} pts · combo {1}" */, NotesOne /* "{0} note" */, NotesMany /* "{0} notes" */, CustomSong, GetMod, JoinDiscord, ModVersion /* "InvectorModKit 0.4.0" */, LanguageCode /* "fr" */; string ModUrl, DiscordUrl; }`
  - `static class ActivityBuilder { JObject Build(PresenceSnapshot s, PresenceTexts t, DateTime sessionStartUtc, DateTime nowUtc); }`
    returns the `activity` object, `type` 0, `large_image` `"logo"`, `small_image` = difficulty in lower case while in a
    level.

Layout decided here: in a level, `details` = `"{Title} · {Artist}"` (title alone when artist empty), `state` =
`"{Difficulty} · " + ScoreLine`, `timestamps.end` = now + remaining; `large_text` = ModVersion, plus `" · " +
CustomSong` for custom songs; `small_text` = Difficulty. Paused: `details` as in a level, `state` = Paused, no
timestamps. Editor: `details` = Editor; with a song, `state` = `"{Title} · {Difficulty} · " + notes` (notes from
`NotesOne`/`NotesMany` through `Plural.IsOne(Notes, LanguageCode)`), small image = difficulty; `timestamps.start` =
session start (the editor's own start). Every other screen: `details` = that screen's text, `timestamps.start` =
session start, no `state`, no small image. Buttons: `GetMod` -> `ModUrl`, then `JoinDiscord` -> `DiscordUrl` only if it starts with `https://`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Level_ShowsSongDifficultyScoreAndCountdown()
// Playing, "Blinding Lights", "The Weeknd", Hard, 412300, 48, 107 s, now = 2026-11-13T20:00:00Z:
// details "Blinding Lights · The Weeknd"; state "Hard · 412300 pts · combo 48";
// timestamps.end == ToUnixMs(now) + 107000; small_image "hard"; large_image "logo"
[Fact] public void CustomSong_MarkedInLargeText() // large_text "InvectorModKit 0.4.0 · Custom song"
[Fact] public void Paused_HasNoTimestamps() // state == texts.Paused, no "timestamps" key
[Fact] public void Menu_ShowsScreenAndElapsed() // details texts.Menu, timestamps.start == ToUnixMs(sessionStart), no state, no small_image
[Fact] public void Buttons_ModThenDiscord() // 2 buttons, labels/urls in that order
[Fact] public void EmptyInvite_DropsDiscordButton() // DiscordUrl "" -> 1 button
[Fact] public void Editor_ShowsSongDifficultyAndNotes() // Editor, "Blinding Lights", Hard, 412, fr texts: details texts.Editor; state "Blinding Lights · Hard · 412 notes"; small_image "hard"
[Fact] public void Editor_OneNote_UsesSingular() // Notes 1 (and 0 with LanguageCode "fr") -> NotesOne
[Fact] public void Editor_NoSong_HasNoState()
[Fact] public void Hidden_BuildsNull()
[Fact] public void LongTexts_AreTruncated() // 300-char title -> details.Length 128; 40-char GetMod -> label.Length 32
```

- [ ] **Step 2: Run, expect FAIL** `dotnet test tests/InvectorModKit.Marketplace.Client.Tests --filter DiscordActivityBuilderTests`
- [ ] **Step 3: Implement** the three files. Truncation cuts on a char boundary that does not split a surrogate pair.
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** `feat(presence): build the Discord activity from the game's state`

### Task 3: Presence client (connection and pacing)

**Files:**
- Create: `src/InvectorModKit.Marketplace.Client/Discord/DiscordPresence.cs`
- Test: `tests/InvectorModKit.Marketplace.Client.Tests/DiscordPresenceTests.cs` (with a `FakePipe : IDiscordPipe` recording frames, able to refuse `TryOpen` or throw on `Write`)

**Interfaces:**
- Consumes: `IpcFrame`, `IDiscordPipe` (Task 1); `PresenceSnapshot`, `PresenceTexts`, `ActivityBuilder` (Task 2).
- Produces:
  - `sealed class DiscordPresence : IDisposable`, with `public const string DefaultApplicationId`
  - `DiscordPresence(string applicationId, int pid, Func<IDiscordPipe> pipeFactory, Action<string> log, DateTime sessionStartUtc)`
  - `void Update(PresenceSnapshot snapshot, PresenceTexts texts)`: thread-safe, stores the latest wish
  - `void Tick(DateTime nowUtc)`: one deterministic step (connect if due, drain reads, answer Ping with Pong, send if due)
  - `void Start()` / `Dispose()`: background thread calling `Tick(DateTime.UtcNow)` every 500 ms; `Dispose` stops it and closes the pipe

Rules: after `TryOpen` succeeds, send the handshake, then consider the connection ready (Discord's READY reply is
drained and ignored). A wish is a *major* change when anything but `Score`, `Combo` or `RemainingSeconds` differs from
the last sent, or the computed countdown end moved by 2 s or more; otherwise a *minor* change. Major: sent once 4 s have
passed since the last send. Minor: once 15 s have passed. Nothing sent when the wish equals the last sent.
Failures (refused open, `IOException`, Close frame from Discord) drop the pipe and schedule the next open with the
backoff of Global Constraints; the backoff resets after a successful handshake. After a reconnect the current wish is
sent again even if unchanged.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void FirstTick_HandshakesThenSendsActivity() // frames: [Handshake {"v":1,"client_id":"123"}, Frame SET_ACTIVITY with args.pid 42]
[Fact] public void ScoreOnlyChange_WaitsFifteenSeconds() // update score at t+1 s: nothing at t+14 s, one frame at t+15 s
[Fact] public void ScreenChange_WaitsFourSecondsOnly() // Menu -> Marketplace at t+1 s: nothing at t+3 s, sent at t+4 s
[Fact] public void EndDrift_UnderTwoSeconds_IsNotAChange() // RemainingSeconds drifts by 0.5 s per tick for 10 ticks in 10 s, same score: no frame before t+15 s
[Fact] public void Reconnects_AfterBackoff() // TryOpen refused twice: attempts at t, t+5 s, t+15 s; log called once
[Fact] public void WriteFailure_DropsConnection_AndBacksOff() // Write throws: no exception from Tick, next TryOpen not before +5 s, then handshake + activity resent
[Fact] public void Ping_IsAnsweredWithPong() // pipe yields a Ping frame {"x":1}: a Pong frame with the same json is written
```

- [ ] **Step 2: Run, expect FAIL** `dotnet test tests/InvectorModKit.Marketplace.Client.Tests --filter DiscordPresenceTests`
- [ ] **Step 3: Implement `DiscordPresence`.** `nonce` = a counter as string. `Tick` catches everything and logs it once per disconnect.
- [ ] **Step 4: Run the whole client test project, expect PASS** `dotnet test tests/InvectorModKit.Marketplace.Client.Tests`
- [ ] **Step 5: Commit** `feat(presence): connect to Discord and pace activity updates`

### Task 4: In-game tracker, texts and settings

**Files:**
- Create: `src/InvectorModKit.Marketplace/Presence/PresenceTracker.cs` (MonoBehaviour)
- Modify: `src/InvectorModKit.Marketplace/MarketplacePlugin.cs` (`Awake`: config entries, add the component when enabled)
- Modify: `src/InvectorModKit.Marketplace/Loc.cs` (new rows)
- Modify: `README.md` (one line in the features list: Discord rich presence, how to turn it off)

**Interfaces:**
- Consumes: `DiscordPresence`, `Win32DiscordPipe`, `PresenceSnapshot`, `PresenceTexts`, `PresenceScreen`.

- [ ] **Step 1: Theo creates the Discord application** (guided in chat): discord.com/developers/applications, New Application named `Invector: Rhythm Galaxy`; Rich Presence > Art Assets: `logo` (game logo, 1024x1024), `casual`, `normal`, `hard`, `ultra` (difficulty icons). Copy the Application ID into the `ApplicationId` default.
- [ ] **Step 2: Add Loc rows** in the 11 languages: `presence_menu` "In the main menu", `presence_select` "Choosing a song", `presence_marketplace` "Browsing the marketplace", `presence_paused` "Paused", `presence_results` "Looking at the results", `presence_score` "{0} pts · combo {1}", `presence_custom` "Custom song", `presence_get_mod` "Get InvectorModKit" (32 chars max in every language), `presence_join` "Join the Discord" (32 max).
- [ ] **Step 3: Implement `PresenceTracker`.** Once a second on the main thread, build a snapshot:
  - `StateGame` with a session: `Playing` (or `Paused` when `ModKit.CurrentState is StateGamePause`); title/artist from `levelDefinition.MusicDefinition.SongName`/`ArtistName`; custom when CustomSongs knows the `levelId` (`CustomSongsApi`); difficulty from `FirstPlayer.playerGameSession.Difficulty`; score `PlayerScore.ScorePoints`, combo `PlayerScore.CurrentPickupStreak`; remaining = `musicHandler.MusicPlayer.SongEndTime - musicHandler.RealPlaybackTime`, clamped to 0 (check in game that it counts down to 0 at the song's end; else use `Song.loadedGameplayAudio.clipLength`).
  - A running `InvectorLevelEditor` process (`Process.GetProcessesByName`, sampled every 5 s, checked first): `Hidden`, so only the editor's own card shows; `StateGameResult`: `Results`; `StateSelectSongSingleplayer`/`Multiplayer`: `SongSelect`; `MarketplaceScreen.IsOpen`: `Marketplace`; anything else: `Menu`.
  - Texts from `Loc` each time (language can change), `ModUrl` `https://github.com/theovil1/invectormodkit/releases/latest`, `DiscordUrl` `Community.DiscordUrl`, `ModVersion` `"InvectorModKit " + ModKit.Version`.
  - `Start()` the `DiscordPresence` (pid `Process.GetCurrentProcess().Id`, log to `MarketplacePlugin.Log`); `Dispose` it in `OnDestroy` and `OnApplicationQuit`.
- [ ] **Step 4: Build** `dotnet build InvectorModKit.slnx -c Release -p:DeployToGame=false`, expect 0 errors.
- [ ] **Step 5: Verify in game** (ask Theo before launching; launch through Steam): with Discord open, check the profile in the main menu, song select, marketplace, a level (song, difficulty, score refreshing, countdown), pause, results; quit Discord mid-level and reopen it. Send screenshots of the profile card. Check the BepInEx log has no repeated errors.
- [ ] **Step 6: Commit** `feat(presence): show the game on the player's Discord profile`

### Task 5: Level editor presence

**Files:**
- Modify: `tools/ChartEditor/ChartEditor.csproj` (reference `src/InvectorModKit.Marketplace.Client`)
- Modify: `src/InvectorModKit.ChartEditor.Core/EditorSettings.cs` (`public bool DiscordPresence = true;`)
- Modify: `tools/ChartEditor/MainForm.cs` (new `host.presence` message; own `DiscordPresence`, started at window load when the setting is on, disposed on close)
- Modify: `tools/ChartEditor/ui/app.js` (send `host.presence`), `tools/ChartEditor/ui/i18n.js` (new rows)
- Test: `tools/ChartEditor/ui/tests/i18n.test.js` already checks every row is complete; run it.

**Interfaces:**
- Consumes: `DiscordPresence`, `Win32DiscordPipe`, `PresenceSnapshot` (`Screen = Editor`, `Title`, `Artist`, `Difficulty`, `Notes`), `PresenceTexts`.
- Produces: message `host.presence` with `{ title, artist, difficulty, notes, texts: { editor, notesOne, notesMany, getMod, join, lang } }`; `title` empty when no song is open. The host fills `ModUrl`, `DiscordUrl` (`https://discord.gg/9wXNm5NM26`, the plugin's default invite) and `ModVersion` (`"InvectorModKit " + ` the editor's assembly version).

- [ ] **Step 1: Add i18n rows** in the 11 languages: `presenceEditor` "Making a level", `presenceNotesOne` "{0} note", `presenceNotesMany` "{0} notes", `presenceGetMod` "Get InvectorModKit" (32 max), `presenceJoin` "Join the Discord" (32 max).
- [ ] **Step 2: Run** `cd tools/ChartEditor/ui; node --test tests/i18n.test.js`, expect PASS (every row has 11 entries).
- [ ] **Step 3: Send `host.presence` from `app.js`** next to `host.title` (same moments: song opened, difficulty changed, edit made, language changed), with the current difficulty's note count `this.editor.doc.notes[this.difficulty].length`. Only when `inHost`; failures ignored like `host.title`.
- [ ] **Step 4: Handle `host.presence` in `MainForm.cs`**: build the snapshot and texts, call `Update`. Session start = editor window load time.
- [ ] **Step 5: Build** `dotnet build tools/ChartEditor/ChartEditor.csproj -c Release`, expect 0 errors; check `bin/Release/net48` holds `InvectorModKit.Marketplace.Client.dll`.
- [ ] **Step 6: Verify** (Discord open, game closed, then game open): editor with no song, then a song, changing difficulty and adding notes updates the card (within 15 s); with the game running, only the editor's card shows, and closing the editor brings the game's back within 5 s. Screenshots of the card.
- [ ] **Step 7: Commit** `feat(editor): show the level being made on the player's Discord profile`

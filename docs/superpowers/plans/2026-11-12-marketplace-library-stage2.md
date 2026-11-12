# Marketplace stage 2: My library and My publications Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the interim "My songs" tab and the "Add by link" window with two tabs of the full-screen marketplace: My library (every song on this PC, adding from a link, publishing) and My publications (the player's published charts, their stats, new versions, video link, withdrawal).

**Architecture:** Pure logic (filters, button states, publication state, chart content hash, API calls) goes in `InvectorModKit.Marketplace.Client` with xunit tests; the two tabs reuse stage 1's building blocks (`UiKit`, `KeyBar`, `ScreenInput`, `PreviewPlayer`/`PreviewCache`, the screen's list helpers) and share one new `Sheet`. The link download becomes a service that outlives the screen. The server gains publication stats and a video link change.

**Tech Stack:** C# net472 (BepInEx, Unity 2021.3 UI, Harmony), Newtonsoft.Json; xunit (net8.0) for the client; Cloudflare Worker + D1 + vitest for the server.

**Spec:** `docs/superpowers/specs/2026-11-12-marketplace-redesign-design.md` (stage 2 of Delivery; tabs 2 and 3).

## Global Constraints

- Every user-facing string exists in the 11 languages (en, fr, de, it, es, pt-BR, pl, ru, ja, ko, zh) in `Loc.cs`; no em dash in any of them.
- Every button label keeps `UiKit.Margin` (24 units of 1920) on each side, shrinks with `WordFit` (never below `UiKit.MinFont` 18) and never leaves its button.
- Mouse, keyboard and controller all work; the key bar shows the keys of the current tab.
- Server: work only in a worktree under `C:\Users\Theo\source\repos\invectormodkit-tools-worktrees\`; never touch the main checkout (it has uncommitted work). Deploying is the owner's call.
- The game repo: worktree `InvectorModKit-worktrees\feat-marketplace-library`, branch `feat/marketplace-library` (from main a24061c).
- Builds use `-p:DeployToGame=false` except to deploy for an in-game check; the game is launched with `steam://rungameid/2009450`; the player's save is never touched.
- Before driving the game: close Medal (all `Medal*` processes); never press F7 to F10 (Medal and DevTools hotkeys).
- Commits and PRs: English, no AI mention, no Co-Authored-By; PRs ready (not draft); merge only with the owner's agreement.

## Review Focus

1. Not signed in: PUBLISH in My library and the My publications tab first ask to sign in (the screen's `EnsureSignedIn`); nothing is sent before. Checked in game (Task 10).
2. A server not yet deployed: `/me/charts` without `players`/`reports` gives null (the stats line leaves them out); a video link change answered 404 or 405 says the server does not support it yet. Test `MyCharts_StatsMissingAreNull` (Task 3).
3. A publication whose song is not on this PC (published from another PC, or its song deleted): its row says NOT ON THIS PC and PUBLISH VERSION is inactive. Test `Publication_NoLocalSongIsNotOnThisPc` (Task 5).
4. A publication made before this stage (no content hash remembered): "changed since" falls back on the note counts. Test `Publication_NoHashComparesNotes` (Task 5).
5. Closing the marketplace during a link download: the download continues and the song arrives; reopening shows it in progress or done. Checked in game (Task 10).

---

### Task 1: Server: publication stats in `/me/charts`

**Files:**
- Modify: `server/src/publish.ts` (`myCharts`), `server/src/types.ts`
- Test: `server/test/publish.test.ts`

Worktree: `git -C C:\Users\Theo\source\repos\invectormodkit-tools worktree add ..\invectormodkit-tools-worktrees\feat-publication-stats -b feat/publication-stats origin/feat/catalogue-preview-time` (stacked on the open PR #2, which also changes `myCharts`); `server/node_modules` is a junction to the main checkout's.

**Interfaces:**
- Produces: `GET /me/charts` items = `ChartSummary` + `players: number` (distinct non-banned players with a score on the chart, any version or difficulty) + `reports: number` (rows in `reports` for the chart). Type `MyChart extends ChartSummary`.

- [ ] **Step 1: Write the failing test** `it("my charts give ranked players and reports")`: publish as a publisher; insert scores for that chart from two players (two difficulties for one of them) and from a banned player; insert two reports; `GET /me/charts` → `players` 2, `reports` 2.
- [ ] **Step 2: Run** `npx vitest run test/publish.test.ts` — Expected: FAIL (`players` undefined).
- [ ] **Step 3: Implement** two correlated subqueries in `myCharts`' SELECT (`COUNT(DISTINCT s.steam_id)` joined to `users` with `role != 'banned'`; `COUNT(*)` from `reports`), mapped onto the summary.
- [ ] **Step 4: Run** `npm test` — Expected: all pass.
- [ ] **Step 5: Commit** `feat(publications): ranked players and reports in my charts`.

### Task 2: Server: change a chart's video link

**Files:**
- Modify: `server/src/publish.ts` (new `changeVideo`), `server/src/index.ts` (route)
- Test: `server/test/publish.test.ts`

**Interfaces:**
- Produces: `PATCH /charts/:id` body `{ youtubeUrl }` → the chart's `ChartSummary`. Author only (403 otherwise, 403 "banned" for a banned author), 404 missing chart, 400 not a YouTube video link, 409 when the author already has another chart for that video. Only `youtube_id` and `updated_at` change; version, scores and stats stay.

- [ ] **Step 1: Write the failing tests**: `author changes the video link` (200, `youtubeId` is the new one, `version` unchanged); `others cannot change the video link` (403); `invalid video link is 400`; `video of another of my charts is 409`.
- [ ] **Step 2: Run** `npx vitest run test/publish.test.ts` — Expected: FAIL (404 route).
- [ ] **Step 3: Implement `changeVideo(request, env, user, id)`** reusing `youtubeId`, `findChart`, `readJson`, `HttpError`.
- [ ] **Step 4: Run** `npm test` — Expected: all pass.
- [ ] **Step 5: Commit** `feat(publications): change a chart's video link`; push, open the PR against `feat/catalogue-preview-time` (ready), body says it builds on #2.

### Task 3: Client API: stats and video link

**Files:**
- Modify: `src/InvectorModKit.Marketplace.Client/Models.cs`, `MarketplaceApi.cs`
- Test: `tests/InvectorModKit.Marketplace.Client.Tests/MarketplaceApiTests.cs`

**Interfaces:**
- Produces: `ChartSummary.Players`, `ChartSummary.Reports` (`int?`, null when the server does not send them); `Task<ChartSummary> MarketplaceApi.ChangeVideoAsync(string chartId, string youtubeUrl)` (PATCH `/charts/{id}`, bearer).

- [ ] **Step 1: Write the failing tests**: `MyCharts_ReadsStats` (`"players":3,"reports":1` → 3 and 1); `MyCharts_StatsMissingAreNull`; `ChangeVideo_SendsPatchWithBearer` (method `PATCH`, url ends `/charts/abc`, body has `youtubeUrl`, bearer set, result parsed).
- [ ] **Step 2: Run** `dotnet test tests/InvectorModKit.Marketplace.Client.Tests` — Expected: FAIL to compile.
- [ ] **Step 3: Implement** in `Summary(JObject)` and a `ChangeVideoAsync` beside `PublishAsync`.
- [ ] **Step 4: Run** the tests — Expected: PASS.
- [ ] **Step 5: Commit** `feat(marketplace): read publication stats, change a video link`.

### Task 4: Chart content hash

**Files:**
- Create: `src/InvectorModKit.Marketplace.Client/ChartContent.cs`
- Test: `tests/InvectorModKit.Marketplace.Client.Tests/ChartContentTests.cs`

**Interfaces:**
- Produces: `static string ChartContent.Hash(string chartJson)`: SHA-256 (lowercase hex) of the chart's top-level object without its `generator` and `source` blocks (the server drops the same two), serialized with `Formatting.None`; null when the text is not a JSON object.

- [ ] **Step 1: Write the failing tests**: `Hash_IgnoresSourceAndGenerator` (same chart with and without `"source":{"marketplaceId":"x"}` and a `generator` block → equal); `Hash_ChangesWithANote` (one note's time changed → different); `Hash_NullForNonObject` (`"[]"`, `"not json"` → null).
- [ ] **Step 2: Run** — Expected: FAIL to compile.
- [ ] **Step 3: Implement** with `JObject.Parse`, `Remove`, `ToString(Formatting.None)`, `SHA256`.
- [ ] **Step 4: Run** — Expected: PASS.
- [ ] **Step 5: Commit** `feat(marketplace): a chart's content hash, blind to its source block`.

### Task 5: What the sheets' main buttons say (library, publications) and the library filters

**Files:**
- Create: `src/InvectorModKit.Marketplace.Client/SheetButton.cs`, `LibraryState.cs`, `PublicationState.cs`
- Modify: `src/InvectorModKit.Marketplace.Client/CatalogueFilter.cs`
- Test: `tests/.../LibraryStateTests.cs`, `PublicationStateTests.cs`, `CatalogueFilterTests.cs`

**Interfaces:**
- Produces:
  - `sealed class SheetButton { string LabelKey; object[] LabelArgs; bool Enabled; string SubKey; object[] SubArgs; int? Percent; bool Accent; bool Warning; }`
  - `sealed class LibraryFacts { bool Mine, Published, LinkValid, Publishing, Adding, Preparing; int PublishedVersion; double Progress; string Author, PublishStep, AddError; }` and `static SheetButton LibraryState.For(LibraryFacts f)`, in this order: Adding (`main_adding`, percent, sub `add_step_download`), Preparing (`main_adding` 100, sub `main_step_prepare`), AddError (`main_add_failed`, inactive, warning, sub = the error key), not Mine (`main_installed`, inactive, sub `origin_marketplace` with Author), Published (`main_published`, inactive, sub `main_published_sub` with the version), Publishing (`main_publishing`, inactive, sub = PublishStep), else `main_publish`, accent, enabled = LinkValid, sub `only_chart_sent` (LinkValid) or `link_needed`.
  - `enum PublicationRow { UpToDate, Changed, Hidden, NotOnThisPc }`; `sealed class PublicationFacts { int ServerVersion, Reports; bool Hidden, LocalSongFound, NotesMatch, Publishing; string LocalHash, PublishedHash; }`; `sealed class PublicationView { PublicationRow Row; bool Changed; SheetButton Main; }`; `static PublicationView PublicationState.For(PublicationFacts f)`: changed = PublishedHash != null ? LocalHash != PublishedHash : !NotesMatch; no local song → `main_not_on_pc` inactive, sub `not_on_pc_sub`; Publishing → `main_publishing`; changed → `main_publish_version` (ServerVersion + 1), accent, sub `changed_warning` (ServerVersion); else `main_up_to_date` inactive. Row: Hidden wins, then NotOnThisPc, Changed, UpToDate.
  - `enum LibraryShown { All, Mine, FromMarketplace, Published }`; `sealed class LibraryItem { string Id, Title, Artist; bool Mine, Published; }`; `static List<LibraryItem> CatalogueFilter.Library(IEnumerable<LibraryItem> items, LibraryShown shown)`: sorted by artist then title, ignoring case.

- [ ] **Step 1: Write the failing tests**:
  - `Library_OwnUnpublishedOffersPublish` (link valid → enabled, accent, `only_chart_sent`); `Library_NoLinkCannotPublish` (enabled false, `link_needed`); `Library_PublishedSaysWhere`; `Library_MarketplaceSongSaysWhose` (`origin_marketplace`, args `["Alice"]`); `Library_AddingShowsPercent` (Progress 0.42 → 42); `Library_AddFailedShowsReason`.
  - `Publication_UpToDate`; `Publication_ChangedOffersNextVersion` (ServerVersion 2 → label args `[3]`, sub args `[2]`); `Publication_NoHashComparesNotes`; `Publication_NoLocalSongIsNotOnThisPc`; `Publication_HiddenRowEvenWhenChanged`.
  - `Library_FiltersAndSorts` (four filters on five items; order by artist then title).
- [ ] **Step 2: Run** — Expected: FAIL to compile.
- [ ] **Step 3: Implement** the three files and `CatalogueFilter.Library`.
- [ ] **Step 4: Run** — Expected: PASS (and the 60 existing tests).
- [ ] **Step 5: Commit** `feat(marketplace): library and publication button states, library filters`.

### Task 6: The shared sheet, the link download service, paste while typing

**Files:**
- Create: `src/InvectorModKit.Marketplace/Ui/Sheet.cs`, `src/InvectorModKit.Marketplace/LinkDownloads.cs`
- Modify: `Ui/ScreenInput.cs` (Ctrl+V while typing), `Ui/PreviewCache.cs` (local songs)
- Delete later (Task 9): `Ui/AddSong.cs`

**Interfaces:**
- Produces:
  - `Sheet(Transform parent, Action main, Action listen)` with `Root`, `CoverButton`, `Main`, `Middle` (a `RectTransform` between the facts and the main button, for the tab's own content), `SetSong(string title, string artist, Sprite cover, string info)`, `SetMain(SheetButton b, string label, string sub)` (Loc'd strings), `SetActions(params (string label, Action act, bool danger)[] buttons)`, `SetMore(params (string label, Action act, bool danger)[] buttons)`, `ActionButtons()`, `MoreButtons()`, `SetExcerpt(string)`, `SetVisible(bool)`. Same layout and styles as `ChartSheet` (cover 260, title, artist, info; main 110 high with a progress fill; actions and More rows of 60).
  - `static class LinkDownloads`: `Start(string youtubeLink)`, `IReadOnlyList<LinkDownload> All`, `event Action Changed` (main thread); `LinkDownload { string VideoId, Title; double Progress; bool Preparing, Done; string ErrorKey, ErrorDetail; }`; the steps of today's `AddSongModal.Run` and `WaitChart` (yt-dlp name, `link_exists`, download with `AudioFetch.ProgressOf`, move, thumbnail, metadata reload, wait for the chart up to 180 s), driven by `MarketplacePlugin`'s `Update`, so it continues with the screen closed. Progress is written from yt-dlp's thread: keep it in a field read on the main thread only.
  - `ScreenInput`: while `Typing`, Ctrl+V adds the clipboard (trimmed, line breaks removed) to `Typed`.
  - `PreviewCache.GetLocal(string songId, string previewPath, Action<AudioClip, double, double> ready, Action failed)`.

- [ ] **Step 1: Build** `dotnet build src/InvectorModKit.Marketplace -c Release -p:DeployToGame=false` after each file — Expected: 0 errors. (Unity code: checked in game in Task 10.)
- [ ] **Step 2: Commit** `feat(marketplace): shared sheet, link downloads that outlive the screen, paste while typing`.

### Task 7: My library

**Files:**
- Create: `src/InvectorModKit.Marketplace/Ui/LibraryTab.cs`
- Modify: `src/InvectorModKit.Marketplace/MarketplaceSession.cs` (`RememberPublished(songId, chartId, link, hash)`, `PublishedHash(songId)`), `Loc.cs`

**Interfaces:**
- Consumes: Task 4 `ChartContent.Hash`, Task 5 `LibraryState.For`, `CatalogueFilter.Library`, Task 6 `Sheet`, `LinkDownloads`, `PreviewCache.GetLocal`.
- Produces: `LibraryTab : ITab` (same members as `DiscoverTab`: `Show`, `Hide`, `Tick`, `Listen`, `Search`, `Typing`, `FirstItem`, `LastRow`); `Ui/Publishing.cs`, moved from `MySongsTab` for both tabs: `static void Publishing.Publish(MarketplaceScreen screen, LocalSong s, string link, Action<string> step, Action<ChartSummary> done, Action<string> failed)` (sign-in first, fingerprint, send, the 428 rules confirmation, remember the link and `ChartContent.Hash`, pin), `static void Publishing.Pin(LocalSong s, string chartId, int version, string author)`, `static void Publishing.OpenInEditor(LocalSong s, Action<string> status)`. Withdrawing moves to My publications.

Layout like Discover: on the left the "+ Add a song from a YouTube link" button (opens the screen's `TextPrompt`, filled from the clipboard), the four filter chips, the list (ongoing link downloads first, then the songs; tags MINE, PUBLISHED, HIDDEN, MARKETPLACE); on the right the `Sheet`: origin line and note counts in its info; for the player's own unpublished chart, in `Middle`: the link field (a button; Enter, A or a click starts typing; filled with the remembered link or a YouTube link on the clipboard), the check line ("Checking the video…", then "Video found: <title>" from `VideoInfo`, or "Not a YouTube video link"), the reminder that only the chart is sent. Main button from `LibraryState`. Actions: Level editor, More… (Open the folder; Remove from game for marketplace songs only). Space plays the song's local excerpt. Keys: Enter (main), Space, Tab, Esc.

New `Loc` rows (en / fr; the other 9 languages written alongside): `tab_library` MY LIBRARY / MA BIBLIOTHÈQUE; `add_from_link` Add a song from a YouTube link / Ajouter un morceau depuis un lien YouTube; `filter_mine` My charts / Mes charts; `filter_from_marketplace` From the marketplace / Du marketplace; `filter_published` Published / Publiés; `tag_mine` MINE / À MOI; `tag_published` PUBLISHED / PUBLIÉ; `tag_marketplace` MARKETPLACE / MARKETPLACE; `origin_own` Your chart, not published yet / Ton chart, pas encore publié; `origin_marketplace` Chart by {0}, from the marketplace / Chart de {0}, du marketplace; `notes_line` Casual {0} · Normal {1} · Hard {2} · Ultra {3}; `link_field` YouTube link of this song's video / Lien de la vidéo YouTube de ce morceau; `link_checking` Checking the video… / Vérification de la vidéo…; `video_found` Video found: {0} / Vidéo trouvée : {0}; `only_chart_sent` Only the chart is sent, never the music: players get it from this video. / Seul le chart est envoyé, jamais la musique : les joueurs la récupèrent depuis cette vidéo.; `link_needed` Paste the link of the song's YouTube video / Colle le lien de la vidéo YouTube du morceau; `main_publish` PUBLISH / PUBLIER; `main_publishing` PUBLISHING… / PUBLICATION…; `main_published` PUBLISHED / PUBLIÉ; `main_published_sub` Version {0}, followed in My publications / Version {0}, suivie dans Mes publications; `main_adding` ADDING {0} % / AJOUT {0} %; `add_step_download` Fetching the song from YouTube / Récupération du morceau sur YouTube; `main_add_failed` NOT ADDED / PAS AJOUTÉ.

- [ ] **Step 1: Write `LibraryTab`, the session hash, the Loc rows**; publishing remembers `ChartContent.Hash` of the published chart.
- [ ] **Step 2: Build** — Expected: 0 errors; `dotnet test tests/InvectorModKit.Marketplace.Client.Tests` still passes.
- [ ] **Step 3: Commit** `feat(marketplace): My library, adding and publishing in the sheet`.

### Task 8: My publications

**Files:**
- Create: `src/InvectorModKit.Marketplace/Ui/PublicationsTab.cs`
- Modify: `Loc.cs`

**Interfaces:**
- Consumes: Task 3 `MyChartsAsync`, `ChangeVideoAsync`; Task 4; Task 5 `PublicationState.For`; Task 6 `Sheet`; Task 7 `Publishing.Publish`, `Publishing.Pin`.
- Produces: `PublicationsTab : ITab`.

On showing: not signed in → the list says so and the main button is SIGN IN (the screen's `SignIn`); else `MyChartsAsync`, failures shown in the list with Retry; the local song of each chart is the one pinned to it (`MarketplaceId`) or remembered by the session. The pinning and "keep only live publications" of `MySongsTab.SyncWithServer` move here. Rows: the title, then "version N · up to date / changed since / hidden, N reports / not on this PC · N installs · N ranked players" under it (each count left out when null). Sheet info: installs, ranked players, reports (each left out when null), the video's title (`VideoInfo`); a warning line when changed. Main button from `PublicationState` (publishing a new version sends the local chart with the remembered link). Actions: Watch the video, Change the video link (`TextPrompt`, then `ChangeVideoAsync`; 404/405 → `server_too_old`), Withdraw publication… (confirmation, delete, forget, unpin: today's `MySongsTab.Withdraw`). Space plays the local excerpt, else the YouTube one.

New `Loc` rows: `tab_publications` MY PUBLICATIONS / MES PUBLICATIONS; `pub_up_to_date` up to date / à jour; `pub_changed` changed since / modifié depuis; `pub_hidden` hidden, {0} reports / masqué, {0} signalements; `pub_not_on_pc` not on this PC / pas sur ce PC; `pub_row` version {0} · {1}; `pub_installs` {0} installs / {0} installations; `pub_players` {0} ranked players / {0} joueurs classés; `pub_reports` {0} reports / {0} signalements; `main_publish_version` PUBLISH VERSION {0} / PUBLIER LA VERSION {0}; `main_up_to_date` UP TO DATE / À JOUR; `changed_warning` Your chart changed since version {0}: publish it so players get it. / Ton chart a changé depuis la version {0} : publie-le pour que les joueurs l'aient.; `main_not_on_pc` NOT ON THIS PC / PAS SUR CE PC; `not_on_pc_sub` Its song is not in your CustomSongs / Son morceau n'est pas dans tes CustomSongs; `change_link` Change the video link / Changer le lien de la vidéo; `link_changed` Video link changed / Lien de la vidéo changé; `server_too_old` The server does not do this yet / Le serveur ne le permet pas encore; `sign_in_publications` Sign in to see your publications / Connecte-toi pour voir tes publications; `no_publications` No chart published yet: publish one from My library. / Aucun chart publié : publie-en un depuis Ma bibliothèque.

- [ ] **Step 1: Write `PublicationsTab` and the Loc rows.**
- [ ] **Step 2: Build and test** — Expected: 0 errors, client tests pass.
- [ ] **Step 3: Commit** `feat(marketplace): My publications, stats, new versions, video link, withdrawal`.

### Task 9: The screen: three tabs on their own row

**Files:**
- Modify: `Ui/MarketplaceScreen.cs`, `Ui/EntryPoints.cs` (if it names `MySongsTab`), `Loc.cs`
- Delete: `Ui/MySongsTab.cs`, `Ui/AddSong.cs`

**Interfaces:**
- Consumes: `DiscoverTab`, `LibraryTab`, `PublicationsTab`.

The top bar becomes two rows: row 1 keeps the title, the account, Sign in, Discord, Rules and X; row 2 holds the tabs, equal widths over the whole width (room for the fourth tab of stage 3 in every language). The body starts under row 2. `Box`, `_addLink`, `Regular`/`Bold`/`SemiBold` (only for My songs) go. Tab and Shift+Tab (RB/LB) cycle through the three tabs. Loc rows only My songs used (`tab_mine`, `mine_help`, `add_link`, `add_link_question`, the AddSong strings) are deleted if nothing else uses them.

- [ ] **Step 1: Make the change**; `grep -rn "MySongsTab\|AddSongModal\|\.Box\b" src` — Expected: nothing.
- [ ] **Step 2: Build and test the whole solution** `dotnet build InvectorModKit.slnx -c Release -p:DeployToGame=false` and `dotnet test InvectorModKit.slnx -c Release -p:DeployToGame=false` — Expected: 0 errors, all tests pass.
- [ ] **Step 3: Commit** `feat(marketplace): My library and My publications replace My songs`.

### Task 10: In the game

Close Medal; deploy (`dotnet build src/InvectorModKit.Marketplace -c Release`); launch via Steam. Check and capture:

- [ ] Each tab with the mouse, the keyboard and (if one is plugged in) a controller; nothing reaches the menu underneath.
- [ ] My library: the four filters; adding a song from a link (progress in its row, then the song in the list); closing the marketplace during a download and reopening; a marketplace song's sheet; the player's own unpublished chart: link from the clipboard, a wrong link, "Video found"; publishing while signed out asks to sign in; the level editor; Space plays the excerpt.
- [ ] My publications (with the server deployed by the owner, else stats left out): up to date, changed since (edit a chart in the level editor), PUBLISH VERSION N, change the video link, withdraw (then publish again); signed out; offline.
- [ ] The three tabs in the 11 languages (F6 with `[Debug] LanguageCycling = true`, restored afterwards), screenshots checked for labels touching their edges.
- [ ] Push, open the PR (ready), with the results and the screenshots' findings; report to the owner with screenshots.

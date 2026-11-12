# Marketplace stage 3: Moderation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Admins moderate from the game: a Moderation tab (reported, hidden, all charts, banned players), the chart actions (hide or show, clear reports, delete, ban the author), score actions on every Discover leaderboard (delete a score, ban the player), hidden charts visible to admins in Discover.

**Architecture:** The server's existing admin API (`/admin/api/...`, used by the web admin page with a cookie) also accepts the game's bearer token of an admin account. The game gains admin calls and pure state logic in `InvectorModKit.Marketplace.Client` (xunit), a `ModerationTab` built on stage 2's `Sheet`, and per-entry actions on Discover's leaderboard.

**Tech Stack:** C# net472 (BepInEx, Unity UI), Newtonsoft.Json, xunit (net8.0); Cloudflare Worker + D1 + vitest.

**Spec:** `docs/superpowers/specs/2026-11-12-marketplace-redesign-design.md` (stage 3 of Delivery; tab 4 and "In every leaderboard").

## Global Constraints

- Every user-facing string in the 11 languages in `Loc.cs`; no em dash.
- Labels keep `UiKit.Margin`, shrink with `WordFit` (never below 18) and never leave their buttons.
- Every action that cannot be undone (delete a chart, delete a score, ban) asks first in the screen's confirmation, Cancel preselected.
- Nothing changes locally until the server agreed; a failed action says why on the sheet.
- The tab exists only for an admin account (`Session.CanModerate`); others never see it or its strings.
- Server: worktree under `C:\Users\Theo\source\repos\invectormodkit-tools-worktrees\` from `origin/main`; never the main checkout. Deploying and applying migrations are the owner's to run.
- Game repo: worktree `InvectorModKit-worktrees\feat-marketplace-moderation`, branch `feat/marketplace-moderation` from main a1f4fbb.
- Builds `-p:DeployToGame=false` except to deploy for an in-game check; launch with `steam://rungameid/2009450`; close Medal first; never F7 to F10; the save is never touched.
- In-game checks never act on real players or charts without the owner's yes: moderation actions are tried on a test chart and a test account the owner names, or shown without confirming.
- Commits and PRs in English, no AI mention, PRs ready; merge only with the owner's agreement.

## Review Focus

1. A non-admin with a bearer token calling the admin API gets 403, a banned account too, and an admin's own role cannot be changed (it would lock them out). Tests `admin api refuses a player's token`, `admin api refuses a banned token` (Task 1).
2. Hidden charts reach admins only: a player's catalogue never lists them, signed in or not. Tests `catalogue hides hidden charts from players`, `catalogue shows hidden charts to admins, marked` (Task 2).
3. Deleting a score uses the board's own version and difficulty (the numbers the server stores), not the sheet's labels. Test `DeleteScore_SendsBoardNumbers` (Task 3) and an in-game check (Task 7).
4. An admin who loses the role while the screen is open: actions answered 401/403 say "not an admin any more" and the tab's list empties. Test `Moderation_RefusedSaysNotAdmin` (Task 4).
5. Today's server (before deployment): the Moderation tab says the server does not do this yet (404 on the bearer path), Discover keeps working. Checked in game before the owner deploys (Task 7).

---

### Task 1: Server: the admin API accepts an admin's bearer token

**Files:** Modify `server/src/web/admin.ts`; Test `server/test/admin.test.ts`

Worktree: `git -C C:\Users\Theo\source\repos\invectormodkit-tools worktree add ..\invectormodkit-tools-worktrees\feat-admin-bearer -b feat/admin-bearer origin/main`; `server/node_modules` a junction to the main checkout's.

**Interfaces:**
- Produces: every `/admin/api/*` route also answers a request with `Authorization: Bearer <game token>` whose account role is `admin` (via `auth.ts`' session lookup). With a bearer, POSTs need no `x-admin` header (no cookie, no CSRF). No bearer and no admin cookie: 401; a bearer of a non-admin or banned account: 403. Cookie behaviour unchanged.

- [ ] **Step 1: Write the failing tests**: `admin api accepts an admin's token` (GET reports 200 with the owner's token); `admin api refuses a player's token` (403); `admin api refuses a banned token` (403); `admin api POST works with a token and no x-admin` (hide a chart → 200, chart hidden).
- [ ] **Step 2: Run** `npx vitest run test/admin.test.ts` — Expected: FAIL (401).
- [ ] **Step 3: Implement** in `adminRoute`: when no admin cookie, read the bearer user; role `admin` → that admin; other roles → 403.
- [ ] **Step 4: Run** `npm test` — Expected: all pass.
- [ ] **Step 5: Commit** `feat(admin): the admin API accepts an admin's game token`.

### Task 2: Server: hidden charts in the catalogue for admins, ban dates

**Files:** Modify `server/src/catalog.ts`, `server/src/index.ts`, `server/src/web/admin.ts`; Create `server/migrations/0007_ban_date.sql`; Test `server/test/catalog.test.ts`, `server/test/admin.test.ts`

**Interfaces:**
- Produces: `GET /charts` with an admin's bearer also lists hidden charts (`hidden: true`); players and anonymous callers never see them. `users.banned_at` (INTEGER, null) set when the role becomes `banned`, cleared otherwise; `GET /admin/api/users?banned=1` lists banned players only, each with `bannedAt`. `GET /admin/api/reports` gives each chart's `authorSteamId` (to ban the author).

- [ ] **Step 1: Write the failing tests**: `catalogue hides hidden charts from players`; `catalogue shows hidden charts to admins, marked`; `banning records the date and lifting clears it`; `users can be listed banned only`; `reports give the author's steam id`.
- [ ] **Step 2: Run** — Expected: FAIL.
- [ ] **Step 3: Implement**: the migration (`ALTER TABLE users ADD COLUMN banned_at INTEGER;`), the catalogue's hidden filter depending on the caller, `POST user` setting `banned_at`, `GET users` with `banned=1`.
- [ ] **Step 4: Run** `npm test` — Expected: all pass.
- [ ] **Step 5: Commit** `feat(admin): hidden charts for admins in the catalogue, ban dates`; push; open the PR (ready), saying the migration must be applied before deploying.

### Task 3: Client: admin models and calls

**Files:** Modify `src/InvectorModKit.Marketplace.Client/Models.cs`, `MarketplaceApi.cs`; Test `tests/.../MarketplaceApiTests.cs`

**Interfaces:**
- Produces:
  - `sealed class ReportDetail { string Reason, Name, SteamId; long At; }`; `sealed class ReportedChart { string Id, Title, Artist, Author, AuthorSteamId; bool Hidden; int Version, Reports; long Last; List<ReportDetail> Details; }`; `sealed class AdminChart { string Id, Title, Artist, Author, AuthorSteamId; bool Hidden; int Version, Downloads, Reports; long UpdatedAt; }`; `sealed class AdminUser { string SteamId, Name, Role; long CreatedAt; long? BannedAt; int Charts, Scores; }`.
  - `MarketplaceApi`: `Task<List<ReportedChart>> ReportsAsync()`, `Task<List<AdminChart>> AdminChartsAsync(string query)`, `Task<List<AdminUser>> BannedAsync()`, `Task ChartActionAsync(string chartId, string action)` (`hide`, `show`, `clear-reports`, `delete`), `Task DeleteScoreAsync(string steamId, string chartId, int version, int difficulty)`, `Task SetRoleAsync(string steamId, string role)`; all with the bearer, under `/admin/api/`.

- [ ] **Step 1: Write the failing tests**: `Reports_ReadsChartsAndDetails`; `ChartAction_SendsIdAndAction`; `DeleteScore_SendsBoardNumbers` (version 3, difficulty 2 as numbers); `SetRole_SendsSteamIdAndRole`; `Banned_AsksBannedOnly` (url has `banned=1`).
- [ ] **Step 2: Run** — Expected: FAIL to compile.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** — Expected: PASS.
- [ ] **Step 5: Commit** `feat(marketplace): admin calls for moderation`.

### Task 4: Client: what the moderation sheets' buttons say

**Files:** Create `src/InvectorModKit.Marketplace.Client/ModerationState.cs`; Test `tests/.../ModerationStateTests.cs`

**Interfaces:**
- Produces: `enum ModerationShown { Reported, Hidden, All, Banned }`; `static SheetButton ModerationState.ForChart(bool hidden, bool busy)`: hidden → `mod_show` (SHOW IT AGAIN), else `mod_hide` (HIDE THIS CHART), accent, enabled unless busy; `static SheetButton ModerationState.ForPlayer(bool busy)`: `mod_lift_ban` (LIFT THE BAN…); `static string ModerationState.Refused(int status)`: 401/403 → `mod_not_admin`, 404 → `server_too_old`, else null.

- [ ] **Step 1: Write the failing tests**: `Moderation_HiddenOffersShow`, `Moderation_VisibleOffersHide`, `Moderation_BusyWaits`, `Moderation_PlayerOffersLift`, `Moderation_RefusedSaysNotAdmin` (401 and 403), `Moderation_OldServerSaysSo` (404).
- [ ] **Step 2: Run** — FAIL. **Step 3: Implement.** **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat(marketplace): moderation button states`.

### Task 5: The Moderation tab

**Files:** Create `src/InvectorModKit.Marketplace/Ui/ModerationTab.cs`; Modify `Ui/MarketplaceScreen.cs` (the tab only for `Session.Current.CanModerate`), `Loc.cs`

**Interfaces:**
- Consumes: Task 3 calls, Task 4 states, stage 2 `Sheet`, the screen's `Confirm`, `Prompt` (search), `Keys`.
- Produces: `ModerationTab : ITab`.

Left: four chips with counts (Reported N, Hidden N, All charts, Banned players); F searches in All charts (the screen's typing, as in Discover); the list. Sheet of a chart: title, artist, author, version, downloads, reports count; in `Middle` the reports (reason, who, when, newest first); main from `ModerationState.ForChart` (HIDE THIS CHART / SHOW IT AGAIN); actions: Clear the reports, Watch the video; More…: Delete the chart… (danger), Ban the author… (danger). Sheet of a banned player: name, banned since, charts and scores counts; main LIFT THE BAN… Every action with a confirmation names the chart or the player; after the server agrees, the lists reload and the result shows on the note line; refused → `ModerationState.Refused` text or the error.

New `Loc` rows (en / fr; the 9 others alongside): `tab_moderation` MODERATION / MODÉRATION; `mod_reported` Reported ({0}) / Signalés ({0}); `mod_hidden_filter` Hidden ({0}) / Masqués ({0}); `mod_all` All charts / Tous les charts; `mod_banned_filter` Banned players / Joueurs bannis; `mod_hide` HIDE THIS CHART / MASQUER CE CHART; `mod_show` SHOW IT AGAIN / LE RÉAFFICHER; `mod_clear` Clear the reports / Effacer les signalements; `mod_delete` Delete the chart… / Supprimer le chart…; `mod_ban_author` Ban the author… / Bannir l'auteur…; `mod_lift_ban` LIFT THE BAN… / LEVER LE BANNISSEMENT…; `mod_report_line` {0} · {1}: “{2}” / {0} · {1} : « {2} »; `mod_banned_since` Banned since {0} / Banni depuis le {0}; `mod_confirm_hide` Hide “{0}” from the catalogue? / Masquer « {0} » du catalogue ?; `mod_confirm_delete` Delete “{0}” with its scores? This cannot be undone. / Supprimer « {0} » avec ses scores ? C'est définitif.; `mod_confirm_ban` Ban {0}? Their charts and scores leave the marketplace. / Bannir {0} ? Ses charts et scores quittent le marketplace.; `mod_confirm_lift` Lift {0}'s ban? / Lever le bannissement de {0} ?; `mod_done` Done. / C'est fait.; `mod_not_admin` This account is not an admin any more. / Ce compte n'est plus administrateur.; `mod_nothing` Nothing to moderate here. / Rien à modérer ici.

- [ ] **Step 1: Write the tab, the screen's conditional tab, the Loc rows.**
- [ ] **Step 2: Build** `dotnet build src/InvectorModKit.Marketplace -c Release -p:DeployToGame=false` — Expected: 0 errors; client tests pass.
- [ ] **Step 3: Commit** `feat(marketplace): the Moderation tab for admins`.

### Task 6: Discover: leaderboard actions and hidden charts for admins

**Files:** Modify `Ui/ChartSheet.cs`, `Ui/DiscoverTab.cs`, `Loc.cs`

**Interfaces:**
- Consumes: Task 3 `DeleteScoreAsync`, `SetRoleAsync`; `BoardList.Version`, `BoardEntry.SteamId/Name/Score`.
- Produces: `ChartSheet.SetBoardEntries(IList<(string line, Action select)> entries)` (admins: one small button per board line; others keep the text).

For an admin, each leaderboard line is a button; choosing one puts "Delete this score…" and "Ban this player…" (both danger, both confirmed) in the sheet's More row and opens it. The score is deleted with the board's version and the difficulty number the leaderboard request used. A chart the server marks hidden gets the tag HIDDEN in its row and a note on its sheet.

New `Loc` rows: `tag_hidden` HIDDEN / MASQUÉ; `mod_delete_score` Delete this score… / Supprimer ce score…; `mod_ban_player` Ban this player… / Bannir ce joueur…; `mod_confirm_score` Delete {0}'s score of {1} on this chart? This cannot be undone. / Supprimer le score de {1} de {0} sur ce chart ? C'est définitif.; `hidden_admin_note` Hidden: only admins see it. / Masqué : seuls les admins le voient.

- [ ] **Step 1: Make the change.** **Step 2: Build and test** — 0 errors, tests pass.
- [ ] **Step 3: Commit** `feat(marketplace): admins act on leaderboard entries, see hidden charts`.

### Task 7: In the game

Close Medal; deploy the plugin; launch via Steam. Before the owner deploys the server: the Moderation tab says the server does not do this yet, Discover unchanged. After the owner deploys: the four filters and their counts; a reported chart's sheet with its reports; on a test chart the owner names: hide, show, clear reports; a leaderboard entry's actions shown (confirmed only on a test account the owner names); the 11 languages on the Moderation tab (F6 with `[Debug] LanguageCycling`, restored afterwards); a non-admin account never sees the tab (owner's call whether to check). Push, open the PR (ready), report with screenshots.

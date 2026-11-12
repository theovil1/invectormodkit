# Marketplace: a full screen that is clear to use

Date: 2026-11-12. Status: design approved in chat (mockups in the owner's private canvas "Marketplace Invector,
pistes"), spec waiting for the owner's review.

## Context

The in-game marketplace (`src/InvectorModKit.Marketplace/Ui`) is a window drawn over the main menu or song select,
built from the game's own dialog: a "Browse" tab, a "My songs" tab, an "Add by link" prompt, Discord and rules
buttons. The owner finds it very hard to use:

- nothing is clear or easy: finding a chart, installing it, publishing one's own;
- the menu's music keeps playing behind it;
- some key presses reach the menu underneath;
- songs cannot be heard before installing them;
- buttons are crowded, several labels are close to their edges or overflow;
- many buttons with close names ("Remove", "Remove from game", "Withdraw publication").

This spec replaces that window with a full screen of the game. It keeps everything the marketplace does today
(catalogue, install with the audio fetched from YouTube, leaderboards, reports, publishing, the level editor button,
add a song from a YouTube link) and adds previews and moderation in the game.

## Goal

A player finds a chart, hears it, installs it and plays it without wondering what to do next; an author publishes
and follows their charts; an admin moderates without leaving the game. With a mouse, a keyboard or a controller.

Success criteria:

- While the marketplace is open, no key, button or click changes anything in the menu underneath, and the menu's
  music is silent; closing it gives both back as they were.
- Resting on a chart for under a second starts its excerpt, at its strongest part, faded in and out.
- Every action the player can take on the selected chart is visible on its sheet, with one main button that says
  what happens next (install, progress, retry, installed, update).
- No label touches or leaves its button, in any of the 11 languages.
- Everything works with the mouse, the keyboard and a controller, and the bottom bar shows the keys for the current
  screen.

## Owner's decisions

- Layout A of the mockups: a full screen, list on the left, sheet on the right (not a grid of covers, not a window).
- Mouse, keyboard and controller, all three comfortable.
- Excerpts of charts that are not installed come from YouTube on demand (about 20 s, fetched with the bundled
  yt-dlp); the server never stores audio, as today ("only the chart is sent, never the audio").
- Excerpts start at the chart's preview point (the strongest part), with a fade in and a fade out.
- When the audio cannot be fetched: "RETRY" with the reason, and "or put the audio file in CustomSongs: it is
  recognised on its own" with a button that opens the folder. No file picker in the game.
- Moderation in the game for admins: reports and hiding, deleting a chart, deleting a score, banning a player.
- Delivered in three stages, each usable on its own (see Delivery).

## What players see

### The screen

- "Marketplace" in the main menu, and the M key or the "MARKETPLACE" hint in song select, open the screen over the
  whole display. Esc or B goes back to where it was opened.
- Top bar: "MARKETPLACE", the tabs, then on the right the Discord logo (no glow), "Rules" and the account (name,
  "admin" for admins, or "Sign in").
- Body: the list on the left (about 560 px of a 1280 px wide reference), the selected item's sheet on the right.
- Bottom bar: the keys for this screen, each as the game's own keyboard key and controller button pictures (the
  ones song select uses), followed by what it does.
- The look is the game's: its fonts, its white selection bar with dark text, its translucent dark panels, its
  violet accent.

### Controls

| Action | Keyboard | Controller | Mouse |
| --- | --- | --- | --- |
| Move in the list and between buttons | arrows | stick, d-pad | hover |
| Main button of the sheet | Enter | A | click |
| Play / pause the excerpt | Space | X | the cover's play button |
| Search | F (then type, Enter or Esc) | Y | click the field |
| Next / previous tab | Tab / Shift+Tab | RB / LB | click |
| Back (close a prompt, then the screen) | Esc | B | the close button |

The list keeps the selection visible when it scrolls; the wheel scrolls it.

### Tabs

1. **Discover**: the catalogue.
   - Search field, then one-click filters: All, Not installed, Installed; sort (popular, recent); difficulty.
   - Rows: cover, title, artist and chart author, a tag (INSTALLED, NEW, UPDATE), length.
   - Sheet: cover with play button, title, artist, author and version, BPM, length, downloads; the four
     difficulties with their note counts (choosing one shows its leaderboard); the leaderboard (top entries and the
     player's own); the main button; "Watch the video"; "Report" (a flag).
2. **My library**: every song on this PC, the player's own and those installed from the marketplace.
   - "Add a song from a YouTube link" on top of the list (today's add-by-link flow, its progress shown in the list).
   - Filters: All, My charts, From the marketplace, Published.
   - Sheet: the song, where its chart comes from, its note counts. For the player's own unpublished chart, the
     publish form is part of the sheet: the YouTube link field (filled from the clipboard when it holds a YouTube
     link), checked as typed ("Video found: <its title>"), the reminder that only the chart is sent, and the main
     button PUBLISH. Other actions: Level editor, and "More…" (open the folder, remove from game).
3. **My publications**: the charts the player published.
   - Rows: title, version and state (up to date, changed since, hidden with N reports), installs, ranked players.
   - Sheet: installs, ranked players, reports; the video; a warning when the local chart changed since the published
     version. Main button "PUBLISH VERSION N" (or "UP TO DATE", inactive); "Change the video link"; "Withdraw
     publication…".
4. **Moderation** (admins only; the tab does not exist for others).
   - Filters: Reported (count), Hidden (count), All charts, Banned players.
   - Sheet of a chart: its reports (reason, who, when). Main button HIDE THIS CHART (or SHOW IT AGAIN); "Clear the
     reports"; "Delete the chart…"; "Ban the author…".
   - In every leaderboard, an admin also gets "Delete this score…" and "Ban this player…" on an entry.
   - Hidden charts show to admins in Discover with a HIDDEN tag.
   - Banned players: name, date; "Lift the ban…".
   - Every action that cannot be undone (delete, ban) asks first, in the screen's own confirmation.

### The main button

One button per sheet, its label following the chart's state:

| State | Label | Under it |
| --- | --- | --- |
| Not installed | INSTALL | The chart and the video's music, in one click |
| Installing | INSTALLING 64 % (a filling bar) | Downloading the chart / Fetching the music |
| Music not fetched | RETRY (yellow outline) | The reason in plain words, then "or put the audio file in CustomSongs: it is recognised on its own" and "Open the folder" |
| Installed | INSTALLED (white, a tick) | To play in Singleplayer, custom songs |
| A newer version | UPDATE | Version N of the chart is out |
| The player's own chart | YOURS | (no action; publishing is in My library and My publications) |

The player can browse other charts while one installs; its row shows the progress too.

### Excerpts

- Resting on a row for 0.8 s plays its excerpt; moving on stops it with a short fade.
- The excerpt starts at the chart's preview point (the `preview` time of the chart: placed by the author in the
  level editor, else chosen by the chart generator), lasts 20 s, fades in over 0.6 s and out over 1.2 s, then starts
  again.
- Songs on this PC: from the excerpt the Custom Songs plugin already prepares for song select (instant).
- Charts not installed: about 20 s of the video's audio, fetched on demand with yt-dlp (only that section). While it
  loads, the cover shows "Loading the excerpt". It is kept in a cache for a while and then deleted.
- If the excerpt cannot be fetched, the cover says "Excerpt unavailable"; nothing else changes.
- The excerpt plays at the game's music volume.

### Isolation from the menu

- On opening: the main menu's music pauses (song select's preview fades out); on closing it resumes where it was
  (song select plays its selected song's preview again).
- While open, the menus underneath receive no input at all: no navigation, no confirm or back, no hotkey, no click.
  Today's leak is investigated first (systematic debugging) to find every path the input still takes.

### Labels always fit

- Every button keeps an inner margin of at least 16 px on each side (reference 1280 px wide).
- A label that does not fit shrinks (down to a minimum size), then wraps on two lines; it never leaves its button.
- Checked on every screen in the 11 languages; German and Russian are the longest.

## Architecture

### In the game (`src/InvectorModKit.Marketplace`)

- `Ui/MarketplaceScreen`: the full screen (one canvas over the whole display, its own scaler), its top bar, tab
  host, bottom key bar and the confirmation and text prompts. Replaces `MarketplaceModal`.
- One file per tab: `DiscoverTab`, `LibraryTab`, `PublicationsTab`, `ModerationTab`, sharing a list and a sheet
  building block (`ChartList`, `Sheet`, `MainButton`). The current `BrowseTab` and `MySongsTab` logic (install,
  audio fetch, publish, withdraw, pinning, leaderboards, reports) moves into them or into the services below.
- `ScreenInput`: owns input while the screen is open: our own explicit navigation (arrows, stick, d-pad, Enter,
  A, B, Space, X, F, Y, Tab, LB, RB, mouse), and blocks the menus underneath (the existing `Update` prefixes, the
  `EventSystem` navigation, plus whatever the investigation finds).
- `MenuAudio`: pauses and resumes the menu's music (`StateMainMenu.PauseMusic` / `UnpauseMusic`; song select's
  `menuSceneRoot.TransitionOutPreviewMusic` / `PlayLastPreviewMusic`).
- `PreviewPlayer`: one `AudioSource` on the game's music mixer group, with the fades and the loop.
- `PreviewCache`: fetches YouTube excerpts with yt-dlp into `BepInEx/cache/InvectorModKit/previews` and deletes
  them after 7 days or beyond 200 MB, oldest first.
- `KeyHints`: the bottom bar, with the game's key and button pictures.
- The entry points (`EntryPoints`), the corner badge, the update check and the leaderboard submission stay as they
  are, pointed at the new screen.

### Pure logic, tested (`src/InvectorModKit.Marketplace.Client`)

- `ChartState`: from what is known about a chart (installed, downloaded, audio fetching or failed and why, local
  version, published version, mine) to the main button's state and label keys.
- `CatalogueFilter`: the filters and sorts of Discover and My library.
- `ExcerptWindow`: start, end and fades from the preview time and the song's length (an excerpt never runs past the
  end; a preview time missing or past the end falls back to 30 % of the song, as charts do).
- `AudioFetch.BuildSectionArgs`: yt-dlp's arguments for one section.
- `PreviewCache` eviction rules.
- The new API calls (moderation, publication stats).

### On the server (the `invectormodkit-tools` repository)

- The catalogue's chart summaries gain `preview` (seconds), read from the chart when it is published (existing
  charts: from their stored chart).
- `GET /me/charts` gains, per chart, `players` (ranked players) and `reports`.
- Moderation from the game: the existing admin API (`/admin/api/...`: reports, charts, scores, users and their
  actions) also accepts the game's bearer token when its account is an admin. The web admin page keeps working as
  it does.
- `GET /charts` returns hidden charts, marked, to admins only.

## Errors and edge cases

- Offline or server unreachable: Discover says so in its list with a "Retry" button; My library still works.
- Not signed in: publishing and moderation first ask to sign in, as today.
- yt-dlp missing: installs and excerpts fall back as today (the video opens in the browser, the folder advice).
- A chart removed or hidden while its sheet is open: the sheet says so, its main button is inactive.
- Moderation actions that fail say why on the sheet; nothing changes locally until the server agreed.
- Long names: titles and artists are cut with an ellipsis in rows, wrap on the sheet.

## Delivery

Each stage is one pull request (plus one in the server repository where noted), usable on its own:

1. **The screen and Discover**: the full screen, input and audio isolation, key bar, previews, the main button and
   its states, labels that fit. The current "My songs" tab is kept as it is inside the new screen until stage 2.
   Server: `preview` in the catalogue.
2. **My library and My publications**: replace "My songs" and "Add by link". Server: publication stats.
3. **Moderation**: the tab and the leaderboard actions. Server: admin API with the game's token, hidden charts for
   admins.

## Testing

- Unit tests (`tests/InvectorModKit.Marketplace.Client.Tests`) for every piece of pure logic above.
- Server tests (vitest, in the server repository) for the new fields and the admin API with a bearer token
  (admin accepted, player refused, banned refused).
- In the game, each stage checked by hand, with the results in the pull request:
  - every key and controller button pressed in each tab changes nothing in the menu underneath;
  - the music stops on opening and comes back on closing, from the main menu and from song select;
  - an excerpt for an installed song and for a chart not installed, its fade in, fade out and loop;
  - each main button state, including a failed audio fetch;
  - the same screens in the 11 languages, screenshots checked for labels touching their edges;
  - the whole flow with a controller only, and with the mouse only.

## Out of scope

- Starting a song directly from the marketplace (the installed state says where to find it).
- Audio stored on the server, in any form.
- A grid of covers or any other layout than A.
- Changing how scores are computed or verified.

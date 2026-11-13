# Level editor: export a song as a zip, import it on another PC

## Intent

A player who made a level in the editor wants to send it to a friend without the marketplace (a private chart,
a test, a song the marketplace does not take). A chart's timing only fits the exact audio file it was made on, so
the audio has to travel with it. Today the player has to find the game's `CustomSongs` folder and zip the right
files by hand, and the friend has to unzip them at the right depth. Both sides get one button instead.

## Goals

- **Export** (editor top bar, after Publish): writes one `Artist - Title.zip` holding everything the mod needs to
  play the song on another PC.
- **Import a zip…** (the Open window, next to "Other file…"): puts such a zip into the player's `CustomSongs`, in
  the song's own folder, and opens it in the editor. The game picks it up without a restart.
- Unzipping by hand also works, whether the files are dropped loose into `CustomSongs` or Windows' "Extract all"
  makes a folder for them.

## Non-goals

- No import inside the game itself (game plugin, other open pull requests touch it).
- No re-encoding: the zip is about the size of the audio. Most mp3/ogg songs (3 to 9 MB) fit Discord's free 10 MB
  limit; a long `.wav` will not.
- No marketplace link: an exported marketplace chart arrives as an ordinary hand-made chart, without its
  leaderboard. The marketplace stays the way to share a chart with its leaderboard.
- Several songs in one zip.

## Anti-goals

- A zip that can write outside the chosen `CustomSongs` folder, or fill the disk.
- A friend whose game regenerates the chart: they must play exactly what was sent.

## Zip content (export)

Files at the zip's root, no folder around them (a folder inside the zip ends up two levels deep after Windows'
"Extract all" and the mod would not see it):

| Entry | From |
|---|---|
| `<audio file name>` (e.g. `Song.ogg`) | the open song's audio, name unchanged |
| `<audio name>.chart.json` | the chart shown in the editor, written as Save writes it (hand-made: no generator or marketplace block, so never regenerated) |
| `<audio name>.jpg` / `.png` / `.jpeg` | the cover the game shows, if any: `cover.*`/`folder.*` when the song is in its own folder in `CustomSongs`, else the image named like the audio. Stored under the audio's name so it works loose or in a folder |
| `song.json` | `{"title", "artist"}` from the editor's fields |

- Unsaved changes are saved first (the player's own file then matches what was sent).
- The zip is written to a temporary file and moved into place, so a failed export leaves no broken zip.
- The default file name is `SongFiles.FolderName(artist, title) + ".zip"`; the Windows "Save as" dialog lets the
  player choose where (it remembers the last folder; the first time, the Desktop).
- Works for a song outside `CustomSongs` too (opened with "Other file…").

## Import

1. "Import a zip…" opens a Windows file dialog filtered on `*.zip`. Without a game folder, the settings open
   instead (as Send to game does).
2. The zip is checked before anything is written. It is refused, with a translated message, when:
   - an entry's path is absolute or contains `..`;
   - it has more than 50 entries, or more than 300 MB once unpacked;
   - it does not hold exactly one audio file (`.mp3`, `.ogg`, `.wav`).
3. Entries are taken by file name only (at the zip's root or inside folders, as when someone zips a song folder by
   hand); only the audio, one `.chart.json`, one image and `song.json` are kept, anything else is skipped.
4. They are written into a new `CustomSongs\<Artist - Title>` folder (title and artist from `song.json`, else the
   zip's name; `(2)`, `(3)`… when the name is taken). The chart is saved as `<audio name>.chart.json`, the image
   as `<audio name>.<ext>`.
5. The song opens in the editor (it asks about unsaved changes first, as Open does) and the status bar says it is
   now in the game's Custom Songs.

## Fix found on the way

`SongFiles.Read` takes title and artist from a `song.json` next to the audio even in the `CustomSongs` root,
where the game ignores it. A zip dropped loose would leave such a file and the editor would give its title to every
loose song. The editor follows the game: `song.json` counts only in a song's own folder.

## Where the code goes

- `src/InvectorModKit.ChartEditor.Core/SongPackage.cs` (new): `Export(...)` builds the zip, `Import(...)` checks
  and extracts it. `System.IO.Compression`, already used by the installer: no new dependency.
- `EditorBackend.cs`: `export` and `import` methods; `SongFiles.cs`: the root `song.json` fix.
- `tools/ChartEditor/MainForm.cs`: `host.saveZip` and `host.openZip` (the Windows dialogs only).
- `tools/ChartEditor/ui/index.html`, `app.js`, `i18n.js`: the Export button, "Import a zip…" in the Open window,
  status and error texts in the 11 languages.
- Tests first in `tests/InvectorModKit.ChartEditor.Tests` (zip content, cover choice, unsaved changes, every
  refusal, folder naming, the `song.json` fix).

## Approaches considered

- **Zip work in the tested editor library, dialogs in the window (chosen):** CI tests every rule; the window only
  shows Windows dialogs.
- Zip built in the interface (JavaScript): needs a new zip library (a dependency to add).
- Zip built in the window's code: works, but CI cannot test it.

## Done

Tests first and green in every suite, the editor building, a ready pull request from a branch cut from
`origin/main`, and Theo's check in the real editor: export a song, import the zip (and unzip one by hand into
`CustomSongs`), then see the song play in the game.

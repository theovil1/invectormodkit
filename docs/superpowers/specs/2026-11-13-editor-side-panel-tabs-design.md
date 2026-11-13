# Level editor: side panel tabs

## Why

The editor's left side panel stacks five panels (Song, Difficulties, Selection, Difficulty tools, Playability) in one
column that scrolls, but nothing shows it: a 6 px scrollbar with a faint thumb. Even maximized on a 1920 x 1080 screen
the column shows about 490 px of its 1 120 px, so Selection, Tools and Playability sit below the fold and players
never find them. At the window's minimum size (1100 x 680) the column is 267 px tall.

## Goals

- Every part of the side panel is one click away and its name is always visible.
- When a part does not fit, it is obvious that there is more below, and easy to get there.
- The game's look: square white buttons and bars, the game's arrow picture, stepped (blocky) fades rather than smooth ones.

Non-goals: moving panels out of the side column, changing what the panels contain, new keyboard shortcuts.

## Approaches considered

Compared on mockups made in the editor with a real song open (maximized and at the minimum size):

- A. Visible scrolling: a wide white scrollbar and a "More below: Selection, Tools, Playability" band at the bottom
  edge whose names jump to the panel. Smallest change, but the selected note's panel still needs scrolling.
- B. Folding panels: A, plus a click on a panel title folds it.
- C. Tabs: Song, Notes, Tools, Playability, one at a time. Nothing below the fold when maximized, but two tabs are
  never seen together.

Theo chose all three, then, of the two ways to combine them, "real tabs" (over tabs used as shortcuts in one column).

## Design

- A row of tabs, fixed at the top of the side column: **Song** (Song panel), **Notes** (Difficulties + Selection),
  **Tools** (Difficulty tools), **Playability** (Playability, with the number of problems in a badge: green 0, red
  otherwise, none while no song is open). Only the chosen tab's panels show. Active tab: white with dark text, like the
  game's focused buttons. Labels wrap onto a second row when a language's words do not fit.
- The editor opens on **Notes**. Changing tabs scrolls the column back to its top.
- In the Notes tab, Difficulties and Selection fold and unfold with a click on their title (the game's difficulty arrow,
  pointing right when folded, down when open). The editor remembers folded panels between sessions.
- Below the tabs the panels scroll with a visible scrollbar: 12 px wide, square white thumb, violet while hovered.
- When the current tab does not fit, a band sits on the bottom edge of the column: a stepped fade, the game's arrow
  pointing down, "More below:" and a button per panel whose title is hidden below. A name scrolls its panel to the top;
  the rest of the band scrolls down by most of a screen. The band disappears once the column is scrolled to the end.
- The Playability list loses its own 220 px limit: it has the whole tab and scrolls with the column.

## Builder's calls (not discussed with Theo)

- Clicking a problem in Playability, or a note in the timeline, does not switch tabs (going through the problem list,
  or using the selection with the Tools, keeps working).
- Only the panels that share a tab fold (a lone panel folded would leave an empty tab).
- The tab is not remembered: a fresh start shows Notes.
- New texts in the 11 languages: tab names "Notes", "Tools", "Playability" (short forms of the panel titles) and
  "More below:". The Song tab reuses the existing "Song" text.

## Constraints

- Other editor branches are open (free note lanes, song export, Discord presence): keep `app.js` and `i18n.js` edits
  small and away from their hunks; the logic goes into a new module `ui/sidepanel.js`.
- The pure parts (which panels are hidden below, reading the remembered folds) get node tests in `ui/tests`.
- Check at the minimum window size and maximized, in the real editor, before the pull request.

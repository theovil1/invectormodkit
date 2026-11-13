# Level editor: any lane for any note, without a turn

## Intent

A player asked for the level editor to let face notes go on any lane even without a turn note leading there:
"In some of the harder difficulties, the turn notes are omitted to improve flow, patterns, and increase difficulty."
Today the editor forces lanes in Tunnel (List) and Road (Bounce) zones: a pickup's lane only changes through a turn,
and the playability alerts call any other lane change an error.

## Facts checked first

- The game places every pickup at the lane written in the level (`GeneratorListPattern`, `GeneratorBounce` read
  `Strum.lane`); turns move nothing. The player can roll or steer at any time with the direction input, a pickup
  scores only when the ship is on its lane, and the game's own autopilot steers to a pickup on another lane with no
  turn. The mod already sends each note's lane as is (`GameAssets.ApplyChart`). No chart format or plugin change.
- The 41 official levels do it: lane changes between two non-turn pickups happen 0 times in Casual, once in Normal,
  104 times in Hard and 380 times in Ultra (Road: 271 times in 18 songs). On the Road it is always to the next lane.
  Shortest time before such a change: Tunnel 0.377 s (Hard) and 0.433 s (Ultra), Road 0.330 s (Hard) and 0.234 s
  (Ultra, a turn then straight back to the old lane). Right after a turn the minimum is about 0.07 to 0.12 s.
- The editor's 3D preview already slides the ship to the next pickup's lane when there is no turn.

## Goals

- Any pickup (arrow or turn) can sit on any lane in every zone: click on a lane row, drag up or down, Up/Down keys,
  and the lane buttons of a selected note.
- Nothing moves by itself: a note stays exactly where it was put. Adding, flipping or deleting a turn moves no other
  note; the author moves the notes after it if they want them to follow.
- Opening a chart keeps the lanes written in the file (no silent rewrite).
- The alerts warn only about lane changes that are too fast, with limits measured on the official charts.

## Non-goals

- The generator does not start making lane changes without turns (its charts stay valid as they are).
- No change to the chart file format, the game plugins or the marketplace server.
- Up/Down keep stopping at the edge lanes (no wrap around the tunnel).

## Approaches

1. Turns push the notes after them (like today) while notes placed by hand stay put: keeps today's comfort when
   inserting a turn, but the rule is subtle (notes slide until the first one placed by hand).
2. **Nothing moves by itself (chosen by Theo)**: every lane is just what the author set, like the game's data. Simplest
   rule; inserting a turn in the middle of a passage means moving the rest by hand (select, then Up/Down).
3. A switch between today's behaviour and free lanes: dropped, two behaviours to learn and more texts to translate.

## Design

- `tools/ChartEditor/ui/model.js`: `normalize` stops deriving lanes from turns; `relane` and `laneIsFree` go away.
  `moveNotes`, `setLane` and the timeline drag change the lanes of the notes they touch and nothing else.
  `laneAfterTurn` stays. Regenerating a zone or a selection keeps today's result for the notes it creates: their lanes
  follow their turns from the pickup before them (the generator's own rule), and the notes after the range keep theirs.
  Copy to another difficulty and paste copy lanes as they are.
- `tools/ChartEditor/ui/app.js`: the lane buttons show for any single selected pickup; the "its lane follows the turns"
  line goes away.
- `src/InvectorModKit.Charting/Playability.cs`: the rules `lane-change` (lane change without a turn) and `turn-lane`
  (lane does not follow the turn) are replaced by one rule, `lane-time`: in Tunnel and Road the ship is on the previous
  pickup's lane, or on the lane its turn leads to; reaching another lane needs at least the zone's limit (Road: once
  per lane crossed). Limits in seconds, Casual/Normal/Hard/Ultra: Tunnel 0.45/0.37/0.37/0.37, Road 0.33/0.33/0.33/0.23.
  Hard and Ultra come from the official minimums (rounded down, Ultra never stricter than Hard); Casual and Normal,
  never seen in official charts, take the Hard value or the time needed after a turn, whichever is longer. The other
  rules (turn timing, turn off the road, gaps) stay as they are.
- `tools/ChartEditor/ui/i18n.js`: a new alert text for `lane-time` in the 11 languages; the texts of the removed rules
  and of the removed line go away.
- 3D preview and timeline: nothing to change (they draw each note at its own lane); checked in the editor.

## Verification

- Tests first: `tools/ChartEditor/ui/tests/model.test.js` (lanes kept on load, after adding/removing/flipping turns,
  with Up/Down, buttons and regeneration) and `tests/InvectorModKit.Charting.Tests` (the new rule).
- All suites green: `node --test` in `tools/ChartEditor/ui`, `dotnet test` for Charting and ChartEditor.
- The official charts, replayed offline through the new rules, raise no lane alert.
- Theo checks a build of the editor started from the worktree (the installed editor, a build of the Discord presence
  branch, stays untouched). The game reads lanes as before, so no game test is required.

## Left to the builder

Function and test names, the exact alert wording, and how the regeneration keeps its lanes.

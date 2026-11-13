# Free note lanes in the level editor: implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let any pickup sit on any lane in Tunnel and Road zones, move nothing by itself, and warn only about lane changes that are too fast.

**Architecture:** The editor's document model (`model.js`) stops deriving lanes from turns; only regeneration gives its new notes turn-derived lanes. The C# playability rules replace "lane change without a turn" and "lane does not follow the turn" by one timing rule measured on the official charts. The interface shows the lane buttons for every pickup and gets one new alert text.

**Tech Stack:** plain ES modules tested with `node --test`; C# netstandard2.0 tested with xUnit on net8.0.

**Spec:** `docs/superpowers/specs/2026-11-13-editor-free-note-lanes-design.md`

## Global Constraints

- Every text a player sees exists in the 11 languages, in this order: en, fr, de, it, es, pt-BR, pl, ru, ja, ko, zh (`ui/i18n.js`).
- Never use em dashes in any text (UI, docs, commits).
- Commits: Conventional Commits in English, no `Co-Authored-By` line, no mention of an AI tool (CI rejects them).
- Write the failing test first, then make it pass. Comments say what code is for, terse, like the surrounding code.
- No change to the chart file format, the game plugins or the server.
- Lane-change limits in seconds (Casual, Normal, Hard, Ultra): Tunnel 0.45, 0.37, 0.37, 0.37; Road 0.33, 0.33, 0.33, 0.23; Road: once per lane crossed.

## Review Focus

- A chart file written by hand whose lanes do not follow its turns: opening and saving it must keep every lane (the old editor rewrote them).
- Changing a zone's mode (Tunnel to Road): lanes stay as they are, even where a Road turn now points off the road.
- A live drag in the timeline (`normalize` runs on every mouse move, then `commit`): the dragged lanes must not snap back.
- Undo after a lane edit restores the previous lanes.
- A turn followed by a pickup on another lane than the turn leads to: legal with enough time (official Ultra Road does it in 0.234 s), flagged below the limit.

---

### Task 1: Free lanes in the document model

**Files:**
- Modify: `tools/ChartEditor/ui/model.js` (lane rule section, `normalize`, `moveNotes` comment, `replaceRange`)
- Test: `tools/ChartEditor/ui/tests/model.test.js`

**Interfaces:**
- Produces: `replaceRange(doc, d, notes, from, to): number[]` (ids of the notes it added); `followTurns(doc, d, ids): void` (the listed pickups take the lanes their turns lead to, chained from the pickup before the first of them in the same zone mode; Freefly and strums untouched); `laneAfterTurn` kept; `relane` and `laneIsFree` removed; `normalize(doc)` no longer touches lanes.

- [ ] **Step 1: Write the failing tests**

In `model.test.js`:
- `file round trip keeps what ChartJson writes`: the expected Hard notes become `[{ t: 2, lane: 0 }, { t: 3, color: 4 }, { t: 4 }, { t: 5, strum: true, len: 0.8 }, { t: 41, lane: 2 }]` (the file's own lanes; drop the "follows it" comment).
- Replace `lanes follow turns in List and Bounce, ...` by `every pickup keeps its own lane, turns move nothing`: open the same notes as today (`{ t: 18, lane: 2 }, { t: 19, color: 5 }, { t: 21, lane: 2 }, { t: 22, color: 5 }, { t: 23 }, { t: 24, color: 4 }, { t: 25 }, { t: 41, lane: 0 }, { t: 44, lane: 2 }`) and assert lanes `[2, 1, 2, 1, 1, 1, 1, 0, 2]`. Then through an `Editor`: add a `{ t: 18.5, color: 5, lane: 2 }` turn, `setColor` the t=19 turn to 4, remove the t=24 turn, `setSectionMode(doc, 1, "List")`; after each, the lanes of the notes that were already there are unchanged.
- `lane edits change only the notes they touch`: a List run `{ t: 2 }, { t: 3, color: 5 }, { t: 4 }, { t: 5 }`; `setLane(doc, "Hard", [id of t=4], 0)` gives lanes `[1, 1, 0, 1]`; `moveNotes(doc, "Hard", [ids of t=3 and t=5], 0, 1)` gives `[1, 2, 0, 2]`; `moveNotes` by +1 again clamps at 2 (`[1, 2, 0, 2]`); two `undo()` give `[1, 1, 0, 1]`. Then a live drag: take `snapshot()`, set the t=2 note's lane to 0 directly, call `m.normalize(doc)` then `commit(snapshot)`: lanes `[0, 1, 0, 1]`. (Do these edits through `Editor.apply`.)
- `regenerated notes follow their turns from the note before them`: List zone, existing `{ t: 1, lane: 2 }` and `{ t: 12, lane: 1 }`; `const ids = m.replaceRange(doc, "Hard", [{ t: 3 }, { t: 4, color: 5 }, { t: 5 }], 2, 10)`; `m.followTurns(doc, "Hard", ids)`; after `normalize`, lanes `[2, 2, 2, 0, 1]` (2, then 2, the turn on 2, wrap to 0; t=12 untouched). Same in a Bounce zone gives `[2, 2, 2, 2, 1]` (no wrap).
- In `copy a range to another difficulty, ...`: assert the pasted notes keep their lanes (`[0, 1]`, the Hard notes at t=2 and t=3).

- [ ] **Step 2: Run them to see them fail**

Run: `node --test tests/model.test.js` (from `tools/ChartEditor/ui`)
Expected: FAIL in the four tests above (lanes rewritten; `followTurns` is not a function).

- [ ] **Step 3: Implement**

In `model.js`: `normalize` sorts and clamps times as today, without `relane`; delete `relane` and `laneIsFree`; `replaceRange` returns the ids it added; add `followTurns(doc, d, ids)` next to `laneAfterTurn` (walk the difficulty's notes sorted by time, keep the previous pickup and its mode, give each listed pickup the lane its predecessor leads to when both are in the same non-Freefly mode); `moveNotes` comment: `dLane` moves the lanes of the notes it moves. Update the section comment over the lane rule: the game places each pickup at its own lane, a turn asks the player to steer.

- [ ] **Step 4: Run all interface tests**

Run: `node --test tests/model.test.js tests/i18n.test.js tests/audio.test.js tests/keys.test.js` then `node --test` (from `tools/ChartEditor/ui`)
Expected: PASS (the i18n test still expects `lane-change`, which still exists at this point).

- [ ] **Step 5: Commit**

```bash
git add tools/ChartEditor/ui/model.js tools/ChartEditor/ui/tests/model.test.js
git commit -m "feat(editor): keep every note on the lane it is given"
```

### Task 2: Lane change timing rule

**Files:**
- Modify: `src/InvectorModKit.Charting/Playability.cs` (class summary, new limits, the pickup loop, `PlayabilityIssue.Code` summary)
- Test: `tests/InvectorModKit.Charting.Tests/PlayabilityIssuesTests.cs`

**Interfaces:**
- Produces: `Playability.MinLaneChangeList = { 0.45, 0.37, 0.37, 0.37 }`, `Playability.MinLaneChangeBounce = { 0.33, 0.33, 0.33, 0.23 }`, `Playability.MinLaneChange(SectionMode mode, Difficulty d): double`; issue code `lane-time` (Mode set, Gap = seconds since the previous pickup); codes `lane-change` and `turn-lane` no longer produced.

- [ ] **Step 1: Write the failing tests**

Add a `Chart Make(SectionMode mode, Difficulty d, params Note[] notes)` helper (ListChart calls it with List and Hard). Replace `LaneChangeWithoutTurn_HasModeAndCode` by:
- `LaneChangeWithoutTurn_WithTime_IsPlayable`: Tunnel Hard, Down on lane 1 at 10.0 then lane 2 at 12.0: no issue.
- `LaneChangeWithoutTurn_TooFast_HasModeCodeAndGap`: Tunnel Hard, lane 1 at 10.0 then lane 2 at 10.30: single issue, `("lane-time", 10.30, SectionMode.List)`, `Gap` 0.30 (3 digits).
- `CasualTunnel_NeedsMoreTime`: Tunnel Casual, lane 1 at 10.0 then lane 2 at 10.40: single `lane-time`.
- `Road_TwoLanes_NeedTwiceTheTime`: Road Ultra, lane 0 at 10.0 then lane 2 at 10.40: single `lane-time`; same with 10.50: no issue.
- `AfterATurn_TheShipStartsOnTheLaneTheTurnLeadsTo`: Road Ultra, TurnLeft on lane 1 at 10.0 then Down on lane 1 at 10.25: no issue (back in 0.25 s); lane 0 at 10.25: no issue; lane 1 at 10.20: single `lane-time`.

- [ ] **Step 2: Run them to see them fail**

Run: `dotnet test tests/InvectorModKit.Charting.Tests`
Expected: FAIL (`lane-change` / `turn-lane` raised, no `lane-time`).

- [ ] **Step 3: Implement**

In the pickup loop of `Issues`, for a non-Freefly pickup after one in the same mode: keep the `turn-time` check when the previous pickup is a turn; the ship's lane is `LaneAfterTurn` after a turn, the previous lane otherwise; when the new pickup is elsewhere and `turn-time` did not fire, require `crossed * MinLaneChange(mode, d) - 1e-3` seconds, `crossed` = 1 in the Tunnel (it wraps), the lane distance on the Road; text `$" {mode}: only {gap:0.00}s to change lane"`. Rewrite the class summary's "key facts": the game places each pickup at its own lane; a turn asks the player to steer one lane over (List wraps, Bounce does not); other lane changes are steered without a note in the official Hard and Ultra charts, never faster than the limits above. Update the `Code` list in `PlayabilityIssue`.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/InvectorModKit.Charting.Tests` and `dotnet test tests/InvectorModKit.ChartEditor.Tests`
Expected: PASS, 0 failed.

- [ ] **Step 5: Commit**

```bash
git add src/InvectorModKit.Charting/Playability.cs tests/InvectorModKit.Charting.Tests/PlayabilityIssuesTests.cs
git commit -m "feat(charting): allow lane changes without a turn, warn when too fast"
```

### Task 3: Editor interface

**Files:**
- Modify: `tools/ChartEditor/ui/app.js` (selection panel lane buttons, `regenerate`)
- Modify: `tools/ChartEditor/ui/i18n.js`
- Test: `tools/ChartEditor/ui/tests/i18n.test.js`

**Interfaces:**
- Consumes: `replaceRange` returning ids and `followTurns` (Task 1); issue code `lane-time` (Task 2).

- [ ] **Step 1: Write the failing test**

In `playability issues are translated, with their numbers`, replace the `lane-change` line by
`assert.equal(issueText({ code: "lane-time", gap: 0.3, mode: "Bounce" }), "Route : trop peu de temps pour changer de voie (0.30 s)");`

- [ ] **Step 2: Run it to see it fail**

Run: `node --test tests/i18n.test.js`
Expected: FAIL (no `issue_lane-time` text).

- [ ] **Step 3: Implement**

- `i18n.js`: add `"issue_lane-time"` after `"issue_turn-time"`, same shape (`{2}` zone, `{0}` seconds, each language's unit as in `issue_turn-time`): en "{2}: too little time to change lane ({0} s)", fr "{2} : trop peu de temps pour changer de voie ({0} s)", then de, it, es, pt-BR, pl, ru, ja, ko, zh using the words each language already uses for lane in `issue_turn-lane` / `issue_lane-change`. Delete `issue_turn-lane`, `issue_lane-change` and `laneFollows`.
- `app.js`: the lane buttons show for any single selected pickup (`if (!n.strum)`), no `laneIsFree`, no `laneFollows` line. `regenerate`: `this.edit((doc) => m.followTurns(doc, d, m.replaceRange(doc, d, generated.notes[d], r.from, r.to)))` with `d = this.difficulty`.

- [ ] **Step 4: Run all interface tests**

Run: `node --test` (from `tools/ChartEditor/ui`)
Expected: PASS, including `every key the interface uses is defined`.

- [ ] **Step 5: Commit**

```bash
git add tools/ChartEditor/ui/app.js tools/ChartEditor/ui/i18n.js tools/ChartEditor/ui/tests/i18n.test.js
git commit -m "feat(editor): lane buttons for every note and the lane timing alert"
```

### Task 4: Verification and pull request

- [ ] **Step 1: Official charts raise no lane alert**

With the throwaway tool in this session's scratchpad (`dump/`), load each official `ModesInTimeContainer` dump as a `Chart` (as `ChartLab/OfficialChart.cs` does) and count `Playability.Issues` codes per difficulty. Expected: no `lane-time`, no `lane-change`, no `turn-lane`. Nothing from the game is committed.

- [ ] **Step 2: Everything green and building**

Run: `node --test` (in `tools/ChartEditor/ui`), `dotnet test tests/InvectorModKit.Charting.Tests`, `dotnet test tests/InvectorModKit.ChartEditor.Tests`, `dotnet build tools/ChartEditor/ChartEditor.csproj -c Release`.
Expected: all pass, build succeeds.

- [ ] **Step 3: Theo checks the editor**

Ask Theo before launching. Start `tools/ChartEditor/bin/Release/net48/InvectorLevelEditor.exe` from the worktree (the installed editor stays untouched). Check with him: a note clicked on any lane stays there, Up/Down and drag move only the selected notes, lane buttons on every pickup, adding or flipping a turn moves nothing, the 3D preview slides the ship to a note without a turn, the alert list shows the new text when two notes are too close on different lanes. Send screenshots.

- [ ] **Step 4: Pull request**

Push `feat/editor-free-note-lanes` and open a ready (not draft) PR against `main`: what changes for chart authors, the official chart facts, the limits, how it was tested.

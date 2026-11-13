import test from "node:test";
import assert from "node:assert/strict";
import * as m from "../model.js";

const file = () => ({
  format: 1, bpm: 120, firstBeat: 0.5, start: 0.2, end: 60, preview: 20, gain: 1.5,
  sections: [{ t: 0, mode: "List" }, { t: 20, mode: "Bounce" }, { t: 40, mode: "Freefly" }],
  notes: {
    Casual: [{ t: 2 }],
    Normal: [],
    Hard: [{ t: 2, lane: 0 }, { t: 3, color: 4 }, { t: 4 }, { t: 5, strum: true, len: 0.8 }, { t: 41, lane: 2, color: 2 }],
    Ultra: [{ t: 2, color: 1, set: 1 }, { t: 3, set: 1, last: true }],
  },
});

test("file round trip keeps what ChartJson writes", () => {
  const doc = m.fromFile(file());
  assert.deepEqual(m.toFile(doc), {
    ...file(),
    notes: {
      Casual: [{ t: 2 }],
      Normal: [],
      Hard: [{ t: 2, lane: 0 }, { t: 3, color: 4 }, { t: 4 }, { t: 5, strum: true, len: 0.8 }, { t: 41, lane: 2 }],
      Ultra: [{ t: 2, color: 1, set: 1 }, { t: 3, set: 1, last: true }],
    },
  });
});

const lanesOf = (doc, d) => doc.notes[d].map((n) => n.lane);
const idAt = (doc, d, t) => doc.notes[d].find((n) => n.t === t).id;

test("every pickup keeps its own lane, turns move nothing", () => {
  // Lanes that do not follow the turns, as the official Hard and Ultra charts have them.
  const ed = new m.Editor(m.fromFile({ ...file(), notes: { Hard: [{ t: 18, lane: 2 }, { t: 19, color: 5 }, { t: 21, lane: 2 }, { t: 22, color: 5 }, { t: 23 }, { t: 24, color: 4 }, { t: 25 }, { t: 41, lane: 0 }, { t: 44, lane: 2 }] } }));
  const before = [2, 1, 2, 1, 1, 1, 1, 0, 2];
  assert.deepEqual(lanesOf(ed.doc, "Hard"), before);
  const kept = () => ed.doc.notes.Hard.filter((n) => n.t !== 18.5 && n.t !== 24).map((n) => n.lane);
  ed.apply((doc) => m.addNote(doc, "Hard", { t: 18.5, color: 5, lane: 2 }));
  assert.deepEqual(kept(), [2, 1, 2, 1, 1, 1, 0, 2]);
  ed.apply((doc) => m.setColor(doc, "Hard", [idAt(doc, "Hard", 19)], 4));
  assert.deepEqual(kept(), [2, 1, 2, 1, 1, 1, 0, 2]);
  ed.apply((doc) => m.removeNotes(doc, "Hard", [idAt(doc, "Hard", 24)]));
  assert.deepEqual(kept(), [2, 1, 2, 1, 1, 1, 0, 2]);
  ed.apply((doc) => m.setSectionMode(doc, 1, "List")); // the Road becomes a Tunnel: lanes stay
  assert.deepEqual(kept(), [2, 1, 2, 1, 1, 1, 0, 2]);
});

test("lane edits change only the notes they touch", () => {
  const ed = new m.Editor(m.fromFile({ ...file(), notes: { Hard: [{ t: 2 }, { t: 3, color: 5 }, { t: 4 }, { t: 5 }] } }));
  ed.apply((doc) => m.setLane(doc, "Hard", [idAt(doc, "Hard", 4)], 0));
  assert.deepEqual(lanesOf(ed.doc, "Hard"), [1, 1, 0, 1]);
  const ids = [idAt(ed.doc, "Hard", 3), idAt(ed.doc, "Hard", 5)];
  ed.apply((doc) => m.moveNotes(doc, "Hard", ids, 0, 1));
  assert.deepEqual(lanesOf(ed.doc, "Hard"), [1, 2, 0, 2]);
  ed.apply((doc) => m.moveNotes(doc, "Hard", ids, 0, 1)); // already on the edge lane
  assert.deepEqual(lanesOf(ed.doc, "Hard"), [1, 2, 0, 2]);
  ed.undo(); ed.undo();
  assert.deepEqual(lanesOf(ed.doc, "Hard"), [1, 1, 0, 1]);
  // a live drag in the timeline: lanes set directly, normalized on every move, then committed
  const snapshot = ed.snapshot();
  ed.doc.notes.Hard[0].lane = 0;
  m.normalize(ed.doc);
  assert.equal(ed.commit(snapshot), true);
  assert.deepEqual(lanesOf(ed.doc, "Hard"), [0, 1, 0, 1]);
});

test("regenerated notes follow their turns from the note before them", () => {
  for (const [from, to, expected] of [[0, 20, [2, 2, 2, 0, 1]], [20, 40, [2, 2, 2, 2, 1]]]) {
    // Tunnel (wraps around) then Road (stops at the edge)
    const doc = m.fromFile({ ...file(), notes: { Hard: [{ t: from + 1, lane: 2 }, { t: from + 12 }] } });
    const ids = m.replaceRange(doc, "Hard", [{ t: from + 3 }, { t: from + 4, color: 5 }, { t: from + 5 }], from + 2, from + 10);
    assert.equal(ids.length, 3);
    m.followTurns(doc, "Hard", ids);
    m.normalize(doc);
    assert.deepEqual(lanesOf(doc, "Hard"), expected, `zone from ${from} to ${to}`);
  }
});

test("grid: snap, lines and bars", () => {
  const doc = m.fromFile(file()); // 120 BPM: 0.5 s per beat, grid starts at 0.5
  assert.equal(m.snap(doc, 1.26, 1), 1.5);
  assert.equal(m.snap(doc, 1.26, 4), 1.25);
  assert.ok(Math.abs(m.snap(doc, 0.68, 3) - (0.5 + 0.5 / 3)) < 1e-9);
  const lines = m.gridLines(doc, 0.5, 2.6, 2);
  assert.deepEqual(lines.map((l) => [l.t, l.level]), [[0.5, 0], [0.75, 2], [1, 1], [1.25, 2], [1.5, 1], [1.75, 2], [2, 1], [2.25, 2], [2.5, 0]]);
  assert.deepEqual(lines.filter((l) => l.level === 0).map((l) => l.bar), [1, 2]);
});

test("tap tempo uses the median interval", () => {
  assert.equal(m.tapTempo([0, 0.5, 1.0, 1.5, 2.3, 2.5]), 120);
  assert.equal(m.tapTempo([0, 1]), null);
});

test("add, move, hold, color, strum, delete, with undo and redo", () => {
  const ed = new m.Editor(m.fromFile(file()));
  const id = ed.apply((doc) => m.addNote(doc, "Normal", { t: 10, color: 3 }));
  assert.equal(ed.doc.notes.Normal.length, 1);
  ed.apply((doc) => m.moveNotes(doc, "Normal", [id], 0.5));
  assert.equal(ed.doc.notes.Normal[0].t, 10.5);
  ed.apply((doc) => m.setHold(doc, "Normal", id, 1)); // pickup: no hold
  assert.equal(ed.doc.notes.Normal[0].len, 0);
  ed.apply((doc) => m.setStrum(doc, "Normal", [id], true));
  ed.apply((doc) => m.setHold(doc, "Normal", id, 1.2));
  assert.deepEqual(m.toFile(ed.doc).notes.Normal, [{ t: 10.5, strum: true, len: 1.2 }]);
  ed.apply((doc) => m.removeNotes(doc, "Normal", [id]));
  assert.equal(ed.doc.notes.Normal.length, 0);
  ed.undo(); ed.undo();
  assert.deepEqual(m.toFile(ed.doc).notes.Normal, [{ t: 10.5, strum: true }]);
  ed.redo();
  assert.equal(ed.doc.notes.Normal[0].len, 1.2);
  assert.equal(ed.dirty, true);
});

test("notes stay inside the song and sorted", () => {
  const doc = m.fromFile(file());
  const ids = doc.notes.Hard.map((n) => n.id);
  m.moveNotes(doc, "Hard", [ids[0]], 100);
  m.normalize(doc);
  assert.equal(doc.notes.Hard[doc.notes.Hard.length - 1].t, 60);
  m.moveNotes(doc, "Hard", ids, -100);
  m.normalize(doc);
  assert.ok(doc.notes.Hard.every((n) => n.t >= 0));
});

test("star sets: numbered, last note marked, cleared", () => {
  const doc = m.fromFile(file());
  const ids = doc.notes.Hard.slice(0, 3).map((n) => n.id);
  assert.equal(m.makeStarSet(doc, "Hard", ids), 1);
  assert.deepEqual(doc.notes.Hard.slice(0, 3).map((n) => [n.set, n.last]), [[1, false], [1, false], [1, true]]);
  assert.equal(m.makeStarSet(doc, "Hard", [doc.notes.Hard[3].id]), 2);
  m.clearStarSet(doc, "Hard", ids);
  assert.ok(doc.notes.Hard.slice(0, 3).every((n) => n.set === 0 && !n.last));
});

test("copy a range to another difficulty, replace a range, copy/paste", () => {
  const doc = m.fromFile(file());
  m.copyRange(doc, "Hard", "Casual", 0, 20);
  m.normalize(doc);
  assert.deepEqual(m.toFile(doc).notes.Casual.map((n) => n.t), [2, 3, 4, 5]);
  m.replaceRange(doc, "Casual", [{ t: 1 }, { t: 3.5, color: 0 }, { t: 30 }], 3, 20);
  m.normalize(doc);
  assert.deepEqual(m.toFile(doc).notes.Casual.map((n) => n.t), [2, 3.5]);
  const clip = m.copyNotes(doc, "Hard", doc.notes.Hard.slice(0, 2).map((n) => n.id));
  m.paste(doc, "Normal", clip, 30);
  m.normalize(doc);
  assert.deepEqual(m.toFile(doc).notes.Normal.map((n) => n.t), [30, 31]);
  assert.deepEqual(lanesOf(doc, "Normal"), [0, 1]); // pasted with their own lanes
});

test("zones: cut, move between neighbours, change mode, remove (never the first)", () => {
  const doc = m.fromFile(file());
  m.addSection(doc, 10, "Bounce");
  m.normalize(doc);
  assert.deepEqual(doc.sections.map((s) => [s.t, s.mode]), [[0, "List"], [10, "Bounce"], [20, "Bounce"], [40, "Freefly"]]);
  m.moveSection(doc, 1, 35);
  assert.equal(doc.sections[1].t, 19.9);
  m.setSectionMode(doc, 1, "Freefly");
  m.removeSection(doc, 0);
  m.removeSection(doc, 2);
  assert.deepEqual(doc.sections.map((s) => [s.t, s.mode]), [[0, "List"], [19.9, "Freefly"], [40, "Freefly"]]);
  assert.deepEqual(m.sectionRange(doc, 2), { from: 40, to: 60 });
  assert.equal(m.modeAt(doc, 25), "Freefly");
});

test("meta values are kept in range", () => {
  const doc = m.fromFile(file());
  m.setMeta(doc, { bpm: 1000, preview: -3, start: 2, firstBeat: 0.51 });
  assert.deepEqual([doc.bpm, doc.preview, doc.start, doc.firstBeat], [400, 0, 2, 0.51]);
});

test("an empty chart opens and saves", () => {
  const doc = m.fromFile({ bpm: 100, end: 30, notes: {} });
  assert.deepEqual(m.toFile(doc).sections, [{ t: 0, mode: "List" }]);
  assert.deepEqual(m.toFile(doc).notes, { Casual: [], Normal: [], Hard: [], Ultra: [] });
});

import test from "node:test";
import assert from "node:assert/strict";
import { fromFile } from "../model.js";
import { prepareRun } from "../preview/run.js";
import { RIG_CONSTANTS, TIERS } from "../preview/rig.js";

const rig = { ...RIG_CONSTANTS, tiers: TIERS };
const near = (a, b, eps = 1e-6, what = "") => assert.ok(Math.abs(a - b) <= eps, `${what} ${a} is not ${b}`);

/** A chart at 120 bpm (a beat every 0.5 s), first beat 0, with the given Hard notes and sections. */
function chart(notes, sections = [{ t: 0, mode: "List" }], end = 60) {
  return fromFile({ format: 1, bpm: 120, firstBeat: 0, start: 0, end, preview: 0, gain: 1, sections, notes: { Casual: [], Normal: [], Hard: notes, Ultra: [] } });
}
const arrows = (...ts) => ts.map((t) => ({ t, lane: 1 }));

test("two perfect notes fill the pump", () => {
  const run = prepareRun(chart(arrows(1, 1.5, 2, 2.5, 3, 4)), "Hard", rig);
  assert.deepEqual(run.notes.filter((n) => n.levelUp).map((n) => n.t), [4]);
  assert.deepEqual(run.levels, [{ t: 0, level: 0 }, { t: 4, level: 1 }]);
  assert.equal(run.levelAt(3.9), 0);
  assert.equal(run.levelAt(4), 1);
});

test("a plain strum is not the level-up pickup", () => {
  const notes = [...arrows(1, 1.5, 2, 2.5, 3), { t: 4, strum: true }, { t: 4.5, lane: 1 }];
  const run = prepareRun(chart(notes), "Hard", rig);
  assert.deepEqual(run.notes.filter((n) => n.levelUp).map((n) => n.t), [4.5]);
});

test("speed and distance", () => {
  const run = prepareRun(chart(arrows(1, 1.5, 2, 2.5, 3, 4)), "Hard", rig);
  near(run.speedAt(3.9), 30);
  near(run.speedAt(4.5), 32.5);
  near(run.speedAt(5), 35);
  near(run.distAt(4), 120);
  near(run.distAt(5), 152.5);
  near(run.distAt(-1), -30);
});

test("fov", () => {
  const run = prepareRun(chart(arrows(1, 1.5, 2, 2.5, 3, 4)), "Hard", rig);
  near(run.fovAt(0), 60);
  near(run.fovAt(5), 70);
});

test("the top level", () => {
  const run = prepareRun(chart(arrows(...Array.from({ length: 60 }, (_, i) => 1 + i * 0.5))), "Hard", rig);
  assert.deepEqual(run.levels.map((l) => l.level), [0, 1, 2, 3]);
  const marks = run.notes.filter((n) => n.levelUp);
  assert.ok(marks.length >= 4, String(marks.length));
  assert.ok(marks[marks.length - 1].t > run.levels[3].t);
  assert.equal(run.levelAt(1000), 3);
});

test("notes slide with the speed", () => {
  const run = prepareRun(chart(arrows(1, 1.5, 2, 2.5, 3, 4)), "Hard", rig);
  near(run.noteDist(6, 4.5), run.distAt(4.5) + 1.5 * 32.5 + 1);
});

test("a fixed tier", () => {
  const run = prepareRun(chart(arrows(1, 1.5, 2, 2.5, 3, 4)), "Hard", rig, { tier: 3 });
  for (const t of [-1, 0, 4.5, 30]) { near(run.speedAt(t), 49); near(run.fovAt(t), 90); assert.equal(run.levelAt(t), 3); }
  assert.equal(run.notes.filter((n) => n.levelUp).length, 0);
});

test("visible notes", () => {
  const notes = [...arrows(1, 4, 6), { t: 2, strum: true, len: 3 }];
  const run = prepareRun(chart(notes, [{ t: 0, mode: "List" }, { t: 5.5, mode: "Freefly" }]), "Hard", rig);
  assert.deepEqual(run.visible(3).map((n) => n.t), [2, 4, 6]); // the hold until 2 + 3 + 1; the ring 3 s ahead
  assert.deepEqual(run.visible(0.5).map((n) => n.t), [1, 2]); // the ring at 6 is 5.5 s ahead
  assert.deepEqual(run.visible(1.2).map((n) => n.t), [2, 4, 6]); // passed 0.15 s ago is gone; Freefly rings show 5 s ahead
});

test("odd charts", () => {
  const cases = [
    chart([]),
    chart([{ t: -1, lane: 1 }, { t: 70, lane: 1 }]),
    chart(arrows(1, 2, 3), [{ t: 0, mode: "Freefly" }]),
  ];
  for (const doc of cases) {
    const run = prepareRun(doc, "Hard", rig);
    for (const t of [-5, 0, doc.end + 5]) {
      assert.ok(Number.isFinite(run.distAt(t)) && Number.isFinite(run.speedAt(t)) && Number.isFinite(run.fovAt(t)));
      assert.ok(Array.isArray(run.visible(t)));
      assert.ok(typeof run.modeAt(t) === "string");
    }
  }
});

test("prepares a dense chart under 50 ms", () => {
  const notes = Array.from({ length: 1500 }, (_, i) => ({ t: 0.5 + i * 0.16, lane: i % 3, color: i % 6, strum: i % 17 === 0, len: i % 34 === 0 ? 0.5 : 0 }));
  const sections = Array.from({ length: 12 }, (_, i) => ({ t: i * 20, mode: ["List", "Bounce", "Freefly"][i % 3] }));
  const doc = chart(notes, sections, 240);
  const times = [];
  for (let k = 0; k < 5; k++) { const t0 = performance.now(); prepareRun(doc, "Hard", rig); times.push(performance.now() - t0); }
  const best = Math.min(...times); // other test files run alongside and share the processor
  assert.ok(best < 50, `${best} ms`);
});

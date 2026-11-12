import test from "node:test";
import assert from "node:assert/strict";
import { fromFile } from "../model.js";
import { prepareRun } from "../preview/run.js";
import { prepareShip, rollAngles } from "../preview/ship.js";
import { createPath } from "../preview/path.js";
import { RIG_CONSTANTS, TIERS } from "../preview/rig.js";
import { cache, HAS_CACHE } from "./gamecache-fixture.js";

const rig = { ...RIG_CONSTANTS, tiers: TIERS };
const near = (a, b, eps = 1e-6, what = "") => assert.ok(Math.abs(a - b) <= eps, `${what} ${a} is not ${b}`);

/** A straight level 4000 long with a fly area every 500 units. */
const level = {
  start: 0, length: 4000,
  pos: [[0, 0, 0], [0, 0, 1333.3], [0, 0, 2666.7], [0, 0, 4000]], rot: [[0, 0, 0, 1]],
  acc: Array.from({ length: 20 }, (_, i) => (i + 1) * 200),
  fly: Array.from({ length: 8 }, (_, i) => ({ z: i * 500, circleY: 1, circleRadius: 20, groundY: -20, pickup: { x: (i % 3) * 4 - 4, y: (i % 2) * 3 } })),
};
/** Roll clips that turn the ship once, twice and three times about z in 1, 1.5 and 2 s, ending 5.4 degrees short as the game's one-turn clip does. */
const roll = (turns, length) => {
  const frames = Math.round(length * 60) + 1, values = [], half = turns * Math.PI - (5.4 * Math.PI) / 360;
  for (let f = 0; f < frames; f++) { const a = (-half * f) / (frames - 1); values.push(0, 0, Math.sin(a), Math.cos(a)); }
  return { length, fps: 60, tracks: [{ path: "", type: 4, attribute: "rotation", size: 4, values }] };
};
const rolls = { 1: roll(1, 1), 2: roll(2, 1.5), 3: roll(3, 2) };

function chart(notes, sections) {
  return fromFile({ format: 1, bpm: 120, firstBeat: 0, start: 0, end: 60, preview: 0, gain: 1, sections, notes: { Casual: [], Normal: [], Hard: notes, Ultra: [] } });
}
const setup = (notes, sections) => {
  const run = prepareRun(chart(notes, sections), "Hard", rig);
  return { run, ship: prepareShip(run, rig, level, rolls) };
};
const flyChart = () => setup(
  Array.from({ length: 6 }, (_, i) => ({ t: 11 + i * 1.5, lane: 1 })),
  [{ t: 0, mode: "List" }, { t: 10, mode: "Freefly" }, { t: 22, mode: "Bounce" }],
);

test("roll clips", { skip: !HAS_CACHE }, () => {
  for (const [file, length, turns] of [["RollRightLong", 1.567, 1], ["coaster_RollRightLong2Turns", 1.933, 2], ["coaster_RollRightLong3Turns", 2.6, 3]]) {
    const r = rollAngles(cache.json(`clips/${file}.json`));
    near(r.length, length, 0.001, file);
    // the one-turn clip overshoots to 384 degrees and settles at 354.6 by its end; the others end on their turns
    near(Math.abs(r.angles[r.angles.length - 1]), turns * 360, 6, file);
  }
});

test("the same time gives the same state", () => {
  const times = Array.from({ length: 50 }, (_, i) => 9 + i * 0.3);
  const inOrder = (() => { const { ship } = flyChart(); return times.map((t) => ship.state(t)); })();
  const reversed = (() => { const { ship } = flyChart(); return [...times].reverse().map((t) => ship.state(t)).reverse(); })();
  const shuffled = (() => { const { ship } = flyChart(); const order = times.map((t, i) => i).sort((a, b) => ((a * 7919) % 50) - ((b * 7919) % 50)); const out = []; for (const i of order) out[i] = ship.state(times[i]); return out; })();
  assert.deepEqual(reversed, inOrder);
  assert.deepEqual(shuffled, inOrder);
});

test("a ring is taken within 1/30 s of its time", () => {
  const { run, ship } = flyChart();
  const si = run.sectionIndex(12), rings = run.notes.filter((n) => n.mode === "Freefly" && !n.strum);
  const picks = ship.flight(si).picks;
  assert.equal(picks.length, rings.length);
  picks.forEach((p, i) => assert.ok(p.t >= rings[i].t - 1 / 30 - 1e-9 && p.t <= rings[i].t - 1 / 30 + 1 / 60 + 1e-9, `${p.t} for ${rings[i].t}`));
});

test("Bounce: a turn moves the ship over the steering transition", () => {
  const { ship } = setup([{ t: 1, lane: 1 }, { t: 2, lane: 1, color: 5 }, { t: 3, lane: 2 }, { t: 4, lane: 2 }], [{ t: 0, mode: "Bounce" }]);
  near(ship.bounceX(1.9), 0);
  near(ship.bounceX(2), 0);
  const mid = ship.bounceX(2.15);
  assert.ok(mid > 0 && mid < rig.laneX, String(mid));
  near(ship.bounceX(2.4), rig.laneX);
  near(ship.bounceX(3.5), rig.laneX);
});

test("a roll eases back to level after its clip", () => {
  const { run, ship } = setup([{ t: 12, lane: 1 }], [{ t: 0, mode: "List" }, { t: 10, mode: "Freefly" }, { t: 22, mode: "Bounce" }]);
  const si = run.sectionIndex(12), p = ship.flight(si).picks[0];
  const end = p.t + rolls[p.turns].length / 0.65;
  near(ship.barrelAt(si, end + 0.2), 0, 1e-9, "after");
  near(Math.abs(ship.barrelAt(si, end + 0.05)), 2.7, 0.3, "half way");
});

test("odd charts", () => {
  const cases = [
    setup([{ t: 1, lane: 0 }, { t: 2, lane: 2 }], [{ t: 0, mode: "Freefly" }]),
    setup([{ t: 1, lane: 0 }, { t: 12, lane: 2 }], [{ t: 0, mode: "Freefly" }, { t: 10, mode: "Freefly" }]),
    setup([], [{ t: 0, mode: "List" }, { t: 5, mode: "Freefly" }]),
  ];
  const path = createPath(level);
  for (const { ship } of cases) {
    for (const t of [-3, 0, 4.9, 5.2, 11, 70]) {
      const s = ship.state(t), w = ship.world(t, path);
      for (const v of [...s.sl, ...s.rot, s.barrel, ...w.pos, ...w.q]) assert.ok(Number.isFinite(v), `${t}: ${JSON.stringify(s)}`);
    }
  }
});

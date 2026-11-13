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

/** An angle's difference from another, wrapped to (-pi, pi]. */
const turnDiff = (a, b) => { let d = (a - b) % (2 * Math.PI); if (d > Math.PI) d -= 2 * Math.PI; if (d <= -Math.PI) d += 2 * Math.PI; return d; };
const THIRD = (2 * Math.PI) / 3;

test("List: the tunnel turns to the lane, smoothly, from the turn's perfect window", () => {
  // 120 bpm: Walker lerps the angle by 12 x 0.75 / 0.5 / 60 = 0.3 of the way each 60th of a second
  const { ship } = setup([{ t: 1, lane: 1 }, { t: 2, lane: 1, color: 4 }, { t: 3, lane: 0 }], [{ t: 0, mode: "List" }]);
  near(turnDiff(ship.spin(0.5), -THIRD), 0, 1e-6, "on the first note's lane"); // turned there before the song
  const t0 = 2 - 1 / 30; // the autopilot steers as soon as the turn is within 1/30 s
  near(turnDiff(ship.spin(t0), -THIRD), 0, 1e-6, "not yet turning");
  near(turnDiff(ship.spin(t0 + 0.05), -THIRD * 0.7 ** 3), 0, 1e-6, "three frames into the turn");
  near(turnDiff(ship.spin(3), 0), 0, 1e-4, "on lane 0 after the turn");
});

test("List: the angle never jumps inside the tunnel", () => {
  const { ship } = setup([{ t: 1, lane: 1 }, { t: 2, lane: 1, color: 5 }, { t: 2.2, lane: 2, color: 5 }, { t: 3, lane: 0 }], [{ t: 0, mode: "List" }]);
  let prev = ship.spin(0.5), worst = 0;
  for (let t = 0.501; t < 4; t += 0.001) { const a = ship.spin(t); worst = Math.max(worst, Math.abs(turnDiff(a, prev))); prev = a; }
  assert.ok(worst < 0.05, `a step of ${worst} rad`);
  near(turnDiff(ship.spin(3.5), 0), 0, 1e-4, "two right turns from lane 1 end on lane 0");
});

test("List: the tunnel turns to a note on another lane without a turn, once the note before it is taken", () => {
  const { ship } = setup([{ t: 1, lane: 1 }, { t: 2, lane: 1 }, { t: 3, lane: 0 }], [{ t: 0, mode: "List" }]);
  const t0 = 2 - 1 / 30 + 1 / 60; // the frame after the autopilot takes the note at 2 s, its next target is the note at 3 s
  near(turnDiff(ship.spin(t0), -THIRD), 0, 1e-6, "not yet turning");
  near(turnDiff(ship.spin(t0 + 0.05), -THIRD * 0.7 ** 3), 0, 1e-6, "three frames into the turn");
  near(turnDiff(ship.spin(2.9), 0), 0, 1e-4, "on lane 0 before the note");
});

test("List: after a turn, the tunnel goes on to the next note's lane", () => {
  // turn left from lane 1 (to lane 0), then a note back on lane 1: the autopilot steers back the next frame
  const { ship } = setup([{ t: 1, lane: 1 }, { t: 2, lane: 1, color: 4 }, { t: 3, lane: 1 }], [{ t: 0, mode: "List" }]);
  near(turnDiff(ship.spin(2 - 1 / 60), -THIRD * 0.7), 0, 1e-6, "one frame towards lane 0");
  let prev = ship.spin(1.5), worst = 0;
  for (let t = 1.501; t < 3.5; t += 0.001) { const a = ship.spin(t); worst = Math.max(worst, Math.abs(turnDiff(a, prev))); prev = a; }
  assert.ok(worst < 0.05, `a step of ${worst} rad`);
  near(turnDiff(ship.spin(3), -THIRD), 0, 1e-4, "back on lane 1 for the note");
});

test("List after Bounce: the tunnel is set to the first note's lane at once, before the tunnel comes", () => {
  const { ship } = setup([{ t: 5, lane: 1 }, { t: 11, lane: 2 }], [{ t: 0, mode: "Bounce" }, { t: 10, mode: "List" }]);
  near(turnDiff(ship.spin(6), 0), 0, 1e-9, "still the song's start angle");
  near(turnDiff(ship.spin(7.01), -2 * THIRD), 0, 1e-9, "set while the tunnel is 3 s away");
  near(turnDiff(ship.spin(10.5), -2 * THIRD), 0, 1e-9, "in the tunnel");
});

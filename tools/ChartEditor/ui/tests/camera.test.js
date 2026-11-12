import test from "node:test";
import assert from "node:assert/strict";
import { fromFile } from "../model.js";
import { prepareRun } from "../preview/run.js";
import { prepareShip } from "../preview/ship.js";
import { createPath } from "../preview/path.js";
import { cameraPose, cameraLocal, rigPos } from "../preview/camera.js";
import { RIG_CONSTANTS, TIERS } from "../preview/rig.js";

const rig = { ...RIG_CONSTANTS, tiers: TIERS };
const near = (a, b, eps = 1e-4, what = "") => assert.ok(Math.abs(a - b) <= eps, `${what} ${a} is not ${b}`);
const nearV = (a, b, eps, what) => b.forEach((v, i) => near(a[i], v, eps, `${what}[${i}]`));

const straight = { start: 0, length: 4000, pos: [[0, 0, 0], [0, 0, 4000 / 3], [0, 0, 8000 / 3], [0, 0, 4000]], rot: [[0, 0, 0, 1]], acc: Array.from({ length: 20 }, (_, i) => (i + 1) * 200), fly: [] };
const flat = (x) => [0, 0, 0, 1];
function setup(notes, sections, level = straight) {
  const doc = fromFile({ format: 1, bpm: 120, firstBeat: 0, start: 0, end: 60, preview: 0, gain: 1, sections, notes: { Casual: [], Normal: [], Hard: notes, Ultra: [] } });
  const run = prepareRun(doc, "Hard", rig, { tier: 0 }), ship = prepareShip(run, rig, level, {});
  return { run, ship, path: createPath(level) };
}

test("straight path, Bounce, tier 0", () => {
  const { run, ship, path } = setup([{ t: 1, lane: 1 }, { t: 9, lane: 1 }], [{ t: 0, mode: "Bounce" }]);
  const d = path.start + run.distAt(5), pose = cameraPose(run, ship, rig, path, 5);
  const [ox, oy, oz] = rig.camOrigin, [, sy, sz] = rig.shipLocal, f = rig.coast.follow, off = rig.coast.offset;
  nearV(pose.position, [ox + off[0], rig.walkerY + oy + sy * f[1] + off[1], -d - (oz + sz * f[2] + off[2])], 1e-3, "position");
  nearV(pose.target, [0, rig.walkerY, -d - rig.coast.look], 1e-3, "target");
  nearV(pose.up, [0, 1, 0], 1e-6, "up");
  near(pose.fov, 60);
});

test("a mode change takes camTransition", () => {
  const { run, ship } = setup([{ t: 1, lane: 1 }, { t: 20, lane: 1 }], [{ t: 0, mode: "List" }, { t: 10, mode: "Bounce" }]);
  const at = (t) => cameraLocal(run, ship, rig, t);
  const sl = ship.state(10.5).sl;
  const roll = rigPos(rig, run, ship, "Roll", sl, 10.5), coast = rigPos(rig, run, ship, "Coast", sl, 10.5), mid = at(10.5).cam;
  let between = 0;
  for (let i = 0; i < 3; i++) {
    if (Math.abs(roll[i] - coast[i]) < 1e-6) continue;
    assert.ok((mid[i] - roll[i]) * (mid[i] - coast[i]) < 0, `axis ${i}: ${mid[i]} not between ${roll[i]} and ${coast[i]}`);
    between++;
  }
  assert.ok(between > 0);
  for (const t of [11, 12]) nearV(at(t).cam, rigPos(rig, run, ship, "Coast", ship.state(t).sl, t), 1e-9, `at ${t}`);
});

test("bends turn the camera", () => {
  const bank30 = [0, 0, Math.sin(Math.PI / 12), Math.cos(Math.PI / 12)];
  const pos = [[0, 0, 0], [0, 0, 10], [0, 0, 20], [0, 0, 30], [0, 0, 40], [10, 0, 60], [30, 0, 60]];
  const acc = [];
  let total = 0, prev = pos[0];
  for (let c = 0; c < 2; c++) for (let s = 1; s <= 20; s++) {
    const u = s / 20, v = 1 - u, P = [0, 1, 2, 3].map((j) => pos[c * 3 + j]);
    const p = [0, 1, 2].map((j) => v * v * v * P[0][j] + 3 * v * v * u * P[1][j] + 3 * v * u * u * P[2][j] + u * u * u * P[3][j]);
    total += Math.hypot(p[0] - prev[0], p[1] - prev[1], p[2] - prev[2]); acc.push(total); prev = p;
  }
  const level = { start: 0, length: total, pos, rot: [flat(), bank30], acc, fly: [] };
  const { run, ship, path } = setup([{ t: 1, lane: 1 }], [{ t: 0, mode: "List" }], level);
  for (const t of [0.5, 1, 1.5, 2]) nearV(cameraPose(run, ship, rig, path, t).up, path.frameAt(path.start + run.distAt(t)).up, 1e-6, `up at ${t}`);
});

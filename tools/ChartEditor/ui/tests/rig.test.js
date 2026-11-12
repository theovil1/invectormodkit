import test from "node:test";
import assert from "node:assert/strict";
import { readRig, RIG_CONSTANTS, TIERS } from "../preview/rig.js";
import { hermite } from "../preview/curves.js";
import { cache, HAS_CACHE } from "./gamecache-fixture.js";

const near = (a, b, eps = 0.001, what = "") => assert.ok(Math.abs(a - b) <= eps, `${what} ${a} is not ${b}`);
const nearAll = (a, b, what) => (Array.isArray(b) ? b.forEach((v, i) => nearAll(a[i], v, `${what}[${i}]`)) : near(a, b, 0.001, what));

test("falls back to the constants", () => {
  assert.deepEqual(readRig({}, null), { ...RIG_CONSTANTS, tiers: TIERS });
  assert.deepEqual(TIERS.map((t) => [t.speed, t.fov, t.limit]), [[30, 60, 5], [35, 70, 5], [43, 75, 5], [49, 90, 30]]);
});

test("the owner's cache gives the prototype's numbers", { skip: !HAS_CACHE }, () => {
  const rig = readRig(cache.json("settings.json"), cache.json("objects/PlayerWalkerGlobal.json").root);
  const expected = {
    walkerY: -2.5, pickupY: -3, roadY: -3.3333, hitAhead: 1, camOrigin: [0, 2.67, -5.39], shipLocal: [0, 0.12, -2.723],
    camTransition: 1, zComp: 0.05, laneX: 4.4933, pickupLaneX: 4.5, tri: [[0, 7.62], [-6.57, -3.81], [6.57, -3.81]],
  };
  for (const [k, v] of Object.entries(expected)) nearAll(rig[k], v, k);
  nearAll(rig.coast.follow, [1, 0.5, 1], "coast.follow");
  nearAll(rig.coast.offset, [0, 1.5, -3.5], "coast.offset");
  near(rig.coast.look, 10, 0.001, "coast.look");
  nearAll(rig.roll.follow, [0, 0, 1], "roll.follow");
  nearAll(rig.roll.offset, [0, 1.5, -2], "roll.offset");
  near(rig.roll.look, 6, 0.001, "roll.look");
  nearAll(rig.fly.follow, [0.9, 0.98, 0], "fly.follow");
  nearAll(rig.fly.offset, [0, -2.5, -5], "fly.offset");
  nearAll(rig.fly.offsetAtPickup, [0, -1, -15], "fly.offsetAtPickup");
  near(rig.fly.pickupDuration, 4.5, 0.001, "fly.pickupDuration");
  near(rig.fly.look, 50, 0.001, "fly.look");
  near(hermite(rig.transition)(0.2), 0.19843943, 1e-6, "transition(0.2)");
  const flyOffset = [[0, 0, 0.9196367, 0.9196367], [0.19845526, 0.99847794, 0.2943929, 0.2943929], [0.62792987, 0.82336414, -1.2110564, -1.2110564], [1, 0, 0.16261473, 0.16261473]];
  nearAll(rig.flyOffset.map((k) => [k.time, k.value, k.inSlope, k.outSlope]), flyOffset, "flyOffset");
  assert.deepEqual(Object.values(rig.steer).map((v) => Math.round(v * 1000) / 1000), [100, 0.1, 1, 7, 10, 15, 20, 30, 60, 0.333, 0.5]);
});

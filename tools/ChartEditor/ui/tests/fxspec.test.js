import test from "node:test";
import assert from "node:assert/strict";
import { trailSets, haloLevels } from "../preview/fxspec.js";
import { cache, HAS_CACHE } from "./gamecache-fixture.js";

const near = (a, b, eps, what = "") => assert.ok(Math.abs(a - b) <= eps, `${what} ${a} is not ${b}`);
const nearV = (a, b, eps, what) => b.forEach((v, i) => near(a[i], v, eps, `${what}[${i}]`));
const byName = (set, re) => set.filter((t) => re.test(t.name) || re.test(t.group || ""));
const flat = (t) => Math.abs(t.normal[1]) > 0.99;
const upright = (t) => Math.abs(t.normal[0]) > 0.99;

test("the Beginner's trails", { skip: !HAS_CACHE }, () => {
  const sets = trailSets(cache.json("objects/Ship_Beginner.json").root);
  assert.equal(sets.streak.length, 4);
  assert.equal(sets.streak[0].length, 0); // tier 1 only has engine particles

  const big = byName(sets.streak[3], /TrailBig/);
  assert.equal(big.length, 2);
  for (const t of big) {
    nearV(t.p, [Math.sign(t.p[0]) * 0.512, -0.039, -0.263], 0.002, t.name);
    near(t.time, 0.1, 1e-3, "time"); near(t.w0, 0.2, 1e-3, "w0"); near(t.w1, 0.1, 1e-3, "w1");
    assert.ok(flat(t), `${t.name} ${t.normal}`);
  }
  const wide = byName(sets.streak[3], /^Wide (Right|Left)$/);
  assert.equal(wide.length, 2);
  for (const t of wide) {
    nearV(t.p, [Math.sign(t.p[0]) * 0.14, -0.022, -0.233], 0.002, t.name);
    near(t.time, 0.06, 1e-3, "time"); near(t.w0, 0.3, 1e-3, "w0"); near(t.w1, 0.3, 1e-3, "w1");
    assert.ok(upright(t), `${t.name} ${t.normal}`);
  }
  const wind = byName(sets.streak[3], /SmallWindTrail/);
  assert.equal(wind.length, 2);
  for (const t of wind) {
    nearV(t.p, [Math.sign(t.p[0]) * 0.485, -0.193, 0.535], 0.002, t.name);
    near(t.time, 0.25, 1e-3, "time"); near(t.w0, 0.075, 1e-3, "w0");
    assert.ok(flat(t), `${t.name} ${t.normal}`);
  }
  for (const set of [...sets.streak, ...sets.freefly, sets.onSteer])
    for (const t of set) assert.ok(t.align === "view" || Math.abs(t.normal[2]) < 0.9, `${t.name} faces along the ship`);

  assert.deepEqual(sets.freefly.map((s) => s.length), [0, 2, 4, 6]);
  for (const t of byName(sets.freefly[1], /Small/)) {
    nearV(t.p, [Math.sign(t.p[0]) * 0.499, -0.19, 0.498], 0.002, t.name);
    near(t.time, 1, 1e-3, "time"); near(t.w0, 0.1, 1e-3, "w0"); near(t.w1, 0.05, 1e-3, "w1");
    assert.equal(t.align, "view");
  }
  assert.equal(sets.onSteer.length, 2);
  for (const t of sets.onSteer) {
    near(t.time, 0.2, 1e-3, "time"); near(t.w0, 0.15, 1e-3, "w0"); near(t.w1, 0.05, 1e-3, "w1");
    assert.equal(t.align, "view");
    assert.ok(t.mat.tex, "texture");
  }
});

test("the level halo", { skip: !HAS_CACHE }, () => {
  const halo = haloLevels(cache.json("objects/XBOX_Pickup_Left_X_OPTIMIZED.json").root);
  const expected = [
    { w: 2.08, h: 2.22, scale: 2, pos: [0, 0.45, 1.3], grey: 0.32 },
    { w: 3.55, h: 3.64, scale: 2.35, pos: [0, -0.41, 1.81], grey: 0.49 },
    { w: 6.58, h: 6.22, scale: 2.5, pos: [0, -2.4, 4.2], grey: 0.49 },
  ];
  assert.equal(halo.levels.length, 3);
  halo.levels.forEach((l, i) => {
    near(l.w, expected[i].w, 0.01, "w"); near(l.h, expected[i].h, 0.01, "h");
    near(l.scale, expected[i].scale, 0.001, "scale"); nearV(l.pos, expected[i].pos, 0.001, "pos");
    near(l.grey, expected[i].grey, 0.01, "grey");
    assert.ok(l.tex, "texture");
  });
  nearV(halo.tint, [0.231, 0.489, 1], 0.001, "tint");
  near(halo.gain, 0.4, 1e-6, "gain");
});

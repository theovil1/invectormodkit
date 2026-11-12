import test from "node:test";
import assert from "node:assert/strict";
import { createPath } from "../preview/path.js";
import { cache, HAS_CACHE } from "./gamecache-fixture.js";

const near = (a, b, eps, what = "") => assert.ok(Math.abs(a - b) <= eps, `${what} ${a} is not ${b}`);
const nearV = (a, b, eps, what = "") => a.forEach((v, i) => near(v, b[i], eps, `${what}[${i}]`));

/** A level path from Unity Bezier points: acc is the cumulative chord length at 20 steps a curve, as the game stores it. */
function level(pos, rot, start = 0) {
  const curves = (pos.length - 1) / 3, acc = [];
  let total = 0, prev = pos[0];
  for (let c = 0; c < curves; c++) {
    const [P0, P1, P2, P3] = [0, 1, 2, 3].map((j) => pos[c * 3 + j]);
    for (let s = 1; s <= 20; s++) {
      const u = s / 20, v = 1 - u;
      const p = [0, 1, 2].map((j) => v * v * v * P0[j] + 3 * v * v * u * P1[j] + 3 * v * u * u * P2[j] + u * u * u * P3[j]);
      total += Math.hypot(p[0] - prev[0], p[1] - prev[1], p[2] - prev[2]);
      acc.push(total);
      prev = p;
    }
  }
  return { start, length: total, pos, rot, acc };
}

const I = [0, 0, 0, 1];
const bank30 = [0, 0, Math.sin(Math.PI / 12), Math.cos(Math.PI / 12)]; // 30 degrees about z
const bent = () => level([[0, 0, 0], [0, 0, 10], [0, 0, 20], [0, 0, 30], [0, 0, 40], [10, 0, 60], [30, 0, 60]], [I, bank30]);

test("a straight path", () => {
  const path = createPath(level([[0, 0, 0], [0, 0, 10], [0, 0, 20], [0, 0, 30]], [I]));
  const f = path.frameAt(15);
  nearV(f.p, [0, 0, -15], 1e-4, "p");
  nearV(f.up, [0, 1, 0], 1e-6, "up");
  nearV(f.back, [0, 0, 1], 1e-6, "back");
  nearV(path.place([2, 3, 0], 15), [2, 3, -15], 1e-4, "place");
});

test("the table follows the exact curve", () => {
  const path = createPath(bent());
  for (let i = 0; i < 200; i++) {
    const d = (path.length * i) / 200;
    const a = path.frameAt(d), b = path.exactAt(d);
    for (const k of ["p", "right", "up", "back"]) nearV(a[k], b[k], 0.01, `${k} at ${d}`);
  }
});

test("a lap later is the same frame", () => {
  const path = createPath(bent());
  for (const d of [3, 41.5, 70]) {
    for (const k of ["p", "up"]) {
      nearV(path.frameAt(d + path.length)[k], path.frameAt(d)[k], 1e-4, k);
      nearV(path.frameAt(d - path.length)[k], path.frameAt(d)[k], 1e-4, k);
    }
  }
});

test("the void path", { skip: !HAS_CACHE }, () => {
  const path = createPath(cache.json("levels/void.json"));
  assert.equal(path.start, 210);
  near(path.length, 2562.75, 0.01, "length");
  for (const d of [0, 500, 2000]) {
    const a = path.frameAt(d), b = path.exactAt(d);
    for (const k of ["p", "right", "up", "back"]) nearV(a[k], b[k], 0.01, `${k} at ${d}`);
  }
});

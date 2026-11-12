import test from "node:test";
import assert from "node:assert/strict";
import { hermite, minMaxCurve, minMaxGradient } from "../preview/curves.js";
import { seeded, toLinear } from "../preview/math.js";

const key = (time, value, inSlope = 0, outSlope = inSlope) => ({ time, value, inSlope, outSlope });
const near = (a, b, eps = 1e-6) => assert.ok(Math.abs(a - b) <= eps, `${a} is not ${b}`);

test("hermite matches the game's transition curve", () => {
  const c = hermite([key(0, 0), key(0.2, 0.19843943, 2.3194094), key(1, 1)]);
  near(c(0), 0);
  near(c(0.2), 0.19843943);
  near(c(1), 1);
  near(c(2), 1);
  near(c(-1), 0);
  const mid = c(0.6);
  assert.ok(mid > 0.19843943 && mid < 1, String(mid));
});

test("minMaxCurve states", () => {
  near(minMaxCurve({ minMaxState: 0, scalar: 2 })(0.3, 0.7), 2);
  const two = minMaxCurve({ minMaxState: 3, scalar: 0.5, minScalar: 0.35 });
  near(two(0, 0), 0.35);
  near(two(0, 0.5), 0.425);
  const down = { m_Curve: [key(0, 0.5, -1.3), key(1, -0.8, -1.3)] };
  const up = { m_Curve: [key(0, -0.5, 1.3), key(1, 0.8, 1.3)] };
  near(minMaxCurve({ minMaxState: 1, scalar: 25, maxCurve: down })(0, 0.9), 12.5);
  near(minMaxCurve({ minMaxState: 2, scalar: 25, minCurve: up, maxCurve: down })(0, 0.5), 0);
});

test("minMaxGradient", () => {
  const white = { r: 1, g: 1, b: 1, a: 1 }, black = { r: 0, g: 0, b: 0, a: 0 };
  const g = { key0: white, key1: black, ctime0: 0, ctime1: 65535, atime0: 0, atime1: 65535, m_Mode: 0, m_NumColorKeys: 2, m_NumAlphaKeys: 2 };
  const c = minMaxGradient({ minMaxState: 1, maxGradient: g })(0.5, 0);
  for (const v of c) near(v, 0.5, 1e-3);
  assert.deepEqual(minMaxGradient({ minMaxState: 0, maxColor: { r: 1, g: 0.5, b: 0.25, a: 1 } })(0.3, 0.2), [1, 0.5, 0.25, 1]);
});

test("seeded is deterministic", () => {
  const a = seeded(42), b = seeded(42);
  const xs = [a(), a(), a(), a(), a()];
  assert.deepEqual(xs, [b(), b(), b(), b(), b()]);
  for (const x of xs) assert.ok(x >= 0 && x < 1, String(x));
});

test("toLinear", () => {
  near(toLinear(0.5), 0.214, 1e-4);
  near(toLinear(0.04), 0.04 / 12.92);
});

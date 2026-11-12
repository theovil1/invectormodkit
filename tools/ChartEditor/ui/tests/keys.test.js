import test from "node:test";
import assert from "node:assert/strict";
import { arrowAction } from "../keys.js";

const left = { key: "ArrowLeft" }, right = { key: "ArrowRight" };

test("arrows move in the song, even with notes selected", () => {
  assert.deepEqual(arrowAction(left, 0.25, false), { seek: -0.25 });
  assert.deepEqual(arrowAction(right, 0.25, true), { seek: 0.25 });
});

test("Ctrl + arrows move the selected notes", () => {
  assert.deepEqual(arrowAction({ ...left, ctrlKey: true }, 0.25, true), { move: -0.25 });
  assert.deepEqual(arrowAction({ ...right, metaKey: true }, 0.25, true), { move: 0.25 });
  assert.deepEqual(arrowAction({ ...left, ctrlKey: true }, 0.25, false), { seek: -0.25 });
});

test("Alt makes the step one millisecond", () => {
  assert.deepEqual(arrowAction({ ...left, altKey: true }, 0.25, true), { seek: -0.001 });
  assert.deepEqual(arrowAction({ ...right, altKey: true, ctrlKey: true }, 0.25, true), { move: 0.001 });
});

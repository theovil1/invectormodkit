import test from "node:test";
import assert from "node:assert/strict";
import { GRIP_GAP, holdEndAt, handleAt, gripX } from "../holdgrip.js";

// 100 px per second, the song starting at x = 0.
const xOf = (t) => t * 100;

test("a plain strum's handle sits just right of its ring", () => {
  const strum = { id: 1, t: 2, len: 0 };
  assert.equal(gripX(strum, xOf), 200 + GRIP_GAP);
  assert.ok(GRIP_GAP > 24, "clear of the strum's 48 px ring");
  assert.equal(handleAt([strum], xOf, 200 + GRIP_GAP + 3), strum);
  assert.equal(handleAt([strum], xOf, 200), null, "the strum itself is not its handle");
});

test("a hold is pulled from its end, never from a handle", () => {
  const hold = { id: 1, t: 2, len: 0.5 };
  assert.equal(gripX(hold, xOf), 250);
  assert.equal(holdEndAt([hold], xOf, 252), hold);
  assert.equal(handleAt([hold], xOf, 200 + GRIP_GAP), null);
  assert.equal(holdEndAt([{ id: 2, t: 2, len: 0 }], xOf, 200 + GRIP_GAP), null, "a plain strum has no hold end");
});

test("the closest grip wins", () => {
  const a = { id: 1, t: 2, len: 0 }, b = { id: 2, t: 2.08, len: 0 };
  assert.equal(handleAt([a, b], xOf, 200 + GRIP_GAP + 3), a);
  assert.equal(handleAt([a, b], xOf, 208 + GRIP_GAP - 1), b);
  assert.equal(handleAt([a, b], xOf, 400), null, "too far from any grip");
});

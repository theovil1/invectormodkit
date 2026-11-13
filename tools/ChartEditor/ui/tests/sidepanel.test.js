import test from "node:test";
import assert from "node:assert/strict";
import { hiddenBelow, readFolded } from "../sidepanel.js";

const two = [{ id: "a", top: 0 }, { id: "b", top: 197 }];

test("nothing below when the tab fits", () => {
  assert.deepEqual(hiddenBelow({ top: 0, height: 486, total: 486 }, two), { more: false, ids: [] });
});

test("a panel whose title is under the band is named", () => {
  assert.deepEqual(hiddenBelow({ top: 0, height: 267, total: 522 }, two), { more: true, ids: ["b"] });
});

test("more below, but every title is in view", () => {
  assert.deepEqual(hiddenBelow({ top: 0, height: 400, total: 522 }, two), { more: true, ids: [] });
});

test("nothing below at the end of the scroll, or within 2 px of it", () => {
  assert.deepEqual(hiddenBelow({ top: 255, height: 267, total: 522 }, two), { more: false, ids: [] });
  assert.deepEqual(hiddenBelow({ top: 253, height: 267, total: 522 }, two), { more: false, ids: [] });
});

test("panels scrolled past are not named", () => {
  const three = [...two, { id: "c", top: 600 }];
  assert.deepEqual(hiddenBelow({ top: 200, height: 267, total: 900 }, three), { more: true, ids: ["c"] });
});

test("remembered folds: the strings of a JSON array, nothing otherwise", () => {
  for (const text of [null, "{bad", '"x"']) assert.deepEqual(readFolded(text), new Set(), String(text));
  assert.deepEqual(readFolded('["card-selection", 1]'), new Set(["card-selection"]));
});

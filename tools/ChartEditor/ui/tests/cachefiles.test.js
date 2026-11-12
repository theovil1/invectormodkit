import test from "node:test";
import assert from "node:assert/strict";
import { parseMesh, parseTexture, textureKind } from "../preview/cachefiles.js";

/** A cache file as part 1 writes it: compact JSON padded with spaces so the line and its "\n" fill a multiple of 4 bytes. */
function file(header, ...parts) {
  let json = JSON.stringify(header);
  while ((json.length + 1) % 4) json += " ";
  const head = new TextEncoder().encode(json + "\n");
  const bytes = parts.map((p) => new Uint8Array(p.buffer, p.byteOffset, p.byteLength));
  const out = new Uint8Array(head.length + bytes.reduce((n, b) => n + b.length, 0));
  out.set(head, 0);
  let at = head.length;
  for (const b of bytes) { out.set(b, at); at += b.length; }
  return out.buffer;
}

const meshHeader = (index) => ({ vertexCount: 3, attributes: { position: 3, uv0: 2 }, index, submeshes: [[0, 3]], bounds: { center: [0, 0, 0], extent: [1, 1, 1] } });

test("parses a two-attribute mesh", () => {
  const pos = Float32Array.from([1, 2, 3, 4, 5, 6, 7, 8, 9]), uv = Float32Array.from([0.1, 0.2, 0.3, 0.4, 0.5, 0.6]);
  const m = parseMesh(file(meshHeader("u16"), pos, uv, Uint16Array.from([0, 1, 2])));
  assert.equal(m.vertexCount, 3);
  assert.deepEqual([...m.attributes.position], [...pos]);
  assert.deepEqual([...m.attributes.uv0], [...uv]);
  assert.deepEqual(m.sizes, { position: 3, uv0: 2 });
  assert.ok(m.indices instanceof Uint16Array);
  assert.deepEqual([...m.indices], [0, 1, 2]);
  assert.deepEqual(m.submeshes, [[0, 3]]);
  assert.deepEqual(m.bounds.extent, [1, 1, 1]);
});

test("32-bit indices", () => {
  const m = parseMesh(file(meshHeader("u32"), new Float32Array(9), new Float32Array(6), Uint32Array.from([2, 1, 0])));
  assert.ok(m.indices instanceof Uint32Array);
  assert.deepEqual([...m.indices], [2, 1, 0]);
});

test("parses a texture", () => {
  const bytes = Uint8Array.from({ length: 40 }, (_, i) => i);
  const t = parseTexture(file({ format: 10, width: 8, height: 4, mips: 2, colorSpace: 1, filter: 1, wrap: [0, 1] }, bytes));
  assert.equal(t.format, 10);
  assert.equal(t.width, 8);
  assert.equal(t.height, 4);
  assert.equal(t.mips, 2);
  assert.equal(t.colorSpace, 1);
  assert.deepEqual(t.wrap, [0, 1]);
  assert.deepEqual([...t.data], [...bytes]);
  assert.equal(textureKind(10), "s3tc");
});

test("unknown format", () => {
  assert.equal(textureKind(28), null);
  assert.equal(textureKind(99), null);
  assert.equal(textureKind(17), "half");
  assert.equal(textureKind(24), "bptc");
  assert.equal(textureKind(4), "plain");
});

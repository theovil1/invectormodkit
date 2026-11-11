import test from "node:test";
import assert from "node:assert/strict";
import { encodeWav } from "../audio.js";

test("the song is played as 16-bit WAV, channels interleaved and clamped", () => {
  const left = new Float32Array([0, 1, -1, 0.5]), right = new Float32Array([2, -2, 0, -0.5]);
  const wav = encodeWav([left, right], 48000), v = new DataView(wav);
  const text = (at, n) => String.fromCharCode(...new Uint8Array(wav, at, n));
  assert.equal(wav.byteLength, 44 + 4 * 2 * 2);
  assert.equal(text(0, 4), "RIFF");
  assert.equal(v.getUint32(4, true), wav.byteLength - 8);
  assert.equal(text(8, 8), "WAVEfmt ");
  assert.equal(v.getUint16(20, true), 1); // PCM
  assert.equal(v.getUint16(22, true), 2);
  assert.equal(v.getUint32(24, true), 48000);
  assert.equal(v.getUint32(28, true), 48000 * 4);
  assert.equal(v.getUint16(32, true), 4);
  assert.equal(v.getUint16(34, true), 16);
  assert.equal(text(36, 4), "data");
  assert.equal(v.getUint32(40, true), 16);
  assert.deepEqual([...new Int16Array(wav, 44)], [0, 32767, 32767, -32768, -32768, 0, 16383, -16384]);
});

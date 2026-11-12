// The level's path (the game's Spline): cubic Bezier curves walked by arc length (accumulatedLengthsPrecision, 20
// steps a curve), facing along the tangent with the up of its node rotations (an EaseInOutQuad slerp between a
// curve's two node rotations, then LookRotation), so the track climbs, dives and banks where the level does.
// Everything is built on a straight track (x across, y up, back toward the camera) and then placed on the path by
// its distance along it; distances wrap round the level (the game's levels loop).
import { normalize, cross, qslerp, qrotate, fromUnityPoint } from "./math.js";

/** A frame of the path in three's axes: place p, and the straight track's right, up and back there. */
function exact(level, dist) {
  const L = level.length, d = dist - Math.floor(dist / L) * L, A = level.acc;
  let c = 0;
  for (let i = 0; i < A.length; i++) {
    if (A[i] > d) { const a0 = i ? A[i - 1] : 0; c = i / 20 + ((d - a0) / (A[i] - a0)) * 0.05; break; }
  }
  const i = Math.floor(c), u = c - i, n = level.pos.length, k = i * 3;
  const [P0, P1, P2, P3] = [0, 1, 2, 3].map((j) => level.pos[(k + j) % n]), v = 1 - u;
  const p = [0, 1, 2].map((j) => v * v * v * P0[j] + 3 * v * v * u * P1[j] + 3 * v * u * u * P2[j] + u * u * u * P3[j]);
  const fwd = normalize([0, 1, 2].map((j) => 3 * v * v * (P1[j] - P0[j]) + 6 * v * u * (P2[j] - P1[j]) + 3 * u * u * (P3[j] - P2[j])));
  let e = u * 2;
  e = e < 1 ? e * e * 0.5 : -0.5 * ((e - 1) * (e - 3) - 1);
  const R = level.rot, up0 = qrotate(qslerp(R[i % R.length], R[(i + 1) % R.length], e), [0, 1, 0]);
  const right = normalize(cross(up0, fwd)), up = cross(fwd, right);
  const back = fromUnityPoint(fwd).map((x) => -x);
  return { p: fromUnityPoint(p), right: fromUnityPoint(right), up: fromUnityPoint(up), back };
}

/**
 * The path of a level file (start, length, pos, rot, acc), with a table of frames every `step` units for fast
 * lookups: frameAt(d) interpolates it, exactAt(d) computes the curve itself.
 */
export function createPath(level, step = 0.5) {
  const L = level.length, count = Math.ceil(L / step) + 2;
  const table = { p: new Float32Array(count * 3), right: new Float32Array(count * 3), up: new Float32Array(count * 3), back: new Float32Array(count * 3) };
  for (let j = 0; j < count; j++) {
    const f = exact(level, j * step);
    for (const key of ["p", "right", "up", "back"]) table[key].set(f[key], j * 3);
  }

  function frameAt(dist) {
    const d = dist - Math.floor(dist / L) * L, x = d / step, j = Math.min(count - 2, Math.floor(x)), k = x - j;
    const out = {};
    for (const key of ["p", "right", "up", "back"]) {
      const t = table[key], a = j * 3, b = a + 3;
      const v = [t[a] + (t[b] - t[a]) * k, t[a + 1] + (t[b + 1] - t[a + 1]) * k, t[a + 2] + (t[b + 2] - t[a + 2]) * k];
      out[key] = key === "p" ? v : normalize(v);
    }
    return out;
  }

  /** A point given in the straight track's axes at distance d (x across, y up, z back) placed on the path. */
  function place(point, dist) {
    const f = frameAt(dist), [x, y, z] = point;
    return [0, 1, 2].map((i) => f.p[i] + f.right[i] * x + f.up[i] * y + f.back[i] * (z || 0));
  }

  return { start: level.start, length: L, frameAt, place, exactAt: (d) => exact(level, d) };
}

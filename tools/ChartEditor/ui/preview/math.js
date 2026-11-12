// Small vector and quaternion helpers on plain arrays, so the preview's calculations run (and are tested) without
// three.js. Vectors are [x, y, z], quaternions [x, y, z, w]. Unity is left-handed with z forward; three is
// right-handed: a Unity point (x, y, z) is (x, y, -z) here, a Unity quaternion (x, y, z, w) is (-x, -y, z, w).

export const add = (a, b) => [a[0] + b[0], a[1] + b[1], a[2] + b[2]];
export const sub = (a, b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
export const scale = (a, k) => [a[0] * k, a[1] * k, a[2] * k];
export const dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
export const cross = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
export const length = (a) => Math.hypot(a[0], a[1], a[2]);
export function normalize(a) {
  const l = length(a);
  return l > 0 ? [a[0] / l, a[1] / l, a[2] / l] : [0, 0, 0];
}
export const lerp = (a, b, k) => [a[0] + (b[0] - a[0]) * k, a[1] + (b[1] - a[1]) * k, a[2] + (b[2] - a[2]) * k];
export const clamp01 = (v) => Math.min(1, Math.max(0, v));
export const smoothstep01 = (k) => k * k * (3 - 2 * k);

export function qmul(a, b) {
  const [ax, ay, az, aw] = a, [bx, by, bz, bw] = b;
  return [
    aw * bx + ax * bw + ay * bz - az * by,
    aw * by - ax * bz + ay * bw + az * bx,
    aw * bz + ax * by - ay * bx + az * bw,
    aw * bw - ax * bx - ay * by - az * bz,
  ];
}

/** v turned by the unit quaternion q. */
export function qrotate(q, v) {
  const [x, y, z, w] = q;
  const ix = w * v[0] + y * v[2] - z * v[1], iy = w * v[1] + z * v[0] - x * v[2];
  const iz = w * v[2] + x * v[1] - y * v[0], iw = -x * v[0] - y * v[1] - z * v[2];
  return [ix * w + iw * -x + iy * -z - iz * -y, iy * w + iw * -y + iz * -x - ix * -z, iz * w + iw * -z + ix * -y - iy * -x];
}

export function qslerp(a, b, k) {
  let [bx, by, bz, bw] = b;
  let c = a[0] * bx + a[1] * by + a[2] * bz + a[3] * bw;
  if (c < 0) { c = -c; bx = -bx; by = -by; bz = -bz; bw = -bw; }
  let ka = 1 - k, kb = k;
  if (c < 0.9995) {
    const th = Math.acos(c), s = Math.sin(th);
    ka = Math.sin((1 - k) * th) / s;
    kb = Math.sin(k * th) / s;
  }
  const q = [a[0] * ka + bx * kb, a[1] * ka + by * kb, a[2] * ka + bz * kb, a[3] * ka + bw * kb];
  const l = Math.hypot(q[0], q[1], q[2], q[3]);
  return [q[0] / l, q[1] / l, q[2] / l, q[3] / l];
}

export function qfromAxisAngle(axis, rad) {
  const s = Math.sin(rad / 2);
  return [axis[0] * s, axis[1] * s, axis[2] * s, Math.cos(rad / 2)];
}

/** The rotation of Euler angles (radians) applied in three's "YXZ" order. */
export function qfromEulerYXZ(x, y, z) {
  return qmul(qmul(qfromAxisAngle([0, 1, 0], y), qfromAxisAngle([1, 0, 0], x)), qfromAxisAngle([0, 0, 1], z));
}

/** The rotation whose columns are the basis vectors right, up and back. */
export function qfromBasis(right, up, back) {
  const [m00, m10, m20] = right, [m01, m11, m21] = up, [m02, m12, m22] = back;
  const tr = m00 + m11 + m22;
  let x, y, z, w;
  if (tr > 0) {
    const s = 0.5 / Math.sqrt(tr + 1);
    w = 0.25 / s; x = (m21 - m12) * s; y = (m02 - m20) * s; z = (m10 - m01) * s;
  } else if (m00 > m11 && m00 > m22) {
    const s = 2 * Math.sqrt(1 + m00 - m11 - m22);
    w = (m21 - m12) / s; x = 0.25 * s; y = (m01 + m10) / s; z = (m02 + m20) / s;
  } else if (m11 > m22) {
    const s = 2 * Math.sqrt(1 + m11 - m00 - m22);
    w = (m02 - m20) / s; x = (m01 + m10) / s; y = 0.25 * s; z = (m12 + m21) / s;
  } else {
    const s = 2 * Math.sqrt(1 + m22 - m00 - m11);
    w = (m10 - m01) / s; x = (m02 + m20) / s; y = (m12 + m21) / s; z = 0.25 * s;
  }
  return [x, y, z, w];
}

export const fromUnityPoint = (p) => [p[0], p[1], -p[2]];
export const fromUnityQuat = (q) => [-q[0], -q[1], q[2], q[3]];

/** An sRGB component (0-1) made linear, as the game's linear colour space sees it. */
export const toLinear = (v) => (v <= 0.04045 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4));

/** Park-Miller random numbers in [0, 1) from a seed: the same seed always gives the same numbers. */
export function seeded(seed) {
  let s = Math.abs(Math.round(seed * 7919 + 12345)) % 2147483647 || 1;
  return () => (s = (s * 16807) % 2147483647) / 2147483647;
}

/** A fixed pseudo-random number in [0, 1) for an integer. */
export function hash01(i) {
  let s = (i * 7919 + 13) % 2147483647;
  s = (s * 16807) % 2147483647;
  s = (s * 16807) % 2147483647;
  return s / 2147483647;
}

// The game's particle systems, emulated particle by particle at a given age since they started, so the same time
// always gives the same particles: each particle's random numbers come from the system's seed. Its place is computed
// directly at its age, in the emitter's own axes: start speed and gravity (with the Limit Velocity dampen, Unity's
// per-frame factor at 60 fps, as an exponential decay), plus the integral of the velocity over lifetime and the
// double integral of the force over lifetime (both functions of the normalized age only).
// Shader "Sonic Ether/Particles/Additive" (read from its compiled code): 2 x vertex colour x tint x texture x
// exp(5 x _EmissionGain), alpha included, blended SrcAlpha One into an HDR target; colours are sRGB made linear.
import { seeded, qrotate, qmul, qfromAxisAngle, toLinear } from "./math.js";

const rad = (d) => (d * Math.PI) / 180;

/** Unity's Euler rotation (degrees, applied z, then x, then y) as a quaternion in Unity's own axes. */
function unityEuler([x, y, z]) {
  return qmul(qmul(qfromAxisAngle([0, 1, 0], rad(y)), qfromAxisAngle([1, 0, 0], rad(x))), qfromAxisAngle([0, 0, 1], rad(z)));
}

/** Start place and direction of a particle on the shape (Unity axes, before the shape's own scale, turn and place). */
function onShape(shape, a1, a2, a3) {
  const r = shape.radius;
  switch (shape.type) {
    case "cone": { const rr = r * Math.sqrt(a1), an = a2 * 2 * Math.PI; return { p: [rr * Math.cos(an), rr * Math.sin(an), shape.length * a3], d: [0, 0, 1] }; }
    case "edge": return { p: [(a1 * 2 - 1) * r, 0, 0], d: [0, 1, 0] };
    case "circle": { const an = a1 * 2 * Math.PI, c = Math.cos(an), s = Math.sin(an); return { p: [c * r, s * r, 0], d: [c, s, 0] }; }
    case "sphere": {
      const z = 2 * a1 - 1, phi = 2 * Math.PI * a2, k = Math.sqrt(1 - z * z), d = [k * Math.cos(phi), k * Math.sin(phi), z], rr = r * Math.cbrt(a3);
      return { p: [d[0] * rr, d[1] * rr, d[2] * rr], d };
    }
    default: return { p: [0, 0, 0], d: [0, 0, 1] };
  }
}

/**
 * Emits a spec's particles alive at `age` seconds after the system started. `frame` places the emitter in three's
 * axes: { o: origin, q: rotation, shapeScale, sizeScale }. Calls out(texture ref, position, linear rgb, size,
 * max screen size) for each live particle.
 */
export function emit(spec, frame, age, seed, out) {
  const rnd = seeded(seed);
  const count = Array.isArray(spec.count) ? Math.round(spec.count[0] + (spec.count[1] - spec.count[0]) * rnd()) : spec.count;
  const qInv = [-frame.q[0], -frame.q[1], -frame.q[2], frame.q[3]];
  let g = null;
  if (spec.gravity) {
    const up = qrotate(qInv, [0, 1, 0]), k = -spec.gravity * 9.81; // world up in the emitter's axes (three), then Unity's
    g = [up[0] * k, up[1] * k, -up[2] * k];
  }
  const sh = spec.shape, shapeQ = sh.rot[0] || sh.rot[1] || sh.rot[2] ? unityEuler(sh.rot) : null, ks = frame.shapeScale || 1;
  const mat = spec.mat, lambda = spec.dampen > 0 && spec.dampen < 1 ? -60 * Math.log(1 - spec.dampen) : 0;
  for (let i = 0; i < count; i++) {
    const life = spec.life[0] + (spec.life[1] - spec.life[0]) * rnd();
    const rv0 = rnd(), rv1 = rnd(), rv2 = rnd(), rf0 = rnd(), rf1 = rnd(), rf2 = rnd();
    const rc = rnd(), rcl = rnd(), rs = rnd(), rsl = rnd(), a1 = rnd(), a2 = rnd(), a3 = rnd(), rsp = rnd();
    const a = age - i * spec.birth;
    if (a < 0 || a >= life) continue;
    const start = onShape(sh, a1, a2, a3);
    let p = [start.p[0] * sh.scale[0], start.p[1] * sh.scale[1], start.p[2] * sh.scale[2]], dir = start.d;
    if (shapeQ) { p = qrotate(shapeQ, p); dir = qrotate(shapeQ, dir); }
    p = [(p[0] + sh.pos[0]) * ks, (p[1] + sh.pos[1]) * ks, (p[2] + sh.pos[2]) * ks];
    const speed = spec.speed[0] + (spec.speed[1] - spec.speed[0]) * rsp;
    const u = a / life, V = spec.vol, F = spec.force, rv = [rv0, rv1, rv2], rf = [rf0, rf1, rf2], AX = ["x", "y", "z"];
    for (let ax = 0; ax < 3; ax++) {
      const v0 = dir[ax] * speed, gx = g ? g[ax] : 0;
      if (lambda > 0) { const e = Math.exp(-lambda * a); p[ax] += ((v0 - gx / lambda) * (1 - e)) / lambda + (gx / lambda) * a; }
      else p[ax] += v0 * a + 0.5 * gx * a * a;
      if (V) p[ax] += V[AX[ax]].i1(u, rv[ax]) * life;
      if (F) p[ax] += F[AX[ax]].i2(u, rf[ax]) * life * life;
    }
    const local = qrotate(frame.q, [p[0], p[1], -p[2]]);
    const world = [local[0] + frame.o[0], local[1] + frame.o[1], local[2] + frame.o[2]];
    const sc = spec.startColor(0, rc), cl = spec.color ? spec.color(u, rcl) : [1, 1, 1, 1];
    const alpha = sc[3] * cl[3];
    // the shader scales its whole output (alpha too) by exp(5 x gain); alpha then scales the colour (SrcAlpha One)
    const k = 2 * mat.e * 2 * alpha * mat.a * mat.e;
    const rgb = [toLinear(sc[0] * cl[0]) * mat.c[0] * k, toLinear(sc[1] * cl[1]) * mat.c[1] * k, toLinear(sc[2] * cl[2]) * mat.c[2] * k];
    const size = (spec.size[0] + (spec.size[1] - spec.size[0]) * rs) * (spec.sizeLife ? spec.sizeLife(u, rsl) : 1) * (frame.sizeScale || 1);
    out(mat.tex, world, rgb, size, spec.maxSize);
  }
}

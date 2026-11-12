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
    // a unit rectangle (Rectangle) or box (Box) around the emitter, sending its particles along its normal, z
    case "rectangle": return { p: [a1 - 0.5, a2 - 0.5, 0], d: [0, 0, 1] };
    case "box": return { p: [a1 - 0.5, a2 - 0.5, a3 - 0.5], d: [0, 0, 1] };
    default: return { p: [0, 0, 0], d: [0, 0, 1] };
  }
}

/**
 * Emits a spec's particles alive at `age` seconds after the system started. `frame` places the emitter in three's
 * axes: { o: origin, q: rotation, shapeScale, sizeScale }; or, for an emitter that moves, a function of a particle's
 * birth (seconds after the start) giving the emitter's frame then, so that each particle starts where the emitter
 * was (a system simulated in the world leaves its particles behind). Calls out(texture ref, position, linear rgb,
 * size, max screen size) for each live particle.
 */
export function emit(spec, frame, age, seed, out) {
  const rnd = seeded(seed);
  /** What a particle needs from its emitter's frame: the frame, gravity in the emitter's (Unity) axes, shape scale. */
  const setup = (fr) => {
    let g = null;
    if (spec.gravity) {
      const qInv = [-fr.q[0], -fr.q[1], -fr.q[2], fr.q[3]];
      const up = qrotate(qInv, [0, 1, 0]), k = -spec.gravity * 9.81; // world up in the emitter's axes (three), then Unity's
      g = [up[0] * k, up[1] * k, -up[2] * k];
    }
    // the shape's scale: one number, or one per axis (an emitter whose transform stretches only its shape)
    const ks = Array.isArray(fr.shapeScale) ? fr.shapeScale : [fr.shapeScale || 1, fr.shapeScale || 1, fr.shapeScale || 1];
    return { fr, g, ks };
  };
  const fixed = typeof frame === "function" ? null : setup(frame);
  const sh = spec.shape, shapeQ = sh.rot[0] || sh.rot[1] || sh.rot[2] ? unityEuler(sh.rot) : null;
  const mat = spec.mat, lambda = spec.dampen > 0 && spec.dampen < 1 ? -60 * Math.log(1 - spec.dampen) : 0;
  /** Particle i, born i x birth after the start, from the random numbers `r` gives. */
  const one = (i, r) => {
    const life = spec.life[0] + (spec.life[1] - spec.life[0]) * r();
    const rv0 = r(), rv1 = r(), rv2 = r(), rf0 = r(), rf1 = r(), rf2 = r();
    const rc = r(), rcl = r(), rs = r(), rsl = r(), a1 = r(), a2 = r(), a3 = r(), rsp = r();
    const a = age - i * spec.birth;
    if (a < 0 || a >= life) return;
    const { fr, g, ks } = fixed || setup(frame(i * spec.birth));
    const start = onShape(sh, a1, a2, a3);
    let p = [start.p[0] * sh.scale[0], start.p[1] * sh.scale[1], start.p[2] * sh.scale[2]], dir = start.d;
    if (shapeQ) { p = qrotate(shapeQ, p); dir = qrotate(shapeQ, dir); }
    p = [(p[0] + sh.pos[0]) * ks[0], (p[1] + sh.pos[1]) * ks[1], (p[2] + sh.pos[2]) * ks[2]];
    const speed = spec.speed[0] + (spec.speed[1] - spec.speed[0]) * rsp;
    const u = a / life, V = spec.vol, F = spec.force, rv = [rv0, rv1, rv2], rf = [rf0, rf1, rf2], AX = ["x", "y", "z"];
    for (let ax = 0; ax < 3; ax++) {
      const v0 = dir[ax] * speed, gx = g ? g[ax] : 0;
      if (lambda > 0) { const e = Math.exp(-lambda * a); p[ax] += ((v0 - gx / lambda) * (1 - e)) / lambda + (gx / lambda) * a; }
      else p[ax] += v0 * a + 0.5 * gx * a * a;
      if (V) p[ax] += V[AX[ax]].i1(u, rv[ax]) * life;
      if (F) p[ax] += F[AX[ax]].i2(u, rf[ax]) * life * life;
    }
    const local = qrotate(fr.q, [p[0], p[1], -p[2]]);
    const world = [local[0] + fr.o[0], local[1] + fr.o[1], local[2] + fr.o[2]];
    const sc = spec.startColor(0, rc), cl = spec.color ? spec.color(u, rcl) : [1, 1, 1, 1];
    const alpha = sc[3] * cl[3];
    // the shader scales its whole output (alpha too) by exp(5 x gain); alpha then scales the colour (SrcAlpha One)
    const k = 2 * mat.e * 2 * alpha * mat.a * mat.e;
    const rgb = [toLinear(sc[0] * cl[0]) * mat.c[0] * k, toLinear(sc[1] * cl[1]) * mat.c[1] * k, toLinear(sc[2] * cl[2]) * mat.c[2] * k];
    const size = (spec.size[0] + (spec.size[1] - spec.size[0]) * rs) * (spec.sizeLife ? spec.sizeLife(u, rsl) : 1) * (fr.sizeScale || 1);
    out(mat.tex, world, rgb, size, spec.maxSize);
  };
  if (spec.loop) {
    // a looping system: only the particles born within the longest life are alive; each draws its own random numbers,
    // so it is the same particle from frame to frame
    const lifeMax = Math.max(spec.life[0], spec.life[1]);
    const i0 = Math.max(0, Math.ceil((age - lifeMax) / spec.birth)), i1 = Math.floor(age / spec.birth);
    for (let i = i0; i <= i1; i++) one(i, seeded(seed * 100003 + i));
    return;
  }
  const count = Array.isArray(spec.count) ? Math.round(spec.count[0] + (spec.count[1] - spec.count[0]) * rnd()) : spec.count;
  for (let i = 0; i < count; i++) one(i, rnd);
}

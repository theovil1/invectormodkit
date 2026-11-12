// A Unity ParticleSystem (its modules as the game data cache writes them) turned into what the emulator needs:
// counts, start values, shape, velocity and force over lifetime, gravity, dampen, colour and size over lifetime,
// simulation space, and the renderer's material (Sonic Ether additive: tint, alpha, emission gain, texture).
import { hermite, minMaxGradient } from "./curves.js";
import { toLinear } from "./math.js";

export function findNode(node, name) {
  if (!node) return null;
  if (node.name === name) return node;
  for (const c of node.children || []) { const f = findNode(c, name); if (f) return f; }
  return null;
}

/** [min, max] of a MinMaxCurve used as a start value (a constant, or random between two constants). */
function range(c) {
  if (!c) return [0, 0];
  if (c.minMaxState === 3) return [c.minScalar ?? 0, c.scalar ?? 0];
  return [c.scalar ?? 0, c.scalar ?? 0];
}

const SHAPES = { 0: "sphere", 1: "sphere", 2: "sphere", 3: "sphere", 4: "cone", 7: "cone", 8: "cone", 9: "cone", 10: "circle", 11: "circle", 12: "edge" };
const xyz = (o, d = 0) => (o ? [o.x ?? d, o.y ?? d, o.z ?? d] : [d, d, d]);
const axes = (m) => (m && m.enabled ? { x: curveTables(m.x), y: curveTables(m.y), z: curveTables(m.z) } : null);

const STEPS = 64;
/** A curve sampled 65 times over the lifetime, with its integral and its double integral (trapezoids). */
function sampled(curve) {
  const f = hermite(curve && curve.m_Curve), v = new Float64Array(STEPS + 1), i1 = new Float64Array(STEPS + 1), i2 = new Float64Array(STEPS + 1);
  for (let i = 0; i <= STEPS; i++) v[i] = f(i / STEPS);
  for (let i = 1; i <= STEPS; i++) {
    i1[i] = i1[i - 1] + ((v[i] + v[i - 1]) / 2) / STEPS;
    i2[i] = i2[i - 1] + ((i1[i] + i1[i - 1]) / 2) / STEPS;
  }
  return { v, i1, i2 };
}
function lookup(t, u) {
  const x = (u <= 0 ? 0 : u >= 1 ? 1 : u) * STEPS, i = x | 0, j = i < STEPS ? i + 1 : i, k = x - i;
  return t[i] + (t[j] - t[i]) * k;
}

/**
 * A MinMaxCurve over the normalized lifetime u, for a particle's random number r: its value v(u, r), its integral
 * i1(u, r) and double integral i2(u, r) from 0 to u. Velocity and force over lifetime only depend on u, so a
 * particle's place at any age is computed directly from these, without stepping through its life.
 */
export function curveTables(c) {
  if (!c) return { v: () => 0, i1: () => 0, i2: () => 0 };
  const scalar = c.scalar ?? 0;
  if (c.minMaxState === 3 || (c.minMaxState !== 1 && c.minMaxState !== 2)) {
    const lo = c.minMaxState === 3 ? c.minScalar ?? 0 : scalar, value = (r) => lo + (scalar - lo) * r;
    return { v: (u, r) => value(r), i1: (u, r) => value(r) * u, i2: (u, r) => (value(r) * u * u) / 2 };
  }
  const max = sampled(c.maxCurve), min = c.minMaxState === 2 ? sampled(c.minCurve) : max;
  const mix = (key) => (u, r) => { const a = lookup(min[key], u); return scalar * (a + (lookup(max[key], u) - a) * r); };
  return { v: mix("v"), i1: mix("i1"), i2: mix("i2") };
}

export function particleSpec(system, renderer, material) {
  const I = system.InitialModule || {}, S = system.ShapeModule || {}, E = system.EmissionModule || {};
  const burst = (E.m_Bursts || [])[0];
  let count, birth = 0;
  if (burst) {
    const c = burst.countCurve || {};
    count = c.minMaxState === 3 ? [c.minScalar ?? 0, c.scalar ?? 0] : Math.round(c.scalar ?? 0);
  } else {
    const rate = (E.rateOverTime && E.rateOverTime.scalar) || 0;
    count = Math.round(rate * (system.lengthInSec || 0));
    birth = rate > 0 ? 1 / rate : 0;
  }
  const radius = typeof S.radius === "object" && S.radius ? S.radius.value : S.radius;
  const tint = (material && material.colors && (material.colors._TintColor || material.colors._Color)) || [1, 1, 1, 1];
  const gain = (material && material.floats && material.floats._EmissionGain) || 0;
  const tex = material && material.textures && material.textures._MainTex ? material.textures._MainTex.ref : null;
  return {
    count,
    birth,
    life: range(I.startLifetime),
    speed: range(I.startSpeed),
    size: range(I.startSize),
    startColor: minMaxGradient(I.startColor),
    gravity: I.gravityModifier ? I.gravityModifier.scalar ?? 0 : 0,
    shape: S.enabled === false
      ? { type: "point", radius: 0, length: 0, scale: [1, 1, 1], rot: [0, 0, 0], pos: [0, 0, 0] }
      : { type: SHAPES[S.type] || "point", radius: radius ?? 0, length: S.length ?? 0, scale: xyz(S.m_Scale, 1), rot: xyz(S.m_Rotation), pos: xyz(S.m_Position) },
    vol: axes(system.VelocityModule),
    force: axes(system.ForceModule),
    color: system.ColorModule && system.ColorModule.enabled ? minMaxGradient(system.ColorModule.gradient) : null,
    sizeLife: system.SizeModule && system.SizeModule.enabled ? curveTables(system.SizeModule.curve).v : null,
    dampen: system.ClampVelocityModule && system.ClampVelocityModule.enabled ? system.ClampVelocityModule.dampen ?? 0 : 0,
    world: system.moveWithTransform === 1,
    maxSize: (renderer && renderer.m_MaxParticleSize) || 0.5,
    mat: { tint: tint.slice(0, 3), c: tint.slice(0, 3).map(toLinear), a: tint[3] ?? 1, gain, e: Math.exp(5 * gain), tex },
  };
}

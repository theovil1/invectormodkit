import test from "node:test";
import assert from "node:assert/strict";
import { particleSpec, findNode, additiveColor, emitterScales } from "../preview/particlespec.js";
import { toLinear } from "../preview/math.js";
import { emit } from "../preview/particles.js";
import { cache, HAS_CACHE } from "./gamecache-fixture.js";

const near = (a, b, eps = 1e-6, what = "") => assert.ok(Math.abs(a - b) <= eps, `${what} ${a} is not ${b}`);
const C = (scalar) => ({ minMaxState: 0, scalar });
const identity = { o: [0, 0, 0], q: [0, 0, 0, 1], shapeScale: 1, sizeScale: 1 };

/** A burst of 10 particles living 1 s, from an edge 4 wide, moving 5 a second along the emitter's z. */
const edgeBurst = () => particleSpec(
  {
    lengthInSec: 1, moveWithTransform: 0, scalingMode: 0,
    InitialModule: { startLifetime: C(1), startSpeed: C(0), startSize: C(0.1), startColor: { minMaxState: 0, maxColor: { r: 1, g: 1, b: 1, a: 1 } }, gravityModifier: C(0) },
    ShapeModule: { enabled: true, type: 12, radius: { value: 2 }, length: 0, angle: 0, m_Scale: { x: 1, y: 1, z: 1 }, m_Rotation: { x: 0, y: 0, z: 0 }, m_Position: { x: 0, y: 0, z: 0 } },
    EmissionModule: { rateOverTime: C(0), m_Bursts: [{ time: 0, countCurve: C(10) }] },
    VelocityModule: { enabled: true, x: C(0), y: C(0), z: C(5) },
    ForceModule: { enabled: false }, ColorModule: { enabled: false }, SizeModule: { enabled: false }, ClampVelocityModule: { enabled: false },
  },
  { m_MaxParticleSize: 0.5 },
  { colors: { _TintColor: [1, 1, 1, 1] }, floats: { _EmissionGain: 0 }, textures: { _MainTex: { ref: "tex" } } },
);

const collect = (spec, age, seed) => {
  const out = [];
  emit(spec, identity, age, seed, (tex, p, c, size) => out.push({ tex, p, c, size }));
  return out;
};

test("a synthetic edge burst", () => {
  const spec = edgeBurst(), a = collect(spec, 0.5, 7);
  assert.equal(a.length, 10);
  for (const { p, tex } of a) {
    assert.equal(tex, "tex");
    assert.ok(p[0] >= -2 && p[0] <= 2, String(p[0]));
    near(p[2], -2.5, 0.05, "z");
  }
  assert.deepEqual(collect(spec, 0.5, 7), a);
  assert.notDeepEqual(collect(spec, 0.5, 8).map((x) => x.p), a.map((x) => x.p));
});

/** A system that loops: 10 particles a second, each living 0.5 s, from the same edge (no burst). */
const looping = () => {
  const spec = edgeBurst();
  return particleSpec(
    {
      lengthInSec: 1, looping: 1, moveWithTransform: 0, scalingMode: 0,
      InitialModule: { startLifetime: C(0.5), startSpeed: C(0), startSize: C(0.1), startColor: { minMaxState: 0, maxColor: { r: 1, g: 1, b: 1, a: 1 } }, gravityModifier: C(0) },
      ShapeModule: { enabled: true, type: 12, radius: { value: 2 }, length: 0, angle: 0, m_Scale: { x: 1, y: 1, z: 1 }, m_Rotation: { x: 0, y: 0, z: 0 }, m_Position: { x: 0, y: 0, z: 0 } },
      EmissionModule: { rateOverTime: C(10), m_Bursts: [] },
      VelocityModule: { enabled: true, x: C(0), y: C(0), z: C(5) },
      ForceModule: { enabled: false }, ColorModule: { enabled: false }, SizeModule: { enabled: false }, ClampVelocityModule: { enabled: false },
    },
    { m_MaxParticleSize: 0.5 },
    { colors: { _TintColor: [1, 1, 1, 1] }, floats: { _EmissionGain: 0 }, textures: { _MainTex: { ref: spec.mat.tex } } },
  );
};

test("a looping system keeps emitting while it plays", () => {
  const spec = looping();
  assert.equal(spec.loop, true);
  // born every 0.1 s, each lives 0.5 s: at 5.05 s the ones born at 4.6, 4.7, 4.8, 4.9 and 5.0 s are alive
  const a = collect(spec, 5.05, 11);
  assert.equal(a.length, 5);
  assert.deepEqual(collect(spec, 5.05, 11), a);
  // a particle keeps its own random start from frame to frame: the one born at 4.9 s has moved 0.5 more by 5.15 s
  const later = collect(spec, 5.15, 11);
  assert.equal(later.length, 5);
  near(later[2].p[0], a[3].p[0], 1e-9, "same place across the edge");
  near(later[2].p[2] - a[3].p[2], -0.5, 1e-9, "moved along z");
});

test("a moving emitter leaves each particle where it was born", () => {
  const base = looping(), spec = { ...base, vol: null, shape: { ...base.shape, type: "point", radius: 0 } }; // a particle stays where it was born
  // the emitter moves 10 units a second along x; at 5.05 s the five live particles were born at 4.6 .. 5.0 s
  const at = (birthAge) => ({ o: [10 * birthAge, 0, 0], q: [0, 0, 0, 1], shapeScale: 1, sizeScale: 1 });
  const out = [];
  emit(spec, at, 5.05, 11, (tex, p) => out.push(p));
  assert.deepEqual(out.map((p) => Math.round(p[0])), [46, 47, 48, 49, 50]);
});

test("a rectangle emitter, stretched by its transform, spreads its particles over the stretched rectangle", () => {
  const base = looping(), spec = { ...base, vol: null, speed: [2, 2], shape: { ...base.shape, type: "rectangle", radius: 0 } };
  const out = [];
  emit(spec, { o: [0, 0, 0], q: [0, 0, 0, 1], shapeScale: [12, 4, 1], sizeScale: 1 }, 5.05, 11, (tex, p) => out.push(p));
  assert.equal(out.length, 5);
  for (const p of out) {
    assert.ok(Math.abs(p[0]) <= 6 && Math.abs(p[1]) <= 2, p.join(","));
    assert.ok(p[2] < 0, "moved along the rectangle's normal (Unity +z, three -z)");
  }
  assert.ok(Math.max(...out.map((p) => Math.abs(p[0]))) > 2, "spread across the 12 wide rectangle");
});

test("particles die", () => {
  let calls = 0;
  emit(edgeBurst(), identity, 1.01, 3, () => calls++);
  assert.equal(calls, 0);
});

test("the game's particle systems as the prototype read them", { skip: !HAS_CACHE }, () => {
  const systemOf = (file, path) => {
    let node = cache.json(file).root;
    for (const name of path) node = findNode(node, name);
    const ps = node.components.find((c) => c.type === "ParticleSystem").fields;
    const r = node.components.find((c) => c.type === "ParticleSystemRenderer").fields;
    return particleSpec(ps, r, r.m_Materials[0]);
  };
  const swirl = systemOf("objects/XBOX_Pickup_Left_X_OPTIMIZED.json", ["Swirly Particles"]);
  assert.equal(swirl.count, 70);
  assert.deepEqual(swirl.life, [0.35, 0.5]);
  assert.deepEqual(swirl.size, [0.1, 0.15]);
  assert.equal(swirl.shape.type, "edge");
  near(swirl.shape.radius, 1);
  assert.equal(swirl.world, true);
  near(swirl.vol.x.v(0, 0), 12.5, 0.01, "vol x at 0, r 0");
  near(swirl.vol.x.v(0, 1), -12.5, 0.01, "vol x at 0, r 1");
  near(swirl.vol.x.v(1, 0), -25, 0.01, "vol x at 1, r 0");
  assert.deepEqual(swirl.mat.tint.map((v) => Math.round(v * 1000) / 1000), [0.11, 0.431, 1]);
  near(swirl.mat.gain, 0.546, 1e-6);
  const level3 = systemOf("objects/PlayerWalkerGlobal.json", ["PickupParticles", "Level3"]);
  assert.equal(level3.count, 60);
  assert.deepEqual(level3.life, [0.33, 0.7]);
  const land = systemOf("objects/PlayerWalkerGlobal.json", ["PickupParticlesLand"]);
  near(land.gravity, -1);
  near(land.dampen, 0.2, 1e-6);
});

test("a dense moment is cheap", { skip: !HAS_CACHE }, () => {
  const pickup = cache.json("objects/XBOX_Pickup_Left_X_OPTIMIZED.json").root, walker = cache.json("objects/PlayerWalkerGlobal.json").root;
  const specs = [];
  for (const [root, name] of [[pickup, "Swirly Particles"], [pickup, "Multiplier Particles"], [pickup, "Pickup Effects"], [pickup, "Extra Glow"], [findNode(walker, "PickupParticles"), "Level3"]]) {
    const node = findNode(root, name), ps = node.components.find((c) => c.type === "ParticleSystem"), r = node.components.find((c) => c.type === "ParticleSystemRenderer");
    specs.push(particleSpec(ps.fields, r.fields, r.fields.m_Materials[0]));
  }
  const times = [];
  for (let k = 0; k < 20; k++) {
    const t0 = performance.now();
    let n = 0;
    for (let p = 0; p < 10; p++) for (const s of specs) emit(s, identity, 0.3, p * 10 + 1, () => n++);
    times.push(performance.now() - t0);
  }
  // the first runs warm the JavaScript engine up, as a playing preview does; the best of the rest is the cost (other
  // test files run alongside and share the processor)
  const best = Math.min(...times.slice(3));
  assert.ok(best < 4, `${best} ms`);
});

test("the renderer's mode tells point sprites from mesh particles", { skip: !HAS_CACHE }, () => {
  const node = (file, name) => findNode(cache.json(file).root, name);
  const spec = (n) => {
    const ps = n.components.find((c) => c.type === "ParticleSystem").fields, r = n.components.find((c) => c.type === "ParticleSystemRenderer").fields;
    return particleSpec(ps, r, r.m_Materials[0]);
  };
  // the Bounce hold's line of sparks is named like the List strum's flash, but drawn as points
  assert.equal(spec(node("objects/Pickup_StrumHold_Start__Bounce_OPTIMIZED.json", "StrumPickup Effects")).mode, "points");
  assert.equal(spec(node("objects/Pickup_StrumHold_Start__Bounce_OPTIMIZED.json", "StrumpickupEffect")).mode, "mesh");
  assert.equal(spec(node("objects/Pickup_Strum__List_OPTIMIZED.json", "StrumPickup Effects")).mode, "mesh");
  assert.equal(spec(node("objects/Pickup_Strum__List_OPTIMIZED.json", "LineBokehParticles")).mode, "points");
  // a mesh particle names its mesh; sprites have none
  assert.equal(spec(node("objects/Pickup_Strum__List_OPTIMIZED.json", "StrumPickup Effects")).mesh, "assets_shared--8301003112650422039");
  assert.equal(spec(node("objects/Pickup_Strum__List_OPTIMIZED.json", "LineBokehParticles")).mesh, null);
  // the strum flash's mesh starts turned -90 degrees about x (3D start rotation, radians, Unity axes)
  const flash = spec(node("objects/Pickup_StrumHold_Start__Bounce_OPTIMIZED.json", "StrumpickupEffect"));
  near(flash.rot3d[0], -Math.PI / 2, 1e-5, "x");
  near(flash.rot3d[1], 0, 1e-9, "y");
  near(flash.rot3d[2], 0, 1e-9, "z");
  // the sparks of a held hold loop for as long as it is held; the walker's bursts do not
  assert.equal(spec(node("objects/PickupStrumHoldFiller__Bounce__All_Platforms.json", "Playground Fire Replacement")).loop, true);
  assert.equal(spec(findNode(node("objects/PlayerWalkerGlobal.json", "PickupParticles"), "Level3")).loop, false);
});

test("a Sonic Ether additive mesh glows as its shader does: tint x 4 x exp(5 gain)^2 x alpha", { skip: !HAS_CACHE }, () => {
  const filler = cache.json("objects/PickupStrumHoldFiller__Bounce__All_Platforms.json").root;
  const material = (name) => { const n = findNode(findNode(filler, "Level2"), name); return n.components.find((c) => c.type === "MeshRenderer" || c.type === "SpriteRenderer").fields.m_Materials[0]; };
  const glow = additiveColor(material("StrumHoldGlowLine")); // gain 0.25, tint (0.937, 0.557, 0.278, 1)
  const k = 4 * Math.exp(5 * 0.25) ** 2;
  near(glow[0], toLinear(0.9372549) * k, 1e-3, "r");
  near(glow[2], toLinear(0.278431356) * k, 1e-3, "b");
  const scroll = additiveColor(material("StrumholdScrollTexture")); // gain 0.2, tint alpha 0.502
  near(scroll[0], 4 * Math.exp(1) ** 2 * 0.5019608, 1e-3, "scroll r");
});

test("a system's transform scales its particles unless it scales only its shape", { skip: !HAS_CACHE }, () => {
  const filler = cache.json("objects/PickupStrumHoldFiller__Bounce__All_Platforms.json").root;
  const node = findNode(filler, "Playground Fire Replacement (1)"); // scaled 12 x 3.97, scaling mode Shape
  const ps = node.components.find((c) => c.type === "ParticleSystem").fields, r = node.components.find((c) => c.type === "ParticleSystemRenderer").fields;
  const spec = particleSpec(ps, r, r.m_Materials[0]);
  assert.equal(spec.shape.type, "rectangle");
  assert.deepEqual(emitterScales(spec, 12, [12, 3.97, 1]), { shapeScale: [12, 3.97, 1], sizeScale: 1 });
  const pickup = cache.json("objects/XBOX_Pickup_Left_X_OPTIMIZED.json").root, flash = findNode(pickup, "Pickup Effects"); // Hierarchy
  const fps = flash.components.find((c) => c.type === "ParticleSystem").fields, fr = flash.components.find((c) => c.type === "ParticleSystemRenderer").fields;
  assert.deepEqual(emitterScales(particleSpec(fps, fr, fr.m_Materials[0]), 0.9), { shapeScale: 0.9, sizeScale: 0.9 });
});

// The game's effects around the ship and the notes, for the time shown: the particle bursts of taken notes (the
// walker's PickupParticles, StrumParticles and PickupParticlesLand, each pickup's own effects, the strums' line
// bokeh), the level halo of taken pickups, the landing glow, the ship's trails, and the hold walls. Particles are
// emulated from their modules (particles.js), so the same time gives the same particles. Buffers are allocated once,
// grow when a frame needs more, and are refilled every frame.
import * as THREE from "three";
import { isTurn } from "../model.js";
import { particleSpec, particleMaterial, findNode } from "./particlespec.js";
import { emit } from "./particles.js";
import { SHIP_SCALE } from "./ship.js";
import { trailSets, haloLevels } from "./fxspec.js";
import { qmul, qrotate, qfromBasis, qfromAxisAngle, toLinear } from "./math.js";

const KEYS = ["left", "right", "down", "up"];
const PICKUPS = { left: "XBOX Pickup Left X OPTIMIZED", right: "XBOX Pickup Right B OPTIMIZED", down: "XBOX Pickup Down A OPTIMIZED", up: "XBOX Pickup Up Y OPTIMIZED", turnL: "Pickup 4 Left OPTIMIZED", turnR: "Pickup 5 Right OPTIMIZED" };
const STRUMS = { strumList: "Pickup Strum, List OPTIMIZED", strumBounce: "Pickup Strum, Bounce OPTIMIZED", holdList: "Pickup StrumHold Start, List OPTIMIZED", holdBounce: "Pickup StrumHold Start, Bounce OPTIMIZED" };
// the systems a taken note plays, as the prototype read the game: an arrow's flash, swirl, multiplier and glow, a
// turn's flash, swirl and glow, a strum's line bokeh
const PICKUP_SYSTEMS = /^(Pickup Effects|TurnPickup Effects|Swirly Particles|Multiplier Particles|Extra Glow)$/;
const STRUM_SYSTEMS = /^LineBokehParticles$/;
const UQ = (x, y, z, w) => [-x, -y, z, w]; // a Unity quaternion in three's axes
const TILT = UQ(-0.2588, 0, 0, 0.9659); // the walker's particle levels lean 30 degrees (FollowLocalRotation)
const FLAT = UQ(-0.7071, 0, 0, 0.7071); // the landing burst lies flat
const MAGENTA = [0.8228, 0.0061, 0.6795]; // player 1's streak colour (ShipColorScheme, sRGB 0.915, 0.072, 0.843) made linear
const HALO_TILT = UQ(0.5, 0, 0, 0.866); // the halo sprite is tilted 60 degrees about x
const TRAIL_RATE = 90; // trail samples per second of trail

const FX_VERT = "attribute float size; attribute float maxPx; attribute vec3 color; varying vec3 vCol; uniform float projScale;" +
  "void main(){ vec4 mv = modelViewMatrix * vec4(position, 1.0); gl_Position = projectionMatrix * mv; vCol = color;" +
  " gl_PointSize = min(size * projScale / max(-mv.z, 0.001), maxPx); }";
const FX_FRAG = "uniform sampler2D map; varying vec3 vCol; void main(){ vec4 t = texture2D(map, gl_PointCoord); gl_FragColor = vec4(vCol * t.rgb * t.a, 1.0); }";
// "Avicii/Triangle Tunnel CG Wall Scrolling UVs" (read from its compiled code): colour 3 x _color, x3 more over the
// last 0.75 of the hold, times the texture at (distance / 15 + _ScrollUV x _Time.x, across), alpha alike, SrcAlpha One
const HOLD_VERT = "attribute vec3 hold; varying vec3 vH; void main(){ vH = hold; gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }";
const HOLD_FRAG = "uniform sampler2D map; uniform vec3 color; uniform float alpha; uniform float scroll; varying vec3 vH;" +
  "void main(){ float k = (vH.z - vH.x <= 0.75) ? 3.0 : 1.0; vec4 tx = texture2D(map, vec2(vH.x * 0.0666667 + scroll, vH.y));" +
  " vec3 c = 3.0 * k * color * tx.rgb; float a = 3.0 * k * alpha * tx.a; gl_FragColor = vec4(c * a, 1.0); }";

function dotTexture() {
  const c = document.createElement("canvas");
  c.width = c.height = 64;
  const g = c.getContext("2d"), r = g.createRadialGradient(32, 32, 0, 32, 32, 32);
  r.addColorStop(0, "rgba(255,255,255,1)"); r.addColorStop(0.3, "rgba(255,255,255,.6)"); r.addColorStop(1, "rgba(255,255,255,0)");
  g.fillStyle = r; g.fillRect(0, 0, 64, 64);
  return new THREE.CanvasTexture(c);
}

/** Typed vertex buffers for one draw call, refilled every frame; they double when a frame needs more. */
class Buffers {
  constructor(layout, capacity) {
    this.layout = Object.entries(layout);
    this.count = 0;
    this.geometry = new THREE.BufferGeometry();
    this.arrays = {};
    this.grow(capacity);
  }
  grow(capacity) {
    this.capacity = capacity;
    for (const [name, size] of this.layout) {
      const arr = new Float32Array(capacity * size);
      if (this.arrays[name]) arr.set(this.arrays[name]);
      this.arrays[name] = arr;
      const attr = new THREE.BufferAttribute(arr, size);
      attr.setUsage(THREE.DynamicDrawUsage);
      this.geometry.setAttribute(name, attr);
    }
  }
  reset() { this.count = 0; }
  /** Adds a vertex: its attribute values, in the layout's order. */
  push(...values) {
    if (this.count >= this.capacity) this.grow(this.capacity * 2);
    for (let a = 0; a < this.layout.length; a++) {
      const [name, size] = this.layout[a], arr = this.arrays[name], v = values[a], o = this.count * size;
      if (size === 1) arr[o] = v; else for (let k = 0; k < size; k++) arr[o + k] = v[k];
    }
    this.count++;
  }
  commit() {
    for (const [name] of this.layout) this.geometry.getAttribute(name).needsUpdate = true;
    this.geometry.setDrawRange(0, this.count);
  }
}

export async function createEffects(cache, rig, level) {
  const root = new THREE.Group();
  const specOf = (node) => {
    const ps = node.components.find((c) => c.type === "ParticleSystem"), r = node.components.find((c) => c.type === "ParticleSystemRenderer");
    return ps && r ? particleSpec(ps.fields, r.fields, (r.fields.m_Materials || [])[0]) : null;
  };
  const walkerTree = cache.object("PlayerWalkerGlobal") ? cache.object("PlayerWalkerGlobal").root : null;
  const levelSpecs = (name) => [0, 1, 2, 3].map((l) => { const n = findNode(findNode(walkerTree, name), "Level" + l); return n ? specOf(n) : null; });
  const shipBurst = levelSpecs("PickupParticles"), strumBurst = levelSpecs("StrumParticles");
  const landNode = findNode(walkerTree, "PickupParticlesLand"), landBurst = landNode ? specOf(landNode) : null;

  // MaterialSwitcher: the walker's bursts take the last arrow's material from MaterialsDatabase (XBOX: controlType 2)
  const db = ((cache.settings.scriptables || {}).MaterialsDatabase || [])[0];
  const entry = db ? (db.fields.materialPerControlType || []).find((e) => e.controlType === 2) : null;
  const switched = {};
  if (entry) for (const [k, f] of [["left", "materialPickupLeft"], ["right", "materialPickupRight"], ["down", "materialPickupDown"], ["up", "materialPickupUp"], ["turn", "materialPickupTurn"]]) if (entry[f]) switched[k] = particleMaterial(entry[f]);

  /**
   * A prefab's effect systems with their place under its root (the prefab's own transforms, in three's axes, the
   * root's position left out as the preview places prefabs), and its colour, scale and halo. Systems switched off at
   * rest count too: the game switches them on when the note is taken.
   */
  function effectsOf(name, systems) {
    const json = cache.object(name);
    if (!json) return { systems: [], color: [1, 1, 1], scale: 1, halo: null };
    const list = [];
    const local = (n) => ({ p: [n.position[0], n.position[1], -n.position[2]], q: UQ(...n.rotation), s: n.scale[0] });
    const visit = (node, parent) => {
      const l = local(node), r = qrotate(parent.q, l.p);
      const w = { p: [0, 1, 2].map((i) => parent.p[i] + parent.s * r[i]), q: qmul(parent.q, l.q), s: parent.s * l.s };
      if (systems.test(node.name)) { const spec = specOf(node); if (spec) list.push({ spec, p: w.p, q: w.q, scale: w.s }); }
      for (const c of node.children) visit(c, w);
    };
    const rootT = { ...local(json.root), p: [0, 0, 0] };
    for (const c of json.root.children) visit(c, rootT);
    const pickup = json.root.components.find((c) => c.class === "Pickup");
    const color = pickup ? [pickup.fields.color.r, pickup.fields.color.g, pickup.fields.color.b] : [0.929, 0.738, 0];
    const halo = json.root.components.some((c) => c.class === "PickupStatesPickup") ? haloLevels(json.root) : null;
    return { systems: list, color, scale: rootT.s, halo };
  }
  const pickupFx = {}, strumFx = {};
  for (const [k, n] of Object.entries(PICKUPS)) pickupFx[k] = effectsOf(n, PICKUP_SYSTEMS);
  for (const [k, n] of Object.entries(STRUMS)) strumFx[k] = effectsOf(n, STRUM_SYSTEMS);

  // textures of every particle system, one Points draw per texture
  const allSpecs = [...shipBurst, ...strumBurst, landBurst, ...Object.values(pickupFx).flatMap((f) => f.systems.map((s) => s.spec)), ...Object.values(strumFx).flatMap((f) => f.systems.map((s) => s.spec))].filter(Boolean);
  const texRefs = new Set([...allSpecs.map((s) => s.mat.tex), ...Object.values(switched).map((m) => m.tex)].filter(Boolean));
  const points = new Map();
  for (const ref of texRefs) {
    const buffers = new Buffers({ position: 3, color: 3, size: 1, maxPx: 1 }, 4096);
    const material = new THREE.ShaderMaterial({ uniforms: { map: { value: await cache.texture(ref) }, projScale: { value: 1 } }, vertexShader: FX_VERT, fragmentShader: FX_FRAG, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending });
    const obj = new THREE.Points(buffers.geometry, material);
    obj.frustumCulled = false;
    root.add(obj);
    points.set(ref, { buffers, material });
  }

  // trails: attach points on the ship model (under its OldRoot), ribbons per texture
  const shipJson = cache.object("Ship Beginner"), shipRoot = shipJson ? shipJson.root : null;
  const trails = shipRoot ? trailSets(shipRoot) : { streak: [[], [], [], []], freefly: [[], [], [], []], onSteer: [] };
  const oldRoot = findNode(shipRoot, "OldRoot"), rootNode = findNode(shipRoot, "Root");
  const oldOrigin = [0, 1, 2].map((i) => (rootNode ? rootNode.position[i] : 0) + (oldRoot ? oldRoot.position[i] : 0)), oldScale = oldRoot ? oldRoot.scale[0] : 1;
  const trailTex = new Set([...trails.streak.flat(), ...trails.freefly.flat(), ...trails.onSteer].map((t) => t.mat.tex).filter(Boolean));
  const ribbons = new Map();
  for (const ref of trailTex) {
    const buffers = new Buffers({ position: 3, color: 3, uv: 2 }, 4096);
    const mesh = new THREE.Mesh(buffers.geometry, new THREE.MeshBasicMaterial({ map: await cache.texture(ref), vertexColors: true, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending, side: THREE.DoubleSide, fog: false }));
    mesh.frustumCulled = false;
    root.add(mesh);
    ribbons.set(ref, buffers);
  }

  // hold walls: the hold walls' triangle is the tunnel's, scaled by their sizes
  const holdWalls = level.holdWalls || null, holdMat = holdWalls && holdWalls.material;
  const holdTex = holdMat && holdMat.textures && holdMat.textures._MainTex ? await cache.texture(holdMat.textures._MainTex.ref) : null;
  if (holdTex) { holdTex.wrapS = holdTex.wrapT = THREE.RepeatWrapping; holdTex.needsUpdate = true; }
  const hc = (holdMat && holdMat.colors && holdMat.colors._color) || [1, 0.714, 0.494, 0.502];
  const holdUniforms = { map: { value: holdTex }, color: { value: new THREE.Color().setRGB(hc[0], hc[1], hc[2], THREE.SRGBColorSpace) }, alpha: { value: hc[3] }, scroll: { value: 0 } };
  const holdBuffers = new Buffers({ position: 3, hold: 3 }, 4096);
  const holdMesh = new THREE.Mesh(holdBuffers.geometry, new THREE.ShaderMaterial({ uniforms: holdUniforms, vertexShader: HOLD_VERT, fragmentShader: HOLD_FRAG, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending, side: THREE.DoubleSide }));
  holdMesh.frustumCulled = false;
  if (holdTex) root.add(holdMesh);
  const scrollU = holdMat && holdMat.colors && holdMat.colors._ScrollUV ? holdMat.colors._ScrollUV[0] : -15;
  const wallScale = holdWalls && level.tunnel && holdWalls.size && level.tunnel.size ? holdWalls.size / level.tunnel.size : 1900 / 1790;
  const tri = rig.tri.map(([x, y]) => [x * wallScale, y * wallScale]);
  const listFaces = [[tri[0], tri[1]], [tri[1], tri[2]], [tri[2], tri[0]]];
  const bounceFaces = [[[tri[1][0] * 0.76, tri[1][1] + 1.725], [tri[2][0] * 0.76, tri[2][1] + 1.725]]];

  // halos (pooled planes, a material per pickup and level) and the landing glow
  const haloPool = [], haloMats = new Map();
  for (const [key, fx] of Object.entries(pickupFx)) {
    if (!fx.halo) continue;
    for (let l = 0; l < fx.halo.levels.length; l++) {
      const L = fx.halo.levels[l], tex = L.tex ? await cache.texture(L.tex) : null;
      let map = null;
      if (tex) {
        map = tex.clone();
        const [tw, th] = tex.userData.size || [L.rect[2], L.rect[3]];
        map.repeat.set(L.rect[2] / tw, L.rect[3] / th);
        map.offset.set(L.rect[0] / tw, L.rect[1] / th);
        map.needsUpdate = true;
      }
      const E = Math.exp(5 * fx.halo.gain), k = 4 * E * E * toLinear(L.grey);
      haloMats.set(key + l, new THREE.MeshBasicMaterial({ map, color: new THREE.Color().setRGB(fx.halo.tint[0], fx.halo.tint[1], fx.halo.tint[2], THREE.SRGBColorSpace).multiplyScalar(k), transparent: true, depthTest: false, depthWrite: false, blending: THREE.AdditiveBlending, side: THREE.DoubleSide, fog: false }));
    }
  }
  const haloGeo = new THREE.PlaneGeometry(1, 1);
  const glowTex = dotTexture();
  const glow = new THREE.Mesh(new THREE.PlaneGeometry(6, 6), new THREE.MeshBasicMaterial({ map: glowTex, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending, fog: false }));
  glow.visible = false;
  root.add(glow);

  const frameQ = (f) => qfromBasis(f.right, f.up, f.back);
  const compose = (frame, sys, scale = 1) => ({ o: frame.o.map((v, i) => v + qrotate(frame.q, sys.p)[i]), q: qmul(frame.q, sys.q), shapeScale: sys.scale * scale, sizeScale: sys.scale * scale });
  const keyOf = (n) => (isTurn(n.color) ? (n.color === 4 ? "turnL" : "turnR") : KEYS[n.color] || "left");
  const drawSize = new THREE.Vector2(), camFwdV = new THREE.Vector3();

  return {
    root,
    update(run, ship, path, camera, renderer, t) {
      for (const p of points.values()) p.buffers.reset();
      for (const r of ribbons.values()) r.reset();
      holdBuffers.reset();
      const height = renderer.getDrawingBufferSize(drawSize).y;
      const out = (tex, p, c, size, maxSize) => { const set = points.get(tex); if (set) set.buffers.push(p, c, size, maxSize * height); };
      const walkerD = path.start + run.distAt(t), wf = path.frameAt(walkerD), wq = frameQ(wf);
      const st = ship.state(t);
      const listLane = ship.lane(t, "List").lane;
      const shipX = st.mode === "Bounce" ? st.sl[0] : 0;
      // the walker's emitters (Particles under the walker, PickupParticles/StrumParticles scaled 2.4)
      const walkerFrame = (x, y, z, q, scale) => ({ o: path.place([x, y, z], walkerD), q: qmul(wq, q), shapeScale: scale, sizeScale: 1 });
      const shipFrame = walkerFrame(shipX, rig.walkerY + 0.225 - 2.4 * 0.638, 2.4 * 1.056, TILT, 2.4);
      const strumFrame = walkerFrame(0, rig.walkerY + 0.225 - 2.4 * 1.32, 2.4 * 2.23, TILT, 2.4);
      const withMat = (spec, mat) => (mat ? { ...spec, mat } : spec);
      let lastCol = "turn", halos = 0;
      for (const n of run.notes) {
        if (n.t > t) break;
        if (!n.strum && n.mode !== "Freefly") lastCol = isTurn(n.color) ? "turn" : KEYS[n.color] || "left";
        // a hold ends with CompletedStrum
        if (n.strum && n.len > 0 && n.mode !== "Freefly") {
          const endAge = t - (n.t + n.len);
          if (endAge >= 0 && endAge <= 0.75) { const lv = run.levelAt(n.t + n.len); if (strumBurst[lv]) emit(strumBurst[lv], strumFrame, endAge, Math.round((n.t + n.len) * 1000), out); }
        }
        const age = t - n.t;
        if (age > 0.75 || n.mode === "Freefly") continue; // PickupEffectTrigger: no pickup effect in Freefly
        const lv = run.levelAt(n.t), seed = Math.round(n.t * 1000);
        const noteD = path.start + run.distAt(n.t) + rig.hitAhead, nf = path.frameAt(noteD), lane = n.lane ?? 1;
        if (n.strum) {
          if (n.len > 0) { if (shipBurst[lv]) emit(withMat(shipBurst[lv], switched[lastCol]), shipFrame, age, seed, out); }
          else if (strumBurst[lv]) emit(strumBurst[lv], strumFrame, age, seed, out);
          const fx = strumFx[(n.len > 0 ? "hold" : "strum") + (n.mode === "List" ? "List" : "Bounce")];
          const base = { o: nf.p, q: frameQ(nf) };
          fx.systems.forEach((sys, i) => emit(sys.spec, compose(base, sys), age, seed + 31 * i, out));
          continue;
        }
        const key = keyOf(n), col = key.startsWith("turn") ? "turn" : key;
        if (shipBurst[lv]) emit(withMat(shipBurst[lv], switched[col]), shipFrame, age, seed, out);
        const fx = pickupFx[key];
        const roll = n.mode === "List" ? ((lane - listLane) * 2 * Math.PI) / 3 : 0, rollQ = qfromAxisAngle([0, 0, 1], roll);
        const at = n.mode === "Bounce" ? [rig.pickupLaneX * (lane - 1), rig.pickupY, 0] : qrotate(rollQ, [0, rig.pickupY, 0]);
        const base = { o: path.place(at, noteD), q: qmul(frameQ(nf), rollQ) };
        fx.systems.forEach((sys, i) => emit(sys.spec, compose(base, sys), age, seed + 1 + i, out));
        if (n.mode === "Bounce" && landBurst) emit(withMat(landBurst, switched[col]), walkerFrame(shipX, rig.walkerY + st.sl[1] - 0.4, -0.84, FLAT, 1), age, seed + 5, out);
        // the level halo round the pickup (PickupStatesPickup: until 0.5 s past the note, by score level, none at 0)
        if (age <= 0.5 && !key.startsWith("turn") && fx.halo && lv >= 1 && fx.halo.levels[lv - 1]) {
          const L = fx.halo.levels[lv - 1];
          if (haloPool.length <= halos) { const m = new THREE.Mesh(haloGeo, haloMats.get(key + (lv - 1))); m.frustumCulled = false; root.add(m); haloPool.push(m); }
          const m = haloPool[halos++];
          m.material = haloMats.get(key + (lv - 1));
          m.visible = true;
          const off = qrotate(rollQ, [L.pos[0] * fx.scale, L.pos[1] * fx.scale, -L.pos[2] * fx.scale]);
          m.position.set(...path.place([at[0] + off[0], at[1] + off[1], off[2]], noteD));
          m.quaternion.set(...qmul(base.q, HALO_TILT));
          m.scale.set(L.w * L.scale * fx.scale, L.h * L.scale * fx.scale, 1);
        }
      }
      for (let i = halos; i < haloPool.length; i++) haloPool[i].visible = false;

      // the landing flash under the ship, in the colour of the last pickup, a quarter second (not in Freefly)
      let last = null;
      for (const n of run.notes) { if (n.t > t) break; if (t - n.t < 0.25) last = n; }
      glow.visible = !!last && last.mode !== "Freefly";
      if (glow.visible) {
        const c = last.strum ? [0.929, 0.738, 0] : pickupFx[keyOf(last)].color, k = 2.2 * (1 - (t - last.t) / 0.25);
        glow.material.color.setRGB(c[0] * k, c[1] * k, c[2] * k);
        const w = ship.world(t, path), up = wf.up, back = wf.back;
        glow.position.set(...[0, 1, 2].map((i) => w.pos[i] - up[i] * 0.6 - back[i] * 1.8));
        glow.quaternion.set(...qmul(wq, qfromAxisAngle([1, 0, 0], -Math.PI / 2)));
      }

      // the ship's trails: the score level's streak set; in Freefly the rings-taken set and the OnSteer pair
      camera.updateMatrixWorld();
      const camPos = camera.position.toArray(), camFwd = camera.getWorldDirection(camFwdV).toArray();
      const depth = (p) => (p[0] - camPos[0]) * camFwd[0] + (p[1] - camPos[1]) * camFwd[1] + (p[2] - camPos[2]) * camFwd[2];
      const list = trails.streak[run.levelAt(t)].map((tr) => ({ tr, since: -Infinity }));
      if (st.mode === "Freefly") {
        const fs = ship.flyStretch(st.si, t);
        for (const tr of trails.freefly[fs.tier]) list.push({ tr, since: fs.tierSince });
        if (fs.steer > 0) for (const tr of trails.onSteer) list.push({ tr, since: fs.steerSince, width: fs.steer, time: fs.steerTime, bright: fs.steer / Math.max(0.001, tr.mat.tint[3]) });
      }
      // the ship's past places on a grid of 1/90 s back from t, shared by every trail
      const past = [];
      const shipAt = (i) => past[i] || (past[i] = ship.world(t - i / TRAIL_RATE, path));
      for (const { tr, since, width = 1, time: tk = 1, bright = 1 } of list) {
        const buf = ribbons.get(tr.mat.tex);
        if (!buf) continue;
        const time = Math.min(tr.time * tk, t - since);
        if (time <= 0.004) continue;
        const E = Math.exp(5 * tr.mat.gain), k = 4 * E * E * tr.mat.tint[3] * bright;
        const local = [SHIP_SCALE * (oldOrigin[0] + oldScale * tr.p[0]), SHIP_SCALE * (oldOrigin[1] + oldScale * tr.p[1]), -SHIP_SCALE * (oldOrigin[2] + oldScale * tr.p[2])];
        const normal = [tr.normal[0], tr.normal[1], -tr.normal[2]];
        const steps = Math.max(1, Math.ceil(time * TRAIL_RATE)), pts = [], nrm = [];
        for (let i = 0; i <= steps; i++) {
          const w = i < steps ? shipAt(i) : ship.world(t - time, path);
          const lp = qrotate(w.q, local);
          pts.push([w.pos[0] + lp[0], w.pos[1] + lp[1], w.pos[2] + lp[2]]);
          nrm.push(tr.align === "view" ? null : qrotate(w.q, normal));
        }
        ribbon(buf, pts, nrm, time, tr.w0 * width, tr.w1 * width, k, depth, camPos, camera.near, tr.mat.fade || 1);
      }

      // the hold walls: lit between a hold's start and end along the track, from its spawn until 1 s after its end
      holdUniforms.scroll.value = (scrollU * t) / 20;
      if (holdTex) {
        for (const n of run.notes) {
          if (n.t > t + 5) break;
          if (!n.strum || !n.len || n.mode === "Freefly" || n.t + n.len + 1 < t) continue;
          const f0 = run.noteDist(n.t, t), f1 = run.noteDist(n.t + n.len, t);
          const segs = Math.max(1, Math.ceil((f1 - f0) / 2));
          for (const [p, q] of n.mode === "List" ? listFaces : bounceFaces) {
            for (let j = 0; j < segs; j++) {
              const fa = f0 + ((f1 - f0) * j) / segs, fb = f0 + ((f1 - f0) * (j + 1)) / segs;
              const A = path.place([p[0], p[1], 0], path.start + fa), B = path.place([q[0], q[1], 0], path.start + fa);
              const C = path.place([p[0], p[1], 0], path.start + fb), D = path.place([q[0], q[1], 0], path.start + fb);
              holdBuffers.push(A, [fa, 0, f1]); holdBuffers.push(B, [fa, 1, f1]); holdBuffers.push(C, [fb, 0, f1]);
              holdBuffers.push(B, [fa, 1, f1]); holdBuffers.push(D, [fb, 1, f1]); holdBuffers.push(C, [fb, 0, f1]);
            }
          }
        }
      }

      const projScale = height / (2 * Math.tan((camera.fov * Math.PI) / 360));
      for (const p of points.values()) { p.material.uniforms.projScale.value = projScale; p.buffers.commit(); }
      for (const r of ribbons.values()) r.commit();
      holdBuffers.commit();
    },
    dispose() {
      root.traverse((o) => {
        if (o.geometry && o.geometry !== haloGeo) o.geometry.dispose();
        if (o.material) o.material.dispose(); // not their textures: the cache's
      });
      for (const m of haloMats.values()) { if (m.map) m.map.dispose(); m.dispose(); } // the halo maps are copies
      holdMesh.geometry.dispose();
      holdMesh.material.dispose();
      haloGeo.dispose();
      glowTex.dispose();
    },
  };
}

/**
 * A trail ribbon along `points` (head first, `time` seconds long), w0 to w1 wide, across `normals` (null: facing
 * the camera), into the buffer as triangles. The game's shader (Sonic Ether/Particles/AdditiveFadeNearClip) is
 * additive and fades out over _FadeDistance from the near plane; the part that runs past the camera is cut where it
 * crosses the near plane.
 */
function ribbon(buf, points, normals, time, w0, w1, k, depth, camPos, near, fade) {
  if (depth(points[0]) < 0.5) return;
  const last = points.length - 1;
  // the head-to-tail fraction of each point: the grid's 1/90 s steps, the last point at the trail's end
  const frac = (i) => (i === last ? 1 : Math.min(1, i / (time * TRAIL_RATE)));
  const pts = [points[0]], nrm = [normals[0]], fs = [0];
  for (let i = 1; i < points.length; i++) {
    const d = depth(points[i]);
    if (d < 0.5) {
      const d0 = depth(points[i - 1]), u = (d0 - 0.5) / (d0 - d);
      pts.push(points[i - 1].map((v, j) => v + (points[i][j] - v) * u)); nrm.push(normals[i]); fs.push(frac(i - 1) + (frac(i) - frac(i - 1)) * u);
      break;
    }
    pts.push(points[i]); nrm.push(normals[i]); fs.push(frac(i));
  }
  if (pts.length < 2) return;
  const side = [], col = [];
  for (let i = 0; i < pts.length; i++) {
    const p = pts[i], a = pts[Math.max(0, i - 1)], b = pts[Math.min(pts.length - 1, i + 1)];
    let tan = [b[0] - a[0], b[1] - a[1], b[2] - a[2]];
    if (tan[0] * tan[0] + tan[1] * tan[1] + tan[2] * tan[2] < 1e-10) tan = [0, 0, 1];
    const n = nrm[i] || [camPos[0] - p[0], camPos[1] - p[1], camPos[2] - p[2]];
    const s = [tan[1] * n[2] - tan[2] * n[1], tan[2] * n[0] - tan[0] * n[2], tan[0] * n[1] - tan[1] * n[0]];
    const l = Math.hypot(s[0], s[1], s[2]) || 1, w = (w0 + (w1 - w0) * fs[i]) / 2 / l;
    side.push([s[0] * w, s[1] * w, s[2] * w]);
    const a2 = k * Math.min(1, Math.max(0, (depth(p) - near) / fade));
    col.push([MAGENTA[0] * a2, MAGENTA[1] * a2, MAGENTA[2] * a2]);
  }
  for (let i = 0; i + 1 < pts.length; i++) {
    const L0 = pts[i].map((v, j) => v - side[i][j]), R0 = pts[i].map((v, j) => v + side[i][j]);
    const L1 = pts[i + 1].map((v, j) => v - side[i + 1][j]), R1 = pts[i + 1].map((v, j) => v + side[i + 1][j]);
    buf.push(L0, col[i], [fs[i], 0]); buf.push(R0, col[i], [fs[i], 1]); buf.push(L1, col[i + 1], [fs[i + 1], 0]);
    buf.push(R0, col[i], [fs[i], 1]); buf.push(R1, col[i + 1], [fs[i + 1], 1]); buf.push(L1, col[i + 1], [fs[i + 1], 0]);
  }
}

// The notes as the game places them: the XBOX pickups (as the game shows a keyboard player) with the white arrow it
// prints on keyboard pickups, turns, strums, holds (start and end bars), beat lines and Freefly gates, each where
// the perfect run puts it at the time shown (Pickup.UpdatePosition: its time to come at the current speed ahead of
// the walker), carried along the level path. In List the notes turn with the tunnel (ship.spin: the tunnel's turn,
// eased as Walker.rotation is, plus 120 degrees a lane), and a strum's label stays upright (Pickup.KeepRotation). A
// level-up pickup shows its "Level Up Text"; selected notes get a thin white outline. Objects come from pools by
// prefab and are reused from frame to frame.
import * as THREE from "three";
import { isTurn } from "../model.js";
import { qfromBasis } from "./math.js";

const KEYS = ["left", "right", "down", "up"]; // model.js colours 0-3
const PREFABS = {
  left: "XBOX Pickup Left X OPTIMIZED", right: "XBOX Pickup Right B OPTIMIZED", down: "XBOX Pickup Down A OPTIMIZED", up: "XBOX Pickup Up Y OPTIMIZED",
  turnL: "Pickup 4 Left OPTIMIZED", turnR: "Pickup 5 Right OPTIMIZED",
  strumList: "Pickup Strum, List OPTIMIZED", strumBounce: "Pickup Strum, Bounce OPTIMIZED",
  holdList: "Pickup StrumHold Start, List OPTIMIZED", holdBounce: "Pickup StrumHold Start, Bounce OPTIMIZED",
  endList: "PickupStrumHoldEnd, List, All Platforms", endBounce: "PickupStrumHoldEnd, Bounce, All Platforms",
  beatList: "BeatLine, List", beatBounce: "BeatLine, Bounce", air: "PickupAir, All Platforms OPTIMIZED",
};
// only what the game shows at rest (the rest plays when a note is taken, failed or spawned)
const AT_REST_HIDDEN = /Failed|Star|Effects|Particles|Level Up|Activated|Activate Shockwave|Spawn Circle|^Symbol$|Fake AA|Level Pickup Effect/i;
const NOTE_GLOW = 0.6, BEAT_GLOW = 6; // the prototype's emissive scales for the pickups and the beat lines
const THIRD = (2 * Math.PI) / 3; // a List lane's turn
// Pickup.KeepRotation: the parts a List pickup keeps upright while it turns with the tunnel (the strums' label); the
// cache does not keep Transform ids, so they are found by their node's name
const KEEP_UPRIGHT = /^ZeroRotation$/;

/** The white arrow the game prints on keyboard pickups (its "Symbol" glyph), as a texture per direction. */
function arrowTexture(angle, color = "#fff") {
  const c = document.createElement("canvas");
  c.width = c.height = 128;
  const g = c.getContext("2d");
  g.translate(64, 64); g.rotate(angle);
  g.strokeStyle = color; g.lineWidth = 10; g.lineCap = "round"; g.lineJoin = "round";
  g.beginPath(); g.moveTo(-34, 0); g.lineTo(32, 0); g.moveTo(10, -22); g.lineTo(34, 0); g.lineTo(10, 22); g.stroke();
  const t = new THREE.CanvasTexture(c);
  t.colorSpace = THREE.SRGBColorSpace;
  return t;
}

export async function createNotes(cache, rig) {
  const root = new THREE.Group();
  const keyTex = { right: arrowTexture(0), up: arrowTexture(-Math.PI / 2), left: arrowTexture(Math.PI), down: arrowTexture(Math.PI / 2) };
  const outlineMat = new THREE.MeshBasicMaterial({ color: 0xffffff, side: THREE.BackSide, fog: false });

  // one template per prefab, with the pickup's own colour (Pickup.color) for its outline and ground ring
  const templates = {};
  for (const [k, name] of Object.entries(PREFABS)) {
    const obj = cache.object(name), pickup = obj && obj.root.components.find((c) => c.class === "Pickup");
    const tint = /^(left|right|down|up|turnL|turnR)$/.test(k) && pickup ? pickup.fields.color : null;
    templates[k] = await cache.prefab(name, {
      key: "note-" + k, glow: /^beat/.test(k) ? BEAT_GLOW : NOTE_GLOW, tint,
      hide: (n) => AT_REST_HIDDEN.test(n), sprites: (n) => n === "Level Up Text",
    });
  }
  // the turns' guide arrows (crunched textures the preview cannot read) get a flat arrow in the turn colour
  for (const [k, angle] of [["turnL", Math.PI], ["turnR", 0]]) {
    templates[k].traverse((o) => {
      if (!o.isMesh || !/Turn .*Ghost/.test(o.parent ? o.parent.name : "")) return;
      for (const m of [].concat(o.material)) if (m && "map" in m && !m.map) { m.map = arrowTexture(angle, "#ffff54"); m.needsUpdate = true; }
    });
  }
  // the Freefly gate's spawn animation (Airpickup Spawn, 4 s; gates spawn 5 s ahead) leaves its inner mesh at 9.55, not 14
  const inside = templates.air.getObjectByName("Base Mesh Inside");
  if (inside) inside.scale.setScalar(9.55);

  const pools = Object.fromEntries(Object.keys(templates).map((k) => [k, []]));
  let used = {};

  /** A pooled copy of a prefab for this frame, in a holder group (place and turn on the path) with an inner one (lane offset). */
  function take(kind) {
    const pool = pools[kind], i = used[kind] = (used[kind] || 0) + 1;
    if (pool.length < i) {
      const holder = new THREE.Group(), inner = new THREE.Group(), obj = templates[kind].clone();
      inner.add(obj);
      holder.add(inner);
      const keep = [];
      obj.traverse((o) => { if (o !== obj && KEEP_UPRIGHT.test(o.name)) keep.push(o); });
      holder.userData = { inner, obj, outline: null, kind, keep };
      if (/^(left|right|down|up)$/.test(kind)) addIcon(obj, kind);
      root.add(holder);
      pool.push(holder);
    }
    const h = pool[i - 1];
    h.visible = true;
    return h;
  }

  function addIcon(obj, key) {
    const node = obj.getObjectByName("SymbolIcon");
    if (!node) return;
    const m = new THREE.Mesh(new THREE.PlaneGeometry(1, 1), new THREE.MeshBasicMaterial({ map: keyTex[key], transparent: true, depthWrite: false, side: THREE.DoubleSide }));
    m.scale.setScalar(0.6 * 4.5);
    node.add(m);
  }

  function setOutline(holder, on) {
    const u = holder.userData;
    if (on && !u.outline) {
      u.outline = new THREE.Group();
      u.obj.updateMatrixWorld(true);
      u.obj.traverse((o) => {
        // the solid parts only: an outline of the additive glows would be a white blob
        if (!o.isMesh || !o.visible || ![].concat(o.material).every((m) => m && m.blending === THREE.NormalBlending && !m.transparent)) return;
        let visible = true;
        for (let p = o; p && p !== u.obj; p = p.parent) if (!p.visible) visible = false;
        if (!visible) return;
        const copy = new THREE.Mesh(o.geometry, outlineMat);
        copy.matrixAutoUpdate = false;
        copy.matrix.copy(new THREE.Matrix4().copy(u.obj.matrixWorld).invert().multiply(o.matrixWorld)).multiply(new THREE.Matrix4().makeScale(1.06, 1.06, 1.06));
        u.outline.add(copy);
      });
      u.obj.add(u.outline);
    }
    if (u.outline) u.outline.visible = on;
  }

  let isSelected = () => false;

  /**
   * Puts a pooled object at straight distance d along the path, offset (x, y) across it, turned by roll; its upright
   * parts turn by `upright` (minus the tunnel's turn in List, none on the road).
   */
  function put(kind, path, d, x, y, roll, note, upright = 0) {
    const h = take(kind), f = path.frameAt(path.start + d);
    h.position.set(...f.p);
    h.quaternion.set(...qfromBasis(f.right, f.up, f.back));
    const u = h.userData;
    u.inner.rotation.set(0, 0, roll);
    for (const k of u.keep) k.rotation.set(0, 0, upright);
    u.obj.position.set(x, y, 0);
    if (u.kind === "turnL" || u.kind === "turnR") {
      u.obj.traverse((o) => { if (/Turn (Toblerone|Bounce) Ghost/.test(o.name)) o.visible = note.mode === "List" ? /Toblerone/.test(o.name) : /Bounce/.test(o.name); });
    }
    const levelUp = u.obj.getObjectByName("Level Up Text");
    if (levelUp) levelUp.visible = !!(note && note.levelUp);
    setOutline(h, !!(note && note.id !== undefined && isSelected(note)));
    return h;
  }

  return {
    root,
    prepare() {},
    setSelection(fn) { isSelected = fn || (() => false); },
    update(run, ship, path, t) {
      used = {};
      for (const pool of Object.values(pools)) for (const h of pool) h.visible = false;
      const spin = ship.spin(t); // the List tunnel's turn
      // beat lines, 1 s ahead (not in Freefly)
      const doc = run.doc, spb = 60 / doc.bpm;
      for (let k = Math.ceil((t - 0.2 - doc.firstBeat) / spb); ; k++) {
        const bt = doc.firstBeat + k * spb;
        if (bt > t + 1) break;
        const mode = run.modeAt(bt);
        if (mode === "Freefly") continue;
        put(mode === "List" ? "beatList" : "beatBounce", path, run.noteDist(bt, t), 0, rig.pickupY, mode === "List" ? spin : 0, null);
      }
      for (const n of run.visible(t)) {
        const ahead = n.mode === "Freefly" && !n.strum ? 5 : 3;
        if (n.strum && n.len > 0) {
          // a hold's end bar, where the hold ends
          const te = n.t + n.len;
          if (n.mode !== "Freefly" && te >= t - 0.15 && te <= t + 3) put(n.mode === "List" ? "endList" : "endBounce", path, run.noteDist(te, t), 0, 0, n.mode === "List" ? spin : 0, n);
        }
        if (n.t < t - 0.15 || n.t > t + ahead) continue;
        const d = run.noteDist(n.t, t);
        if (n.mode === "Freefly") {
          // GeneratorFreefly makes no gate for a strum; every other note is a ring at the level's fly-area point
          if (n.strum) continue;
          const a = ship.ringAt(n.t);
          put("air", path, d, a.px, a.py, 0, n);
          continue;
        }
        if (n.strum) {
          const list = n.mode === "List";
          put(n.len > 0 ? (list ? "holdList" : "holdBounce") : list ? "strumList" : "strumBounce", path, d, 0, 0, list ? spin : 0, n, list ? -spin : 0);
          continue;
        }
        const kind = isTurn(n.color) ? (n.color === 4 ? "turnL" : "turnR") : KEYS[n.color] || "left";
        const lane = n.lane ?? 1;
        if (n.mode === "List") put(kind, path, d, 0, rig.pickupY, spin + lane * THIRD, n, -spin);
        else put(kind, path, d, rig.pickupLaneX * (lane - 1), rig.pickupY, 0, n);
      }
    },
    dispose() {
      root.traverse((o) => { if (o.isMesh && o.material && o.material.map && Object.values(keyTex).includes(o.material.map)) o.material.dispose(); });
    },
  };
}

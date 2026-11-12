// What the ship's trails and the pickups' level halo look like, read from their prefabs in the game data cache.
//
// Trails (TrailRenderer) hang under the ship's OldRoot, grouped in sets that ShipPickupStreakSettings switches on:
// PickupStreakTier1-4 by score level, FreeFlyTier1-4 by rings taken in a Freefly stretch, FreeflyTrails while the
// ship is steered hard. A trail faces the camera (alignment View) or lies across the transform's z axis (TransformZ):
// across the ship's up it lies flat, across its right it stands upright, and one along the ship draws nothing.
// The halo (PickupStatesPickup.levelSettings) is a sprite per score level, scaled and placed above the pickup.
import { qmul, qrotate } from "./math.js";
import { findNode } from "./particlespec.js";

const vec = (o) => (Array.isArray(o) ? o : [o.x, o.y, o.z]);

/** The trails of a set node, with their attach point and facing in OldRoot's axes (Unity's). */
function trailsUnder(setNode, parentOf, oldRoot) {
  const frame = (node) => {
    let pos = [0, 0, 0], q = [0, 0, 0, 1];
    const chain = [];
    for (let n = node; n && n !== oldRoot; n = parentOf.get(n)) chain.unshift(n);
    for (const n of chain) {
      const p = qrotate(q, vec(n.position)); // the trail nodes under OldRoot are not scaled
      pos = [pos[0] + p[0], pos[1] + p[1], pos[2] + p[2]];
      q = qmul(q, n.rotation);
    }
    return { pos, q };
  };
  const out = [];
  const visit = (n, group) => {
    const tr = n.components.find((c) => c.type === "TrailRenderer");
    if (tr && tr.fields.m_Enabled !== 0) {
      const f = tr.fields, P = f.m_Parameters, keys = P.widthCurve.m_Curve, m = (f.m_Materials || [])[0] || {};
      const { pos, q } = frame(n), normal = qrotate(q, [0, 0, 1]);
      const align = P.alignment === 1 ? "normal" : "view";
      if (align === "view" || Math.abs(normal[2]) < 0.9) {
        out.push({
          name: n.name,
          group, // the set's own child the trail belongs to (its trails are often just called "Trail")
          p: pos,
          time: f.m_Time,
          w0: P.widthMultiplier * keys[0].value,
          w1: P.widthMultiplier * keys[keys.length - 1].value,
          align,
          normal,
          mat: {
            tex: m.textures && m.textures._MainTex ? m.textures._MainTex.ref : null,
            tint: (m.colors && m.colors._TintColor) || [1, 1, 1, 1],
            gain: (m.floats && m.floats._EmissionGain) || 0,
            fade: (m.floats && m.floats._FadeDistance) || 1,
          },
        });
      }
    }
    for (const c of n.children) visit(c, group || c.name);
  };
  visit(setNode, null);
  return out;
}

export function trailSets(shipRoot) {
  const parentOf = new Map(), byId = new Map();
  (function index(n, parent) { parentOf.set(n, parent); byId.set(n.id, n); for (const c of n.children) index(c, n); })(shipRoot, null);
  const oldRoot = findNode(shipRoot, "OldRoot");
  let settings = null;
  for (const n of byId.values()) {
    const c = n.components.find((x) => x.class === "ShipPickupStreakSettings");
    if (c) { settings = c.fields; break; }
  }
  const setsOf = (field) => {
    const list = field && field.singlePlayerList ? field.singlePlayerList : Array.isArray(field) ? field : [];
    return list.flatMap((r) => (r && byId.has(r.ref) ? trailsUnder(byId.get(r.ref), parentOf, oldRoot) : []));
  };
  if (!settings) return { streak: [[], [], [], []], freefly: [[], [], [], []], onSteer: [] };
  return {
    streak: [1, 2, 3, 4].map((i) => setsOf(settings["PickupStreakTier" + i])),
    freefly: [1, 2, 3, 4].map((i) => setsOf(settings["FreeFlyTier" + i])),
    onSteer: setsOf(settings.FreeflyTrails),
  };
}

/** The pickup's halo per score level 1-3 (sprite size in units, scale, place), with its material's tint and gain. */
export function haloLevels(pickupRoot) {
  const states = pickupRoot.components.find((c) => c.class === "PickupStatesPickup");
  const node = findNode(pickupRoot, "Level Pickup Effect");
  const renderer = node && node.components.find((c) => c.type === "SpriteRenderer");
  const mat = renderer && renderer.fields.m_Materials ? renderer.fields.m_Materials[0] || {} : {};
  const levels = (states ? states.fields.levelSettings || [] : []).map((l) => {
    const s = l.effectSprite || {}, rect = s.rect || [0, 0, 0, 0], ppu = s.pixelsPerUnit || 100;
    return {
      w: rect[2] / ppu,
      h: rect[3] / ppu,
      scale: l.effectScale ? l.effectScale.x : 1,
      pos: l.effectPosition ? vec(l.effectPosition) : [0, 0, 0],
      grey: l.effectColor ? l.effectColor.r : 1,
      tex: s.texture ? s.texture.ref : null,
      rect: s.rect,
    };
  });
  const tint = (mat.colors && mat.colors._TintColor) || [1, 1, 1, 1];
  return { levels, tint: tint.slice(0, 3), gain: (mat.floats && mat.floats._EmissionGain) || 0 };
}

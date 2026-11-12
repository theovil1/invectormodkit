// The track around the walker: the List tunnel's corner lines (and the lab's smoked walls), the Bounce road (the
// level's own road piece repeated along the path), and the lab decor (a floor far below, blocks and an orange arch
// every 4 bars). Built on the straight track (x across, y up, z = -distance), bent once along the level path, in
// chunks of 100 units of distance kept in a bounded cache. The chunks only depend on the sections, the beat grid and
// the perfect run's level changes, so most edits keep them.
import * as THREE from "three";

const CHUNK = 100;
const BEHIND = 2, AHEAD = 12, KEEP = 24;

function gridTexture(base, line, bold, text) {
  const c = document.createElement("canvas");
  c.width = c.height = 512;
  const g = c.getContext("2d");
  g.fillStyle = base; g.fillRect(0, 0, 512, 512);
  g.strokeStyle = line; g.lineWidth = 2;
  for (let i = 64; i < 512; i += 64) { g.beginPath(); g.moveTo(i, 0); g.lineTo(i, 512); g.moveTo(0, i); g.lineTo(512, i); g.stroke(); }
  g.strokeStyle = bold; g.lineWidth = 8; g.strokeRect(4, 4, 504, 504);
  if (text) { g.fillStyle = bold; g.font = "bold 44px Segoe UI, sans-serif"; g.fillText(text, 22, 64); }
  const t = new THREE.CanvasTexture(c);
  t.colorSpace = THREE.SRGBColorSpace;
  t.wrapS = t.wrapT = THREE.RepeatWrapping;
  t.anisotropy = 8;
  return t;
}

/** Collects straight-track geometry and bends it along the path into one geometry per material. */
class Bender {
  constructor(path) { this.path = path; this.parts = new Map(); }

  /** Adds a geometry (straight-track axes, after `matrix`) for a material. */
  add(geometry, material, matrix) {
    const g = geometry.index ? geometry.toNonIndexed() : geometry.clone();
    g.applyMatrix4(matrix);
    if (!this.parts.has(material)) this.parts.set(material, []);
    this.parts.get(material).push(g);
  }

  build() {
    const group = new THREE.Group();
    for (const [material, list] of this.parts) {
      let count = 0;
      for (const g of list) count += g.attributes.position.count;
      const pos = new Float32Array(count * 3), nrm = new Float32Array(count * 3), uv = new Float32Array(count * 2);
      let at = 0;
      for (const g of list) {
        const p = g.attributes.position, n = g.attributes.normal, u = g.attributes.uv;
        for (let i = 0; i < p.count; i++, at++) {
          const x = p.getX(i), y = p.getY(i), z = p.getZ(i), d = this.path.start - z, f = this.path.frameAt(d);
          for (let k = 0; k < 3; k++) {
            pos[at * 3 + k] = f.p[k] + f.right[k] * x + f.up[k] * y;
            if (n) nrm[at * 3 + k] = f.right[k] * n.getX(i) + f.up[k] * n.getY(i) + f.back[k] * n.getZ(i);
          }
          if (u) { uv[at * 2] = u.getX(i); uv[at * 2 + 1] = u.getY(i); }
        }
        g.dispose();
      }
      const geo = new THREE.BufferGeometry();
      geo.setAttribute("position", new THREE.BufferAttribute(pos, 3));
      geo.setAttribute("normal", new THREE.BufferAttribute(nrm, 3));
      geo.setAttribute("uv", new THREE.BufferAttribute(uv, 2));
      geo.computeBoundingSphere();
      const mesh = new THREE.Mesh(geo, material);
      group.add(mesh);
    }
    return group;
  }
}

/** A seeded random sequence per beat, so the lab's blocks are the same on every build. */
function rnd(seed) { let s = (seed * 7919 + 3) % 2147483647 || 1; return () => (s = (s * 16807) % 2147483647) / 2147483647; }

export async function createTrack(cache, rig, path, level) {
  const road = level.road || null;
  const tex = async (k) => (road && road.material && road.material.textures && road.material.textures[k] ? cache.texture(road.material.textures[k].ref) : null);
  const pieceGeo = road && road.mesh && road.mesh.ref ? await cache.geometry(road.mesh.ref) : null;
  const occlusion = await tex("_OcclusionMap"), emission = await tex("_EmissionMap");
  const glow = (road && road.material && road.material.colors && road.material.colors._EmissionColor) || [0.627, 0.251, 1];
  const roadScale = road && road.scale ? road.scale.x : 13, roadY = road && typeof road.offsetY === "number" ? road.offsetY : rig.roadY;
  let pieceLength = 1.1554 * roadScale;
  if (pieceGeo) { pieceGeo.computeBoundingBox(); const b = pieceGeo.boundingBox; pieceLength = (b.max.z - b.min.z) * roadScale; }
  // the road: its vinyl occlusion, dark, and its emission map in the material's HDR emission colour, added
  const roadDark = new THREE.MeshBasicMaterial({ map: occlusion, color: new THREE.Color(0.025, 0.025, 0.0275), side: THREE.DoubleSide });
  const roadGlow = new THREE.MeshBasicMaterial({ map: emission, color: new THREE.Color(glow[0], glow[1], glow[2]), blending: THREE.AdditiveBlending, transparent: true, depthWrite: false, side: THREE.DoubleSide });
  // HDR white: with the game's bloom threshold (2) the corners sit well above it to glow as in the game
  const corner = new THREE.MeshBasicMaterial({ color: new THREE.Color(8, 8, 8) });
  // the lab only (not the game): a light smoked glass so the white corners still read against the bright decor
  const smoke = new THREE.MeshBasicMaterial({ color: new THREE.Color(0.02, 0.02, 0.03), side: THREE.DoubleSide, transparent: true, opacity: 0.5, depthWrite: false, fog: false });
  const labMat = (t) => new THREE.MeshStandardMaterial({ map: t, roughness: 0.9 });
  const floorTex = gridTexture("#bfc3c7", "#a9adb2", "#8d9196", "4 m");
  floorTex.repeat.set(45, 25);
  const floor = labMat(floorTex), grey = labMat(gridTexture("#bfc3c7", "#a9adb2", "#8d9196", "4 m"));
  const orange = labMat(gridTexture("#f08a24", "#d9761a", "#b85e0e", "")), dark = labMat(gridTexture("#5d6167", "#52565b", "#3f4247", ""));
  const tri = rig.tri.map(([x, y]) => new THREE.Vector2(x, y));

  let run = null, beats = [], key = "";
  const chunks = new Map(); // index -> { group, used }
  const root = new THREE.Group();
  let stamp = 0;

  const m4 = (x, y, z, sx = 1, sy = 1, sz = 1, rz = 0, rx = 0) => {
    const m = new THREE.Matrix4().compose(new THREE.Vector3(x, y, z), new THREE.Quaternion().setFromEuler(new THREE.Euler(rx, 0, rz, "ZXY")), new THREE.Vector3(sx, sy, sz));
    return m;
  };

  function sectionsIn(s0, s1) {
    return run.sections
      .map((s) => ({ mode: s.mode, a: Math.max(s0, run.distAt(Math.max(0, s.from))), b: Math.min(s1, run.distAt(s.to)) }))
      .filter((s) => s.b > s.a);
  }

  function buildChunk(c) {
    const s0 = c * CHUNK, s1 = s0 + CHUNK, bend = new Bender(path);
    for (const sec of sectionsIn(s0, s1)) {
      const len = sec.b - sec.a, mid = -(sec.a + sec.b) / 2, segs = Math.max(1, Math.ceil(len / 3));
      if (sec.mode === "List") {
        for (const [p, q] of [[tri[1], tri[2]], [tri[2], tri[0]], [tri[0], tri[1]]]) {
          const w = p.distanceTo(q), cx = (p.x + q.x) / 2, cy = (p.y + q.y) / 2, ang = Math.atan2(q.y - p.y, q.x - p.x);
          bend.add(new THREE.PlaneGeometry(w, len, 1, segs), smoke, m4(cx, cy, mid, 1, 1, 1, ang, Math.PI / 2));
        }
        for (const p of tri) bend.add(new THREE.BoxGeometry(0.12, 0.12, len, 1, 1, segs), corner, m4(p.x * 0.985, p.y * 0.985, mid));
      } else if (sec.mode === "Bounce") {
        const geo = pieceGeo || new THREE.PlaneGeometry(1.16, 1.16).rotateX(-Math.PI / 2);
        for (let s = Math.ceil(sec.a / pieceLength) * pieceLength; s < sec.b; s += pieceLength) {
          bend.add(geo, roadDark, m4(0, roadY, -s, roadScale, roadScale, roadScale));
          bend.add(geo, roadGlow, m4(0, roadY + 0.01, -s, roadScale, roadScale, roadScale));
        }
      }
    }
    // the lab: a floor strip far below, blocks every other beat, an arch every 4 bars
    bend.add(new THREE.PlaneGeometry(180, CHUNK, 1, 20), floor, m4(0, -22, -(s0 + s1) / 2, 1, 1, 1, 0, -Math.PI / 2));
    for (const b of beats) {
      if (b.s < s0 || b.s >= s1) continue;
      const r = rnd(b.k + 1);
      if (b.k % 2 === 0) for (const side of [-1, 1]) if (r() < 0.7) {
        const w = 6 + r() * 16, h = 4 + r() * 26, d = 6 + r() * 16;
        bend.add(new THREE.BoxGeometry(w, h, d, 1, 1, Math.max(1, Math.ceil(d / 4))), r() < 0.15 ? dark : grey, m4(side * (30 + r() * 60), -22 + h / 2, -(b.s + r() * 20)));
      }
      if (b.k % 16 === 0) {
        for (const x of [-24, 24]) bend.add(new THREE.BoxGeometry(2.4, 44, 2.4), orange, m4(x, 0, -b.s));
        bend.add(new THREE.BoxGeometry(50.4, 2.4, 2.4, 8), orange, m4(0, 22, -b.s));
      }
    }
    return bend.build();
  }

  function free(entry) {
    root.remove(entry.group);
    entry.group.traverse((o) => { if (o.geometry) o.geometry.dispose(); });
  }

  return {
    root,
    /** A new chart: the chunks stay when the sections, the beat grid and the level changes are the same. */
    prepare(newRun, doc) {
      run = newRun;
      const k = JSON.stringify([run.sections, run.levels, doc.bpm, doc.firstBeat, doc.end]);
      if (k === key) return;
      key = k;
      for (const e of chunks.values()) free(e);
      chunks.clear();
      const spb = 60 / doc.bpm, end = doc.end + 10;
      beats = [];
      for (let k2 = Math.ceil((0 - doc.firstBeat) / spb); ; k2++) {
        const t = doc.firstBeat + k2 * spb;
        if (t > end) break;
        if (t >= 0) beats.push({ k: k2, s: run.distAt(t) });
      }
    },
    /** Shows the chunks around the walker at song time t, building the missing ones. */
    update(t) {
      if (!run) return;
      const c0 = Math.floor(run.distAt(t) / CHUNK);
      stamp++;
      for (let c = c0 - BEHIND; c <= c0 + AHEAD; c++) {
        let e = chunks.get(c);
        if (!e) { e = { group: buildChunk(c) }; chunks.set(c, e); root.add(e.group); }
        e.used = stamp;
        e.group.visible = true;
      }
      for (const [c, e] of chunks) if (e.used !== stamp) e.group.visible = false;
      if (chunks.size > KEEP) {
        const old = [...chunks.entries()].filter(([, e]) => e.used !== stamp).sort((a, b) => a[1].used - b[1].used);
        for (const [c, e] of old.slice(0, chunks.size - KEEP)) { free(e); chunks.delete(c); }
      }
    },
    dispose() {
      for (const e of chunks.values()) free(e);
      chunks.clear();
    },
  };
}

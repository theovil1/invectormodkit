// The game-like view around the playhead, in WebGL: the triangular tunnel (List: the lanes are its three faces), the
// neon road (Bounce: left, centre, right) and the open sky (Freefly: rings at the bottom, top left and top right),
// with the pickups coming in, the ship on the lane it is heading for, and the game's glow (bloom).
// The game's own pictures (sprites, and the 3D pickups and ship the mod photographs in game) are used when the skin
// has them; drawn stand-ins otherwise, so the view works without them.
import * as THREE from "three";
import { EffectComposer } from "./vendor/three/addons/postprocessing/EffectComposer.js";
import { RenderPass } from "./vendor/three/addons/postprocessing/RenderPass.js";
import { UnrealBloomPass } from "./vendor/three/addons/postprocessing/UnrealBloomPass.js";
import { OutputPass } from "./vendor/three/addons/postprocessing/OutputPass.js";
import { MODE_COLOR, KEY_COLOR, ACTION, keySprite } from "./look.js";

/** World units per second of song: notes are laid along -z at this speed. */
export const SPEED = 26;
const AHEAD = 4.2; // seconds of chart in view
const R_IN = 4.6; // List: distance from the tunnel's axis to a face
const ROAD_HALF = 7.4; // Bounce: half the road's width
const LANE_X = 4.5; // Bounce: lane spacing (the game's)
const FLY = [[0, -2.6], [-5.2, 5.2], [5.2, 5.2]]; // Freefly: the game's lane positions (bottom, top left, top right), scaled
const FLY_Y = 5; // height of the flight area's centre


const isTurn = (c) => c === 4 || c === 5;
const Z = (t) => -t * SPEED;
const smooth = (x) => (x <= 0 ? 0 : x >= 1 ? 1 : x * x * (3 - 2 * x));
const hdr = (hex, k) => new THREE.Color(hex).multiplyScalar(k); // brighter than white: it glows

function modeAt(doc, t) {
  let m = doc.sections[0].mode;
  for (const s of doc.sections) if (t >= s.t) m = s.mode;
  return m;
}

/** List: angle (radians) of the centre of face `lane`, seen down the tunnel. Lane 1 is the right wall, 2 the left. */
const faceAngle = (lane) => -Math.PI / 2 + (lane * 2 * Math.PI) / 3;

// ---------------------------------------------------------------------------------------------------- drawn textures

function canvasTexture(w, h, draw) {
  const c = document.createElement("canvas");
  c.width = w; c.height = h;
  draw(c.getContext("2d"), w, h);
  const t = new THREE.CanvasTexture(c);
  t.colorSpace = THREE.SRGBColorSpace;
  return t;
}

const drawn = {
  Glow: () => canvasTexture(128, 128, (g, w) => {
    const r = g.createRadialGradient(w / 2, w / 2, 0, w / 2, w / 2, w / 2);
    r.addColorStop(0, "rgba(255,255,255,1)"); r.addColorStop(0.35, "rgba(255,255,255,.35)"); r.addColorStop(1, "rgba(255,255,255,0)");
    g.fillStyle = r; g.fillRect(0, 0, w, w);
  }),
  PickupArrow_Emissive_PickupRingLeftRight: () => canvasTexture(240, 190, (g) => {
    g.strokeStyle = "#fff"; g.lineWidth = 16; g.lineJoin = "round";
    g.beginPath(); g.moveTo(20, 95); g.quadraticCurveTo(110, 5, 200, 20); g.quadraticCurveTo(235, 95, 200, 170); g.quadraticCurveTo(110, 185, 20, 95); g.stroke();
  }),
  PickupStrum_Air_Activate: () => canvasTexture(256, 256, (g) => {
    g.strokeStyle = "#fff"; g.lineWidth = 8; g.shadowColor = "#fff"; g.shadowBlur = 18;
    g.beginPath(); g.arc(128, 128, 90, 0, Math.PI * 2); g.stroke();
  }),
  StrumHoldEndGlowline: () => canvasTexture(256, 64, (g, w, h) => {
    const l = g.createLinearGradient(0, 0, 0, h);
    l.addColorStop(0, "rgba(255,255,255,1)"); l.addColorStop(0.15, "rgba(255,255,255,.5)"); l.addColorStop(1, "rgba(255,255,255,0)");
    g.fillStyle = l; g.fillRect(0, 0, w, h);
  }),
  BoostGlow: () => canvasTexture(128, 128, (g) => {
    for (let i = 0; i < 70; i++) {
      const a = i * 2.39996, r = 8 + ((i * 37) % 50), s = 1 + (i % 4);
      g.fillStyle = `rgba(255,255,255,${0.4 + (i % 3) * 0.2})`;
      g.beginPath(); g.arc(64 + Math.cos(a) * r, 64 + Math.sin(a) * r, s, 0, Math.PI * 2); g.fill();
    }
  }),
  Strum_R_Generic: () => keycap("R"),
  // keyboard keys, as the game's keycaps
  Keyboard_LeftArrow: () => keycap("←"), Keyboard_RightArrow: () => keycap("→"),
  Keyboard_DownArrow: () => keycap("↓"), Keyboard_UpArrow: () => keycap("↑"),
  Keyboard_Space: () => canvasTexture(96, 60, (g) => {
    g.fillStyle = "#fff"; g.beginPath(); g.roundRect(4, 4, 88, 50, 8); g.fill();
    g.fillStyle = "#000"; g.fillRect(28, 34, 40, 5);
  }),
};

function keycap(glyph) {
  return canvasTexture(64, 64, (g) => {
    g.fillStyle = "#fff"; g.beginPath(); g.roundRect(4, 4, 56, 52, 8); g.fill();
    g.fillStyle = "#000"; g.font = "bold 38px 'Segoe UI Symbol', sans-serif"; g.textAlign = "center"; g.textBaseline = "middle";
    g.fillText(glyph, 32, 31);
  });
}

/** Speed streaks on a dark surface, like the game's track. */
function streaks(seed) {
  return canvasTexture(256, 512, (g, w, h) => {
    const base = g.createLinearGradient(0, 0, w, 0);
    base.addColorStop(0, "#3a3a46"); base.addColorStop(0.5, "#24242e"); base.addColorStop(1, "#3a3a46");
    g.fillStyle = base; g.fillRect(0, 0, w, h);
    let s = seed;
    const rnd = () => ((s = (s * 16807) % 2147483647) / 2147483647);
    for (let i = 0; i < 300; i++) {
      const x = rnd() * w, y = rnd() * h, len = 30 + rnd() * 220, a = 0.06 + rnd() * 0.3;
      g.fillStyle = `rgba(255,255,255,${a})`;
      g.fillRect(x, y, rnd() < 0.85 ? 1 : 2, len);
      g.fillRect(x, y - h, 1, len); // wraps vertically
    }
  });
}

function chevron(dir) {
  return canvasTexture(256, 192, (g, w, h) => {
    g.fillStyle = "#CB33FF"; g.beginPath(); g.roundRect(8, 8, w - 16, h - 16, 22); g.fill();
    g.strokeStyle = "#fff"; g.lineWidth = 20; g.lineCap = "round"; g.lineJoin = "round";
    for (const off of [-46, 46]) {
      const cx = w / 2 + off, a = 46;
      g.beginPath(); g.moveTo(cx - dir * a * 0.7, h / 2 - a); g.lineTo(cx + dir * a * 0.7, h / 2); g.lineTo(cx - dir * a * 0.7, h / 2 + a); g.stroke();
    }
  });
}

// --------------------------------------------------------------------------------------------------------- the view

export class Preview3D {
  /**
   * @param {HTMLCanvasElement} canvas
   * @param {{ skin?: string, manifest?: object }} options skin: base URL of the editor skin (sprites/…, models/…,
   *   ships/…, backgrounds/…); manifest: its manifest.json, when there is one.
   */
  constructor(canvas, { skin = null, manifest = null } = {}) {
    this.skin = skin;
    this.manifest = manifest || {};
    this.textures = new Map();
    this.renderer = new THREE.WebGLRenderer({ canvas, antialias: true, powerPreference: "high-performance" });
    this.renderer.setPixelRatio(Math.min(1.5, window.devicePixelRatio || 1));
    this.renderer.toneMapping = THREE.ACESFilmicToneMapping;
    this.renderer.toneMappingExposure = 1.05;

    this.scene = new THREE.Scene();
    this.scene.fog = new THREE.FogExp2(0x120a24, 0.0105);
    this.scene.background = new THREE.Color(0x07040e);
    this.camera = new THREE.PerspectiveCamera(62, 16 / 9, 0.1, 900);
    this.scene.add(this.camera);
    this.scene.add(new THREE.HemisphereLight(0x9a70ff, 0x10081c, 1.4));
    const key = new THREE.DirectionalLight(0xffd0ff, 1.6);
    key.position.set(-3, 6, 8);
    this.camera.add(key); // lights the ship from the camera's side
    this.camera.add(key.target);

    this.composer = new EffectComposer(this.renderer);
    this.composer.addPass(new RenderPass(this.scene, this.camera));
    this.bloom = new UnrealBloomPass(new THREE.Vector2(512, 512), 0.85, 0.45, 0.72);
    this.composer.addPass(this.bloom);
    this.composer.addPass(new OutputPass());

    this.world = new THREE.Group(); // the track and its scenery (rebuilt when the zones or the tempo change)
    this.notes = new THREE.Group(); // the pickups (rebuilt on every edit)
    this.scene.add(this.world, this.notes);
    this.addSky();
    this.ship = this.makeShip();
    this.scene.add(this.ship);
    this.doc = null;
    this.items = [];
    this.selected = () => false;
  }

  // ------------------------------------------------------------------------------------------------------ textures

  url(path) { return this.skin ? this.skin + path : null; }

  /** A skin picture as a texture: its drawn stand-in at once, the game's picture as soon as it has loaded. */
  tex(name, file = "sprites/" + name.replace(/ /g, "_") + ".png") {
    if (this.textures.has(file)) return this.textures.get(file);
    const key = name.replace(/ /g, "_");
    const t = drawn[key] ? drawn[key]() : canvasTexture(4, 4, (g) => { g.fillStyle = "#fff"; g.fillRect(0, 0, 4, 4); });
    const url = this.url(file);
    if (url) {
      const image = new Image();
      image.onload = () => { t.image = image; t.needsUpdate = true; };
      image.src = url;
    }
    this.textures.set(file, t);
    return t;
  }

  /** The mod's photograph of a 3D pickup (models/…) or of the ship (ships/…), when it took one. */
  photo(entry) {
    if (!entry || !this.skin) return null;
    const t = this.tex(entry.file, entry.file);
    return { texture: t, w: entry.w * entry.upp, h: entry.h * entry.upp, pivot: entry.pivot, wpx: entry.w, hpx: entry.h };
  }

  material(texture, color, { additive = true, opacity = 1 } = {}) {
    return new THREE.MeshBasicMaterial({
      map: texture, color, transparent: true, opacity, depthWrite: false, side: THREE.DoubleSide,
      blending: additive ? THREE.AdditiveBlending : THREE.NormalBlending, fog: true,
    });
  }

  plane(texture, w, h, color, options) {
    return new THREE.Mesh(new THREE.PlaneGeometry(w, h), this.material(texture, color, options));
  }

  // ------------------------------------------------------------------------------------------------- sky and stars

  addSky() {
    // a dome: dark above and below, the zone's colour along the horizon
    this.skyUniforms = { horizon: { value: new THREE.Color("#3a1460") }, top: { value: new THREE.Color("#030108") } };
    const dome = new THREE.Mesh(new THREE.SphereGeometry(700, 32, 16), new THREE.ShaderMaterial({
      side: THREE.BackSide, depthWrite: false, fog: false, uniforms: this.skyUniforms,
      vertexShader: "varying vec3 vDir; void main() { vDir = normalize(position); gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0); }",
      fragmentShader: "uniform vec3 horizon; uniform vec3 top; varying vec3 vDir; void main() { float h = abs(vDir.y); gl_FragColor = vec4(mix(horizon, top, smoothstep(0.0, 0.55, h)), 1.0); }",
    }));
    dome.renderOrder = -10;
    this.dome = dome;
    this.scene.add(dome);
    // stars all around, following the camera
    const n = 1400, pos = new Float32Array(n * 3), col = new Float32Array(n * 3);
    for (let i = 0; i < n; i++) {
      const u = Math.random() * 2 - 1, a = Math.random() * Math.PI * 2, r = 260 + Math.random() * 260;
      const s = Math.sqrt(1 - u * u);
      pos.set([Math.cos(a) * s * r, u * r, Math.sin(a) * s * r], i * 3);
      const c = new THREE.Color().setHSL(0.72 + Math.random() * 0.2, 0.6, 0.6 + Math.random() * 0.4).multiplyScalar(1 + Math.random() * 1.6);
      col.set([c.r, c.g, c.b], i * 3);
    }
    const g = new THREE.BufferGeometry();
    g.setAttribute("position", new THREE.BufferAttribute(pos, 3));
    g.setAttribute("color", new THREE.BufferAttribute(col, 3));
    // points get their own soft dot, without mipmaps (a far, tiny point would otherwise sample a flat square)
    const dot = drawn.Glow();
    dot.generateMipmaps = false;
    dot.minFilter = THREE.LinearFilter;
    this.stars = new THREE.Points(g, new THREE.PointsMaterial({
      size: 2.2, map: dot, vertexColors: true, transparent: true, depthWrite: false,
      blending: THREE.AdditiveBlending, fog: false, sizeAttenuation: true,
    }));
    // nebulae far away
    const glow = this.tex("Glow");
    [["#7A2CFF", -160, 60, -420, 520], ["#FF3FA8", 220, 20, -460, 380], ["#19C8E6", 40, -120, -500, 420], ["#5A1CC0", -320, -40, -300, 360]].forEach(([c, x, y, z, size]) => {
      const n = this.plane(glow, size, size * 0.6, hdr(c, 0.07));
      n.material.fog = false;
      n.position.set(x, y, z);
      this.stars.add(n);
    });
    this.scene.add(this.stars);
    // dust drifting by, close to the track
    const m = 500, dust = new Float32Array(m * 3);
    for (let i = 0; i < m; i++) dust.set([(Math.random() - 0.5) * 70, (Math.random() - 0.3) * 40, -Math.random() * 160], i * 3);
    const dg = new THREE.BufferGeometry();
    dg.setAttribute("position", new THREE.BufferAttribute(dust, 3));
    this.dust = new THREE.Points(dg, new THREE.PointsMaterial({
      size: 0.28, map: dot, color: hdr("#C9A6FF", 1.2), transparent: true, depthWrite: false,
      blending: THREE.AdditiveBlending,
    }));
    this.scene.add(this.dust);
  }

  // ---------------------------------------------------------------------------------------------------------- ship

  makeShip() {
    const ship = new THREE.Group();
    ship.userData.forms = {};
    // the photographs the mod took of the selected ship, by zone
    const id = this.manifest.ship, shots = id && this.manifest.ships ? this.manifest.ships[id] : null;
    if (shots) {
      for (const mode of ["List", "Bounce", "Freefly"]) {
        const p = this.photo(shots[mode + "-back"]);
        if (!p) continue;
        const scale = 3.6 / Math.max(p.w, 0.001); // the ship is about 3.6 units wide on screen
        // below the glow threshold: the game's (often white) hull stays a hull, its lights still shine
        const mesh = this.plane(p.texture, p.w * scale, p.h * scale, new THREE.Color(0.6, 0.6, 0.62), { additive: false });
        mesh.position.y = (p.h * scale) / 2 - 0.3;
        mesh.visible = false;
        ship.add(mesh);
        ship.userData.forms[mode] = mesh;
      }
    }
    if (Object.keys(ship.userData.forms).length === 0) ship.add(this.drawnShip());
    // its timing ring under it, as in game
    const ring = this.plane(this.tex("ShipTimingIndicator"), 3.4, 1.7, hdr("#ffffff", 0.7));
    ring.rotation.x = -Math.PI / 2.3;
    ring.position.set(0, -0.15, -0.6);
    ship.add(ring);
    return ship;
  }

  /** A low-poly dart with glowing orange edges and engines, close to the game's first ship. */
  drawnShip() {
    const g = new THREE.Group();
    const v = (x, y, z) => new THREE.Vector3(x, y, z);
    const hull = new THREE.BufferGeometry().setFromPoints([
      // top
      v(0, 0.12, -2.3), v(-0.55, 0.38, 0.5), v(0, 0.62, 0.9), v(0, 0.12, -2.3), v(0, 0.62, 0.9), v(0.55, 0.38, 0.5),
      // wings
      v(-0.45, 0.2, -0.6), v(-1.9, -0.05, 0.9), v(-0.55, 0.38, 0.5), v(0.45, 0.2, -0.6), v(0.55, 0.38, 0.5), v(1.9, -0.05, 0.9),
      // belly
      v(0, 0.12, -2.3), v(0, -0.25, 0.8), v(-0.55, 0.38, 0.5), v(0, 0.12, -2.3), v(0.55, 0.38, 0.5), v(0, -0.25, 0.8),
      // back
      v(-0.55, 0.38, 0.5), v(0, -0.25, 0.8), v(0, 0.62, 0.9), v(0.55, 0.38, 0.5), v(0, 0.62, 0.9), v(0, -0.25, 0.8),
    ]);
    hull.computeVertexNormals();
    const body = new THREE.Mesh(hull, new THREE.MeshStandardMaterial({ color: 0x2a2238, metalness: 0.55, roughness: 0.35, flatShading: true, side: THREE.DoubleSide }));
    g.add(body);
    const edges = new THREE.LineSegments(new THREE.EdgesGeometry(hull, 20), new THREE.LineBasicMaterial({ color: hdr("#FF9A3C", 2.6) }));
    g.add(edges);
    // side pods with their lights
    for (const side of [-1, 1]) {
      const pod = new THREE.Mesh(new THREE.BoxGeometry(0.34, 0.26, 1.5), new THREE.MeshStandardMaterial({ color: 0x3a2f52, metalness: 0.5, roughness: 0.4 }));
      pod.position.set(side * 1.35, 0.08, 0.35);
      pod.rotation.z = side * 0.35;
      g.add(pod);
      const strip = new THREE.Mesh(new THREE.BoxGeometry(0.08, 0.06, 1.3), new THREE.MeshBasicMaterial({ color: hdr("#FF7A1A", 3.2) }));
      strip.position.set(side * 1.42, 0.24, 0.3);
      strip.rotation.z = side * 0.35;
      g.add(strip);
      const engine = this.plane(this.tex("Glow"), 0.9, 0.9, hdr("#FF8A2C", 1.8));
      engine.position.set(side * 1.3, 0.1, 1.15);
      g.add(engine);
    }
    const core = this.plane(this.tex("Glow"), 1.0, 0.7, hdr("#FFB060", 1.8));
    core.position.set(0, 0.25, 1.0);
    g.add(core);
    const fin = new THREE.Mesh(new THREE.BoxGeometry(0.06, 0.08, 1.6), new THREE.MeshBasicMaterial({ color: hdr("#FFC070", 3) }));
    fin.position.set(0, 0.55, 0.1);
    fin.rotation.x = -0.12;
    g.add(fin);
    g.scale.setScalar(0.95);
    return g;
  }

  // -------------------------------------------------------------------------------------------------------- chart

  /** Shows `doc` (the editor's document, or a chart file's content) at difficulty `diff`. */
  setChart(doc, diff) {
    const shape = JSON.stringify([doc.bpm, doc.firstBeat, doc.end, doc.sections]);
    if (shape !== this.shape) {
      this.shape = shape;
      this.doc = doc;
      this.buildWorld();
    }
    this.doc = doc;
    this.diff = diff;
    this.buildNotes();
  }

  /** Which notes are drawn as selected. */
  setSelection(isSelected) {
    this.selected = isSelected || (() => false);
    for (const item of this.items) item.mark.visible = !!this.selected(item.note);
  }

  clear(group) {
    for (const child of [...group.children]) {
      group.remove(child);
      child.traverse((o) => {
        if (o.geometry) o.geometry.dispose();
        if (o.material) {
          if (o.material.userData.ownMap) o.material.map.dispose();
          o.material.dispose();
        }
      });
    }
  }

  zones() {
    const doc = this.doc;
    return doc.sections.map((s, i) => ({ mode: s.mode, from: s.t, to: i + 1 < doc.sections.length ? doc.sections[i + 1].t : doc.end }));
  }

  buildWorld() {
    this.clear(this.world);
    const doc = this.doc, spb = 60 / doc.bpm;
    const zones = this.zones();
    zones.forEach((zone, i) => {
      if (zone.to <= zone.from) return;
      // the first zone starts behind the camera, the last one runs on into the distance
      const z0 = Z(zone.from) + (i === 0 ? 40 : 0), z1 = Z(zone.to) - (i === zones.length - 1 ? 220 : 0);
      const length = z0 - z1, mid = (z0 + z1) / 2;
      const color = MODE_COLOR[zone.mode];
      const beats = [];
      for (let k = Math.ceil((zone.from - doc.firstBeat) / spb - 1e-6); ; k++) {
        const t = doc.firstBeat + k * spb;
        if (t >= zone.to) break;
        beats.push({ t, bar: ((k % 4) + 4) % 4 === 0 });
      }
      if (zone.mode === "List") this.buildTunnel(z0, length, mid, color, beats);
      else if (zone.mode === "Bounce") this.buildRoad(z0, length, mid, color, beats);
      else this.buildSky(zone, color, beats);
      // the gate into the zone
      if (i > 0) this.buildGate(Z(zone.from), zone.mode, color);
    });
  }

  surface(width, length, color, seed) {
    const t = streaks(seed);
    t.wrapS = t.wrapT = THREE.RepeatWrapping;
    t.repeat.set(1, length / 22);
    const m = new THREE.MeshBasicMaterial({ map: t, color: new THREE.Color(color).multiplyScalar(0.62), side: THREE.DoubleSide });
    m.userData.ownMap = true;
    return new THREE.Mesh(new THREE.PlaneGeometry(width, length), m);
  }

  /** Thin glowing bars: one InstancedMesh per colour. */
  bars(list, color, k) {
    if (!list.length) return null;
    const mesh = new THREE.InstancedMesh(new THREE.BoxGeometry(1, 1, 1), new THREE.MeshBasicMaterial({ color: hdr(color, k) }), list.length);
    const m = new THREE.Matrix4(), q = new THREE.Quaternion(), e = new THREE.Euler();
    list.forEach((b, i) => {
      q.setFromEuler(e.set(b.rx || 0, b.ry || 0, b.rz || 0));
      m.compose(new THREE.Vector3(b.x, b.y, b.z), q, new THREE.Vector3(b.sx, b.sy, b.sz));
      mesh.setMatrixAt(i, m);
    });
    return mesh;
  }

  buildTunnel(z0, length, mid, color, beats) {
    const side = 2 * R_IN * Math.tan(Math.PI / 3); // a face's width
    for (let lane = 0; lane < 3; lane++) {
      const a = faceAngle(lane);
      const face = this.surface(side, length, color, 7 + lane);
      face.rotation.order = "ZXY";
      face.rotation.z = a + Math.PI / 2;
      face.rotation.x = -Math.PI / 2;
      face.position.set(Math.cos(a) * R_IN, Math.sin(a) * R_IN, mid);
      this.world.add(face);
    }
    // the three corners, glowing all along
    const R = R_IN * 2, corners = [], ribs = [], ribsBar = [];
    for (let c = 0; c < 3; c++) {
      const a = faceAngle(c) + Math.PI / 3; // a corner sits between two faces
      corners.push({ x: Math.cos(a) * R * 0.97, y: Math.sin(a) * R * 0.97, z: mid, sx: 0.16, sy: 0.16, sz: length });
    }
    // a glowing triangle on every beat (brighter on bars), like the beat lines
    for (const b of beats) {
      for (let lane = 0; lane < 3; lane++) {
        const a = faceAngle(lane);
        (b.bar ? ribsBar : ribs).push({ x: Math.cos(a) * (R_IN - 0.02), y: Math.sin(a) * (R_IN - 0.02), z: Z(b.t), sx: side, sy: 0.07, sz: b.bar ? 0.22 : 0.1, rz: a + Math.PI / 2 });
      }
    }
    for (const mesh of [this.bars(corners, color, 3.2), this.bars(ribsBar, color, 2.4), this.bars(ribs, "#ffffff", 0.28)]) if (mesh) this.world.add(mesh);
  }

  buildRoad(z0, length, mid, color, beats) {
    const road = this.surface(ROAD_HALF * 2, length, color, 3);
    road.rotation.x = -Math.PI / 2;
    road.position.set(0, 0, mid);
    this.world.add(road);
    const edges = [-1, 1].map((s) => ({ x: s * ROAD_HALF, y: 0.08, z: mid, sx: 0.18, sy: 0.12, sz: length }));
    const lanes = [-0.5, 0.5].map((s) => ({ x: s * LANE_X, y: 0.02, z: mid, sx: 0.05, sy: 0.02, sz: length }));
    const lines = beats.map((b) => ({ x: 0, y: 0.03, z: Z(b.t), sx: ROAD_HALF * 2, sy: 0.02, sz: b.bar ? 0.24 : 0.08, bar: b.bar }));
    // neon frames and slanted bars along the road, as in the game's worlds
    const frames = [], slants = [];
    let s = 11;
    const rnd = () => ((s = (s * 16807) % 2147483647) / 2147483647);
    for (const b of beats.filter((x) => x.bar)) {
      if (rnd() < 0.55) {
        const sideX = (rnd() < 0.5 ? -1 : 1) * (14 + rnd() * 16), w = 5 + rnd() * 8, h = 6 + rnd() * 12, z = Z(b.t) - rnd() * 10, y = -2 + rnd() * 6;
        frames.push({ x: sideX, y: y + h, z, sx: w, sy: 0.18, sz: 0.18 }, { x: sideX, y, z, sx: w, sy: 0.18, sz: 0.18 },
          { x: sideX - w / 2, y: y + h / 2, z, sx: 0.18, sy: h, sz: 0.18 }, { x: sideX + w / 2, y: y + h / 2, z, sx: 0.18, sy: h, sz: 0.18 });
      }
      if (rnd() < 0.18) for (const sx of [-1, 1]) slants.push({ x: sx * (ROAD_HALF + 4), y: 7, z: Z(b.t), sx: 1.1, sy: 15, sz: 0.3, rz: sx * 0.38 });
    }
    for (const mesh of [this.bars(edges, "#C060FF", 2.6), this.bars(lanes, color, 0.9), this.bars(lines.filter((l) => l.bar), color, 1.6),
      this.bars(lines.filter((l) => !l.bar), "#ffffff", 0.2), this.bars(frames, color, 2.6), this.bars(slants, "#E8F8FF", 2.4)]) if (mesh) this.world.add(mesh);
  }

  buildSky(zone, color, beats) {
    // floating rocks around the flight path, lit from above
    let s = 5;
    const rnd = () => ((s = (s * 16807) % 2147483647) / 2147483647);
    const rockMat = new THREE.MeshStandardMaterial({ color: 0x2c1f3a, roughness: 0.85, metalness: 0.05, flatShading: true, emissive: new THREE.Color(color), emissiveIntensity: 0.05 });
    const n = Math.min(240, Math.ceil((zone.to - zone.from) * 6));
    const rocks = new THREE.InstancedMesh(new THREE.IcosahedronGeometry(1, 0), rockMat, n);
    const m = new THREE.Matrix4(), q = new THREE.Quaternion();
    for (let i = 0; i < n; i++) {
      const t = zone.from + rnd() * (zone.to - zone.from), a = rnd() * Math.PI * 2, r = 16 + rnd() * 40, size = 0.6 + rnd() ** 2 * 5;
      q.setFromEuler(new THREE.Euler(rnd() * 3, rnd() * 3, rnd() * 3));
      m.compose(new THREE.Vector3(Math.cos(a) * r, FLY_Y + Math.sin(a) * r * 0.6, Z(t)), q, new THREE.Vector3(size, size * (0.6 + rnd() * 0.5), size));
      rocks.setMatrixAt(i, m);
    }
    this.world.add(rocks);
    // faint guides through the three positions, and a ring on every bar
    const dots = [];
    for (const b of beats) for (const [x, y] of FLY) dots.push({ x, y: FLY_Y + y, z: Z(b.t), sx: 0.22, sy: 0.22, sz: 0.22 });
    const g = this.bars(dots, color, 1.2);
    if (g) this.world.add(g);
    const ringTex = this.tex("PickupStrum_Air_Activate");
    for (const b of beats.filter((x) => x.bar)) {
      const ring = this.plane(ringTex, 26, 26, hdr(color, 0.5));
      ring.position.set(0, FLY_Y + 1.5, Z(b.t));
      this.world.add(ring);
    }
  }

  buildGate(z, mode, color) {
    const tex = this.tex("PickupStrum_Air_Activate");
    const gate = this.plane(tex, mode === "List" ? 24 : 30, mode === "List" ? 24 : 30, hdr(color, 1.6));
    gate.position.set(0, mode === "Bounce" ? 4 : mode === "Freefly" ? FLY_Y : 0, z);
    this.world.add(gate);
  }

  // -------------------------------------------------------------------------------------------------------- notes

  /** Where a note sits, and which way is "up" for it. */
  place(mode, lane) {
    if (mode === "List") {
      const a = faceAngle(lane), up = new THREE.Vector3(-Math.cos(a), -Math.sin(a), 0);
      return { pos: new THREE.Vector3(Math.cos(a) * R_IN, Math.sin(a) * R_IN, 0), up, roll: a + Math.PI / 2 };
    }
    if (mode === "Bounce") return { pos: new THREE.Vector3((lane - 1) * LANE_X, 0, 0), up: new THREE.Vector3(0, 1, 0), roll: 0 };
    const [x, y] = FLY[lane] || FLY[0];
    return { pos: new THREE.Vector3(x, FLY_Y + y, 0), up: new THREE.Vector3(0, 1, 0), roll: 0, air: true };
  }

  buildNotes() {
    this.clear(this.notes);
    this.items = [];
    const doc = this.doc, notes = (doc.notes && doc.notes[this.diff]) || [];
    for (const note of notes) {
      const mode = modeAt(doc, note.t);
      const g = note.strum ? this.makeStrum(note, mode) : this.makePickup(note, mode);
      const mark = this.plane(this.tex("PickupStrum_Air_Activate"), 4.2, 4.2, hdr("#ffffff", 1.8));
      mark.visible = !!this.selected(note);
      mark.position.copy(g.userData.center || new THREE.Vector3());
      mark.rotation.z = g.userData.roll || 0;
      g.add(mark);
      g.position.z = Z(note.t);
      g.visible = false;
      this.notes.add(g);
      const upright = [];
      g.traverse((o) => { if (o.userData.upright !== undefined) upright.push(o); });
      this.items.push({ note, group: g, mark, upright, roll: g.userData.roll || 0, end: note.t + (note.len || 0) });
    }
  }

  makePickup(note, mode) {
    const g = new THREE.Group();
    const at = this.place(mode, note.lane);
    const lift = at.air ? 0 : 1.25; // floats above its lane
    const center = at.pos.clone().addScaledVector(at.up, lift);
    g.userData.center = center;
    g.userData.roll = at.roll;
    const color = KEY_COLOR[note.color] || "#ffffff";
    const holder = new THREE.Group();
    holder.position.copy(center);
    holder.rotation.z = at.roll;
    holder.scale.setScalar(at.air ? 1 : 1.25);
    g.add(holder);

    const halo = this.plane(this.tex("Glow"), 4.2, 4.2, hdr(color, 0.3));
    holder.add(halo);
    if (isTurn(note.color)) {
      const dir = note.color === 4 ? -1 : 1;
      const arrow = this.plane(this.chevron(dir), 3.0, 2.25, new THREE.Color(1.15, 1.15, 1.15), { additive: false });
      arrow.userData.upright = 0;
      holder.add(arrow);
      const turnKey = this.keyName(ACTION[note.color]); // the player's turn key, when the mod told us
      if (turnKey) {
        const key = this.plane(this.tex(turnKey), 1.1, 1.1, new THREE.Color(0.95, 0.95, 0.95), { additive: false });
        key.position.set(dir * 2.2, 0, 0.02);
        holder.add(key);
      }
      // and a glowing band across the lane, where the turn happens
      const band = this.plane(this.tex("StrumHoldEndGlowline"), 6.5, 1.2, hdr(color, 1.4));
      band.rotation.x = -Math.PI / 2;
      band.position.y = -lift + 0.05;
      if (!at.air) holder.add(band);
    } else {
      const model = this.modelFor(note, mode);
      if (model) {
        const scale = 3.0 / Math.max(model.w, model.h);
        const mesh = this.plane(model.texture, model.w * scale, model.h * scale, new THREE.Color(1, 1, 1), { additive: false });
        holder.add(mesh);
      } else if (at.air) {
        const ring = new THREE.Mesh(new THREE.TorusGeometry(1.55, 0.13, 10, 40), new THREE.MeshBasicMaterial({ color: hdr(color, 2.8) }));
        holder.add(ring);
        const inner = this.plane(this.tex("PickupStrum_Air_Activate"), 4.2, 4.2, hdr(color, 0.8));
        holder.add(inner);
      } else {
        const shape = this.plane(this.tex("PickupArrow_Emissive_PickupRingLeftRight"), 2.9, 2.3, hdr(color, 1.2));
        shape.userData.upright = [0, Math.PI, Math.PI / 2, -Math.PI / 2][note.color] || 0; // the ring points where the key does
        holder.add(shape);
        const fill = new THREE.Mesh(new THREE.CircleGeometry(0.95, 24), new THREE.MeshBasicMaterial({ color: new THREE.Color(color).multiplyScalar(0.55), transparent: true, opacity: 0.8, depthWrite: false }));
        fill.position.z = -0.01;
        holder.add(fill);
      }
      const keyName = this.keyName(ACTION[note.color]);
      if (keyName && !model) {
        const key = this.plane(this.tex(keyName), 0.86, 0.84, new THREE.Color(0.95, 0.95, 0.95), { additive: false });
        key.position.z = 0.02;
        key.userData.upright = 0;
        holder.add(key);
      }
    }
    if (note.set) this.addStar(holder, note);
    return g;
  }

  makeStrum(note, mode) {
    const g = new THREE.Group();
    const color = "#E8F4FF";
    if (mode === "Freefly") {
      const ring = this.plane(this.tex("PickupStrum_Air_Glow"), 15, 15, hdr(color, 1.5));
      ring.position.set(0, FLY_Y + 1.5, 0);
      g.add(ring);
      g.userData.center = ring.position.clone();
    } else {
      // a bar across the track, on the floor the ship is on
      const lane = mode === "List" ? this.laneAt(note.t, "List") : 1;
      const at = this.place(mode, lane);
      const holder = new THREE.Group();
      const width = mode === "List" ? 2 * R_IN * Math.tan(Math.PI / 3) * 0.9 : ROAD_HALF * 2 * 0.95;
      holder.position.copy(mode === "List" ? at.pos : new THREE.Vector3(0, 0, 0)).addScaledVector(at.up, 0.35);
      holder.rotation.z = at.roll;
      g.add(holder);
      const bar = new THREE.Mesh(new THREE.BoxGeometry(width, 0.1, 0.16), new THREE.MeshBasicMaterial({ color: hdr(color, 1.25) }));
      holder.add(bar);
      const glow = this.plane(this.tex("StrumHoldEndGlowline"), width, 1.3, hdr("#9FD8FF", 0.55));
      glow.position.y = 0.6;
      glow.rotation.z = Math.PI;
      holder.add(glow);
      const badge = this.plane(this.tex(this.keyName("strum") || "Keyboard_Space"), 1.5, 0.93, new THREE.Color(0.95, 0.95, 0.95), { additive: false });
      badge.position.set(0, 1.0, 0.05);
      holder.add(badge);
      if (note.len > 0) {
        const length = note.len * SPEED;
        const trail = this.plane(this.tex("Glow"), width * 0.55, length, hdr("#7FC8FF", 0.9));
        trail.rotation.x = -Math.PI / 2;
        trail.position.set(0, -0.15, -length / 2);
        holder.add(trail);
        const core = new THREE.Mesh(new THREE.BoxGeometry(0.22, 0.08, length), new THREE.MeshBasicMaterial({ color: hdr(color, 2.2) }));
        core.position.set(0, -0.1, -length / 2);
        holder.add(core);
        const end = this.plane(this.tex("StrumHoldEndGlowline"), width * 0.8, 1.6, hdr(color, 1.6));
        end.position.set(0, 0.6, -length);
        end.rotation.z = Math.PI;
        holder.add(end);
      }
      g.userData.center = holder.position.clone().addScaledVector(at.up, 0.8);
      g.userData.roll = at.roll;
    }
    if (note.set) this.addStar(g, note);
    return g;
  }

  addStar(parent, note) {
    const star = this.plane(this.tex("BoostGlow"), 4.6, 4.6, hdr("#FFD86B", note.last ? 2.2 : 1.3));
    star.position.z = -0.05;
    parent.add(star);
  }

  /** The key picture for an action ("strum", "left"… "turnRight"): the player's own or the default, if we can show it. */
  keyName(action) {
    const name = keySprite(this.manifest, action);
    return name && ((this.manifest.sprites && this.manifest.sprites[name]) || drawn[name.replace(/ /g, "_")]) ? name : null;
  }

  chevron(dir) {
    const key = "chevron" + dir;
    if (!this.textures.has(key)) this.textures.set(key, chevron(dir));
    return this.textures.get(key);
  }

  modelFor(note, mode) {
    const models = this.manifest.models;
    if (!models) return null;
    const type = ["PickupLeft", "PickupRight", "PickupDown", "PickupUp"][note.color];
    if (!type) return null;
    return this.photo(models[`${type}.${mode}`] || models[`${type}.List`]);
  }

  // ---------------------------------------------------------------------------------------------------- the ship

  /** The lane the ship is on at t in a zone: the lane of the pickup it is heading for, turns applied. */
  laneAt(t, mode) {
    const notes = (this.doc.notes && this.doc.notes[this.diff]) || [];
    let lane = 1;
    for (const n of notes) {
      if (n.strum || modeAt(this.doc, n.t) !== mode) continue;
      if (n.t > t) return n.lane;
      lane = isTurn(n.color) && mode !== "Freefly" ? afterTurn(mode, n.lane, n.color) : n.lane;
    }
    return lane;
  }

  /** The ship's pose at t: blends from one lane to the next in the quarter-second before the next pickup. */
  shipPose(t, mode) {
    const notes = ((this.doc.notes && this.doc.notes[this.diff]) || []).filter((n) => !n.strum && modeAt(this.doc, n.t) === mode);
    let prev = null, next = null;
    for (const n of notes) { if (n.t <= t) prev = n; else { next = n; break; } }
    const laneOf = (n) => (n ? (isTurn(n.color) && mode !== "Freefly" ? afterTurn(mode, n.lane, n.color) : n.lane) : 1);
    const from = prev ? laneOf(prev) : next ? next.lane : 1;
    const to = next ? next.lane : from;
    let k = 0;
    if (next && to !== from) {
      const start = Math.max(prev ? prev.t : next.t - 0.3, next.t - 0.28);
      k = smooth((t - start) / Math.max(0.05, next.t - 0.04 - start));
    }
    if (mode === "List") {
      let a0 = faceAngle(from), a1 = faceAngle(to);
      let d = a1 - a0;
      while (d > Math.PI) d -= 2 * Math.PI;
      while (d < -Math.PI) d += 2 * Math.PI;
      const a = a0 + d * k;
      const up = new THREE.Vector3(-Math.cos(a), -Math.sin(a), 0);
      return { pos: new THREE.Vector3(Math.cos(a) * R_IN, Math.sin(a) * R_IN, 0).addScaledVector(up, 0.55), up, roll: a + Math.PI / 2 };
    }
    const p0 = this.place(mode, from).pos, p1 = this.place(mode, to).pos;
    const pos = p0.clone().lerp(p1, k);
    if (mode === "Bounce") pos.y = 0.55;
    return { pos, up: new THREE.Vector3(0, 1, 0), roll: mode === "Freefly" ? (p0.x - p1.x) * 0.04 * Math.sin(k * Math.PI) : 0 };
  }

  // ------------------------------------------------------------------------------------------------------ drawing

  resize(width, height) {
    this.renderer.setSize(width, height, false);
    this.composer.setSize(width, height);
    this.camera.aspect = width / Math.max(1, height);
    this.camera.updateProjectionMatrix();
  }

  /** Draws the view at song time `t` (seconds). */
  render(t) {
    if (!this.doc) { this.composer.render(); return; }
    const doc = this.doc;
    const mode = modeAt(doc, t);
    const zone = this.zones().find((z) => t >= z.from && t < z.to);
    // the camera and the ship, blended across a zone change
    let pose = this.shipPose(t, mode), view = this.cameraFor(mode, pose);
    const boundary = zone ? zone.from : 0, since = t - boundary;
    if (zone && since < 0.45 && boundary > 0) {
      const before = modeAt(doc, boundary - 0.001);
      if (before !== mode) {
        const k = smooth(since / 0.45);
        const p0 = this.shipPose(boundary - 0.001, before), v0 = this.cameraFor(before, p0);
        pose = { pos: p0.pos.clone().lerp(pose.pos, k), up: p0.up.clone().lerp(pose.up, k).normalize(), roll: p0.roll + (pose.roll - p0.roll) * k };
        view = { eye: v0.eye.clone().lerp(view.eye, k), look: v0.look.clone().lerp(view.look, k), up: v0.up.clone().lerp(view.up, k).normalize() };
      }
    }
    const z = Z(t);
    this.ship.position.set(pose.pos.x, pose.pos.y, z);
    this.ship.quaternion.setFromEuler(new THREE.Euler(0, 0, pose.roll));
    for (const [form, mesh] of Object.entries(this.ship.userData.forms)) mesh.visible = form === mode;
    this.camera.position.set(view.eye.x, view.eye.y, z + view.eye.z);
    this.camera.up.copy(view.up);
    this.camera.lookAt(view.look.x, view.look.y, z + view.look.z);

    // fog and glow follow the zone
    const c = new THREE.Color(MODE_COLOR[mode]).multiplyScalar(0.11).lerp(new THREE.Color(0x0a0614), 0.45);
    this.scene.fog.color.copy(c);
    this.scene.background.copy(c).multiplyScalar(0.7);
    this.skyUniforms.horizon.value.copy(new THREE.Color(MODE_COLOR[mode]).multiplyScalar(0.1));
    this.dome.position.copy(this.camera.position);
    this.stars.position.copy(this.camera.position);
    this.dust.position.set(0, 0, z - ((t * SPEED * 0.35) % 160) + 80);

    for (const item of this.items) {
      const visible = item.end >= t - 0.12 && item.note.t <= t + AHEAD;
      item.group.visible = visible;
      if (!visible) continue;
      // keys and arrows stay upright on screen, as the game keeps them, whichever face of the tunnel they are on
      for (const o of item.upright) o.rotation.z = o.userData.upright + pose.roll - item.roll;
      // pickups the ship has just taken burst
      const age = t - item.note.t;
      if (!item.note.strum) item.group.children[0].scale.setScalar(age > 0 ? 1 + age * 5 : 1);
    }
    this.composer.render();
  }

  cameraFor(mode, pose) {
    if (mode === "List") {
      const back = 10.5, height = 3.7;
      const eye = pose.pos.clone().addScaledVector(pose.up, height);
      eye.z = back;
      const look = pose.pos.clone().addScaledVector(pose.up, 2.4);
      look.z = -30;
      return { eye, look, up: pose.up.clone() };
    }
    if (mode === "Bounce") {
      return { eye: new THREE.Vector3(pose.pos.x * 0.55, 3.9, 9.5), look: new THREE.Vector3(pose.pos.x * 0.25, 1.5, -30), up: new THREE.Vector3(0, 1, 0) };
    }
    return { eye: new THREE.Vector3(pose.pos.x * 0.6, pose.pos.y + 3.2, 13), look: new THREE.Vector3(pose.pos.x * 0.3, FLY_Y + 1.5, -40), up: new THREE.Vector3(0, 1, 0) };
  }

  dispose() {
    this.clear(this.world);
    this.clear(this.notes);
    for (const t of this.textures.values()) t.dispose();
    this.composer.dispose();
    this.renderer.dispose();
  }
}

function afterTurn(mode, lane, color) {
  const delta = color === 4 ? -1 : 1;
  return mode === "List" ? (lane + delta + 3) % 3 : Math.max(0, Math.min(2, lane + delta));
}

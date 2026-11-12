// The editor's 3D preview as the game shows it, for a perfect run, from the game data cache: the same interface as
// preview3d.js (setChart, setSelection, resize, render, dispose). Everything is a function of the song time, so
// scrubbing shows the exact picture of that time, and the editor only redraws when something changed.
import * as THREE from "three";
import { loadGameCache } from "./gamecache.js";
import { readRig } from "./rig.js";
import { createPath } from "./path.js";
import { prepareRun } from "./run.js";
import { prepareShip, SHIP_SCALE } from "./ship.js";
import { cameraPose } from "./camera.js";
import { createTrack } from "./track.js";
import { createNotes } from "./notes.js";
import { createEffects } from "./effects.js";
import { createPost } from "./post.js";

const WORLD = "void"; // fixed until the preview settings (part 3)

export class GamePreview {
  /** Loads the game data and returns a ready preview; rejects when the cache cannot be used here. */
  static async create(canvas, options = {}) {
    const preview = new GamePreview(canvas, options);
    try {
      await preview.load();
    } catch (e) {
      preview.dispose();
      throw e;
    }
    return preview;
  }

  constructor(canvas, { skin = null } = {}) {
    this.usesGameData = true;
    this.skin = skin;
    this.options = { tier: null }; // null: a perfect run; 0-3: a fixed score level (to compare with the prototype)
    this.stats = { frameMs: 0, prepareMs: 0 };
    this.renderer = new THREE.WebGLRenderer({ canvas, antialias: true, powerPreference: "high-performance" });
    this.renderer.setPixelRatio(Math.min(1.5, window.devicePixelRatio || 1));
    this.renderer.toneMapping = THREE.NoToneMapping;
    this.scene = new THREE.Scene();
    this.camera = new THREE.PerspectiveCamera(60, 16 / 9, 0.1, 3000);
    this.scene.add(this.camera);
    // the bright prototype "lab" around the track (the game's world decor is not drawn)
    this.scene.background = new THREE.Color("#c9d3dc");
    this.scene.fog = new THREE.Fog(0xc9d3dc, 150, 1100);
    this.scene.add(new THREE.HemisphereLight(0xffffff, 0x8a8f96, 2.2));
    const sun = new THREE.DirectionalLight(0xffffff, 1.4);
    sun.position.set(-40, 80, 30);
    this.scene.add(sun);
    // the ship's key light from the camera side (the game lights it with the world's sun and cubemap)
    const key = new THREE.DirectionalLight(0xffffff, 1.2);
    key.position.set(-3, 8, 6);
    this.camera.add(key, key.target);
    this.isSelected = () => false;
    this.doc = null;
  }

  async load() {
    this.cache = await loadGameCache(this.renderer);
    const walker = this.cache.object("PlayerWalkerGlobal");
    this.rig = readRig(this.cache.settings, walker ? walker.root : null);
    this.level = await this.cache.level(WORLD);
    this.path = createPath(this.level);
    const [r1, r2, r3] = await Promise.all(["RollRightLong", "coaster@RollRightLong2Turns", "coaster@RollRightLong3Turns"].map((n) => this.cache.clip(n)));
    this.rolls = { 1: r1, 2: r2, 3: r3 };
    this.ship = await this.cache.prefab("Ship Beginner", { key: "ship" });
    this.ship.scale.setScalar(SHIP_SCALE);
    this.shipMount = new THREE.Group();
    this.shipMount.add(this.ship);
    this.scene.add(this.shipMount);
    this.track = await createTrack(this.cache, this.rig, this.path, this.level);
    this.scene.add(this.track.root);
    this.notes = await createNotes(this.cache, this.rig);
    this.notes.setSelection(this.isSelected);
    this.scene.add(this.notes.root);
    this.effects = await createEffects(this.cache, this.rig, this.level);
    this.scene.add(this.effects.root);
    this.post = createPost(this.renderer, this.scene, this.camera);
    const size = this.renderer.getSize(new THREE.Vector2());
    this.post.resize(size.x, size.y);
    window.gamePreview = this; // the console's view of the stats and options
  }

  setChart(doc, diff) {
    const t0 = performance.now();
    this.doc = doc;
    this.diff = diff;
    this.run = prepareRun(doc, diff, this.rig, { tier: this.options.tier });
    this.shipCtl = prepareShip(this.run, this.rig, this.level, this.rolls);
    this.track.prepare(this.run, doc);
    this.stats.prepareMs = performance.now() - t0;
  }

  /** Changes the options (the fixed tier) and prepares the chart again. */
  setOptions(options) {
    Object.assign(this.options, options);
    if (this.doc) this.setChart(this.doc, this.diff);
  }

  setSelection(isSelected) {
    this.isSelected = isSelected;
    if (this.notes) this.notes.setSelection(isSelected);
  }

  resize(width, height) {
    this.renderer.setSize(width, height, false);
    if (this.post) this.post.resize(width, height);
    this.camera.aspect = width / Math.max(1, height);
    this.camera.updateProjectionMatrix();
  }

  render(t) {
    const t0 = performance.now();
    if (this.run) {
      const pose = cameraPose(this.run, this.shipCtl, this.rig, this.path, t);
      this.camera.position.set(...pose.position);
      this.camera.up.set(...pose.up);
      this.camera.lookAt(...pose.target);
      if (this.camera.fov !== pose.fov) { this.camera.fov = pose.fov; this.camera.updateProjectionMatrix(); }
      const w = this.shipCtl.world(t, this.path);
      this.shipMount.position.set(...w.pos);
      this.shipMount.quaternion.set(...w.q);
      this.track.update(t);
      this.notes.update(this.run, this.shipCtl, this.path, t);
      this.effects.update(this.run, this.shipCtl, this.path, this.camera, this.renderer, t);
    }
    if (this.post) this.post.render(); else this.renderer.render(this.scene, this.camera);
    this.stats.frameMs = performance.now() - t0;
  }

  dispose() {
    if (this.track) this.track.dispose();
    if (this.effects) this.effects.dispose();
    if (this.post) this.post.dispose();
    this.renderer.dispose();
    if (window.gamePreview === this) window.gamePreview = null;
  }
}

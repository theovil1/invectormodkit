// The editor: files, history, selection, side panel, tools, shortcuts. Drawing is in timeline.js, sound in
// audio.js, edits in model.js, the C# side behind rpc.js.
import * as m from "./model.js";
import { AudioEngine } from "./audio.js";
import { Timeline, BUTTON_COLORS, BUTTON_GLYPHS, laneName } from "./timeline.js";
import { call, on, inHost } from "./rpc.js";
import { LANGUAGES, setLang, t, issueText, langCode } from "./i18n.js";
import { skin } from "./skin.js";
import { KEY_SPRITE, MODE_COLOR } from "./look.js";

const $ = (id) => document.getElementById(id);
const el = (tag, attrs = {}, ...kids) => {
  const e = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs)) {
    if (k.startsWith("on")) e.addEventListener(k.slice(2), v);
    else if (k === "class") e.className = v;
    else if (v !== false && v !== null && v !== undefined) e.setAttribute(k, v === true ? "" : v);
  }
  for (const k of kids.flat()) if (k !== null && k !== undefined && k !== false) e.append(k instanceof Node ? k : document.createTextNode(String(k)));
  return e;
};
const fmt = (s) => {
  if (!Number.isFinite(s)) return "—";
  const sign = s < 0 ? "-" : "";
  s = Math.abs(s);
  return `${sign}${Math.floor(s / 60)}:${(s % 60).toFixed(3).padStart(6, "0")}`;
};

class App {
  constructor() {
    this.t = t;
    this.audio = new AudioEngine();
    this.editor = null;
    this.file = null; // { audioPath, chartPath, inCustomSongs }
    this.difficulty = "Hard";
    this.ghost = null;
    this.selection = new Set();
    this.division = 4;
    this.color = m.Color.Down;
    this.issues = {};
    this.issueTimes = new Set();
    this.settings = {};
    this.me = null;
    this.taps = [];
    this.clipboard = [];
    this.timeline = new Timeline($("timeline"), this);
    this.audio.onEnded = () => this.refreshTransport();
    this.preview = null; // the 3D view (none without WebGL)
    this.previewDirty = true;
  }

  async start() {
    try {
      this.settings = await call("settings");
    } catch (e) {
      this.settings = {};
      if (!inHost) this.status(e.message, true);
    }
    setLang(this.settings.lang || navigator.language);
    await skin.load();
    skin.onChange = () => this.timeline.draw();
    this.dress();
    await this.startPreview();
    this.translate();
    this.bind();
    this.renderAll();
    this.loop();
    if (this.settings.hasToken) call("me").then((me) => { this.me = me; this.renderAccount(); }).catch(() => {});
    on("closing", () => this.requestClose());
    on("fonts", () => skin.loadFonts().then(() => { this.timeline.draw(); this.markPreview(); }));
    if (!this.settings.gameFound) this.status(t("gameNotFound"));
  }

  /** The game's pictures around the editor: logo, world backdrop. */
  dress() {
    if (skin.has("Logo Menu")) $("logo").src = skin.url("Logo Menu");
    const bg = skin.manifest.backgrounds || {};
    const file = bg["world-6"] || bg["world-5"] || bg.menu;
    if (file) $("backdrop").style.backgroundImage = `url("${skin.base + file}")`;
  }

  async startPreview() {
    try {
      const { Preview3D } = await import("./preview3d.js");
      this.preview = new Preview3D($("preview"), { skin: skin.base, manifest: skin.manifest });
      const wrap = $("preview-wrap");
      const fit = () => { const r = wrap.getBoundingClientRect(); this.preview.resize(Math.max(1, r.width), Math.max(1, r.height)); this.markPreview(); };
      new ResizeObserver(fit).observe(wrap);
      fit();
    } catch (e) {
      this.preview = null;
      $("preview-hint").textContent = t("noPreview");
      $("preview-hint").classList.remove("hidden");
    }
  }

  /** The 3D view is drawn again on the next frame (it is drawn every frame while playing). */
  markPreview() { this.previewDirty = true; }

  /** The chart into the 3D view: at once, or at most every 120 ms during a drag. */
  syncPreview(final = true) {
    if (!this.preview || !this.editor) return;
    clearTimeout(this._previewSync);
    const go = () => { this.preview.setChart(this.editor.doc, this.difficulty); this.preview.setSelection((n) => this.selection.has(n.id)); this.markPreview(); };
    if (final) go();
    else this._previewSync = setTimeout(go, 120);
  }

  // ----- text

  translate() {
    document.documentElement.lang = langCode();
    for (const e of document.querySelectorAll("[data-t]")) e.textContent = t(e.dataset.t);
    $("btn-play").title = t("playHint");
    $("btn-settings").title = t("settings");
    this.renderKeybar();
    this.renderDifficulty();
    this.renderGhost();
  }

  /** The game's key hints: its key pictures (or drawn keys) and what they do. */
  renderKeybar() {
    const short = (name) => name.replace("Keyboard_", "").replace("Alpha", "").replace("LeftControl", "Ctrl").replace("Plus", "+").replace("Minus", "−");
    for (const e of document.querySelectorAll("#keybar .key")) {
      const keys = e.dataset.keys.split(" ").map((k) => (skin.has(k) ? el("img", { src: skin.url(k), alt: short(k) }) : el("kbd", {}, short(k))));
      e.replaceChildren(...keys, el("span", { class: "label" }, t(e.dataset.label)));
    }
    const key = (c) => (skin.has(KEY_SPRITE[c]) ? el("img", { src: skin.url(KEY_SPRITE[c]), alt: "" }) : el("kbd", {}, BUTTON_GLYPHS[c]));
    $("legend").replaceChildren(
      ...[0, 1, 3, 2].map((c) => el("span", { style: `color:${BUTTON_COLORS[c]}` }, key(c), el("span", { style: "color:var(--muted)" }, t("button" + c)))),
      el("span", { style: `color:${BUTTON_COLORS[4]}` }, el("i"), el("span", { style: "color:var(--muted)" }, t("turn"))),
      el("span", { class: "muted" }, "━ " + t("shipPath")));
  }

  status(text, error = false) {
    $("status-text").textContent = text || "";
    $("status-text").classList.toggle("error", !!error);
  }

  // ----- wiring

  bind() {
    $("btn-open").onclick = $("btn-open-2").onclick = () => this.openDialog();
    $("btn-new").onclick = () => this.newDialog();
    $("btn-save").onclick = () => this.save();
    $("btn-send").onclick = () => this.sendToGame();
    $("btn-publish").onclick = () => this.publishDialog();
    $("btn-settings").onclick = () => this.settingsDialog();
    $("btn-play").onclick = () => this.togglePlay();
    $("speed").onchange = (e) => { this.audio.rate = Number(e.target.value); };
    $("division").onchange = (e) => { this.division = Number(e.target.value); this.timeline.draw(); };
    $("ghost").onchange = (e) => { this.ghost = e.target.value || null; this.timeline.draw(); };
    $("btn-zoom-in").onclick = () => this.timeline.zoom(1.25);
    $("btn-zoom-out").onclick = () => this.timeline.zoom(1 / 1.25);
    $("btn-tap").onclick = () => this.tap(performance.now() / 1000, false);
    $("btn-first-minus").onclick = () => this.nudgeFirstBeat(-0.005);
    $("btn-first-plus").onclick = () => this.nudgeFirstBeat(0.005);
    const meta = { "f-bpm": "bpm", "f-first": "firstBeat", "f-start": "start", "f-end": "end", "f-preview": "preview" };
    for (const [id, key] of Object.entries(meta))
      $(id).onchange = (e) => { const v = Number(e.target.value); if (Number.isFinite(v)) this.edit((doc) => m.setMeta(doc, { [key]: v })); };
    $("f-title").oninput = $("f-artist").oninput = () => { this.editor && (this.editor.dirty = true); this.refreshTitle(); };
    document.addEventListener("keydown", (e) => this.key(e));
    document.addEventListener("pointerdown", (e) => { if (!$("menu").contains(e.target)) this.closeMenu(); });
  }

  // ----- documents

  async openSong(audioPath, fresh = null) {
    if (!(await this.confirmDiscard())) return;
    this.busy(t("loading"));
    try {
      const info = await call("open", { audioPath });
      let chart = info.chart;
      if (fresh || !chart) {
        this.closeModal();
        const mode = fresh || (await this.choose(t("noChartTitle"), t("noChartText"), [["blank", t("blankChart")], ["generate", t("autoChart")]]));
        if (!mode) return;
        this.busy(mode === "generate" ? t("generating") : t("analysing"));
        chart = await call("newChart", { audioPath, mode });
      }
      const url = await call("host.mapAudio", { audioPath: info.audioPath });
      await this.audio.load(url);
      this.file = { audioPath: info.audioPath, chartPath: info.chartPath, inCustomSongs: info.inCustomSongs };
      this.editor = new m.Editor(m.fromFile(chart));
      this.editor.dirty = !!fresh || !info.chart;
      $("f-title").value = info.title || "";
      $("f-artist").value = info.artist || "";
      this.selection = new Set();
      this.timeline.t0 = -0.5;
      this.timeline.pps = 140;
      this.closeModal();
      this.renderAll();
      this.syncPreview();
      this.validateSoon();
      this.status(t("opened", info.title || info.audioPath));
    } catch (e) {
      this.closeModal();
      this.status(t("error", e.message), true);
    }
  }

  chartFile() { return m.toFile(this.editor.doc); }

  async save() {
    if (!this.editor) return false;
    try {
      await call("save", { audioPath: this.file.audioPath, chart: this.chartFile(), title: $("f-title").value, artist: $("f-artist").value });
      this.editor.dirty = false;
      this.refreshTitle();
      this.status(t("saved"));
      return true;
    } catch (e) {
      this.status(t("error", e.message), true);
      return false;
    }
  }

  async sendToGame() {
    if (!this.editor) return;
    if (!this.settings.gameFound) { this.settingsDialog(); return; }
    try {
      const r = await call("sendToGame", { audioPath: this.file.audioPath, chart: this.chartFile(), title: $("f-title").value, artist: $("f-artist").value });
      this.file.audioPath = r.audioPath;
      this.file.chartPath = r.chartPath;
      this.file.inCustomSongs = true;
      this.editor.dirty = false;
      this.refreshTitle();
      this.status(r.copied ? t("sentNew") : t("sentUpdate"));
    } catch (e) {
      this.status(t("error", e.message), true);
    }
  }

  async confirmDiscard() {
    if (!this.editor || !this.editor.dirty) return true;
    const answer = await this.choose(t("unsavedTitle"), t("unsavedText"), [["save", t("save")], ["discard", t("discard")]]);
    if (answer === "save") return this.save();
    return answer === "discard";
  }

  async requestClose() {
    if (await this.confirmDiscard()) call("host.close");
  }

  // ----- edits

  /** One undoable edit; the document is normalized after it. */
  edit(fn) {
    if (!this.editor) return;
    const result = this.editor.apply(fn);
    this.changed(true);
    return result;
  }

  /** After any change: redraw, panels, playability (later). `final` is false during a drag. */
  changed(final = true) {
    const ids = new Set(this.editor.doc.notes[this.difficulty].map((n) => n.id));
    for (const id of [...this.selection]) if (!ids.has(id)) this.selection.delete(id);
    this.timeline.draw();
    this.syncPreview(final);
    if (final) {
      this.renderMeta();
      this.renderSelection();
      this.refreshTitle();
      this.validateSoon();
    }
  }

  addNoteAt(time, lane) {
    const strum = lane === null;
    const id = this.edit((doc) => m.addNote(doc, this.difficulty, { t: time, strum, lane: strum ? 1 : lane, color: this.color }));
    this.select([id]);
    this.seekPreview(time);
  }

  select(ids) {
    this.selection = new Set(ids);
    this.renderSelection();
    this.timeline.draw();
    this.selectionToPreview();
  }

  toggleSelect(id) {
    if (this.selection.has(id)) this.selection.delete(id);
    else this.selection.add(id);
    this.renderSelection();
    this.selectionToPreview();
  }

  selectionToPreview() {
    if (!this.preview) return;
    this.preview.setSelection((n) => this.selection.has(n.id));
    this.markPreview();
  }

  selectedNotes() { return this.editor ? this.editor.doc.notes[this.difficulty].filter((n) => this.selection.has(n.id)) : []; }

  setDifficulty(d) {
    this.difficulty = d;
    this.selection = new Set();
    this.renderDifficulty();
    this.renderSelection();
    this.renderIssues();
    this.timeline.draw();
    this.syncPreview();
  }

  nudgeFirstBeat(dt) { if (this.editor) this.edit((doc) => m.setMeta(doc, { firstBeat: doc.firstBeat + dt })); }

  /** Tap tempo: song times while playing (the grid phase follows the taps), wall clock otherwise. */
  tap(time, songTime) {
    if (!this.editor) return;
    if (this.taps.length && time - this.taps[this.taps.length - 1].t > 2.5) this.taps = [];
    this.taps.push({ t: time, song: songTime });
    const bpm = m.tapTempo(this.taps.map((x) => x.t));
    this.status(bpm ? t("tapStatus", bpm.toFixed(2), this.taps.length) : t("tapMore"));
    this.tapped = bpm;
  }

  applyTap() {
    if (!this.tapped) return;
    const bpm = this.tapped, taps = this.taps.filter((x) => x.song);
    this.edit((doc) => {
      m.setMeta(doc, { bpm });
      if (taps.length >= 4) {
        // Phase: the average offset of the taps on the new beat grid.
        const spb = 60 / bpm;
        const offsets = taps.map((x) => ((x.t - doc.firstBeat) % spb + spb) % spb).map((o) => (o > spb / 2 ? o - spb : o));
        m.setMeta(doc, { firstBeat: doc.firstBeat + offsets.reduce((a, b) => a + b, 0) / offsets.length });
      }
    });
    this.status(t("tapApplied", bpm.toFixed(2)));
    this.taps = [];
    this.tapped = null;
  }

  // ----- playback

  async togglePlay() {
    if (!this.editor) return;
    if (this.audio.playing) this.audio.pause();
    else await this.audio.play();
    this.refreshTransport();
  }

  seek(time) {
    this.audio.seek(time);
    this.refreshTransport();
    this.timeline.draw();
    this.markPreview();
  }

  /** Clicking a note puts the playhead on it (handy to listen from there). */
  seekPreview(time) { if (!this.audio.playing) this.seek(time); }

  refreshTransport() {
    $("btn-play").textContent = this.audio.playing ? "❚❚" : "▶";
    $("clock").textContent = fmt(this.audio.time);
  }

  loop() {
    const frame = () => {
      if (this.editor && this.audio.playing) {
        const now = this.audio.time, doc = this.editor.doc;
        if ($("follow").checked) {
          const tl = this.timeline;
          if (now > tl.t0 + tl.visibleSeconds * 0.7 || now < tl.t0) { tl.t0 = now - tl.visibleSeconds * 0.25; tl.clamp(); }
        }
        const until = now + 0.3;
        const notes = doc.notes[this.difficulty].filter((n) => n.t >= now && n.t < until).map((n) => n.t);
        const beats = m.gridLines(doc, now, until, 1).map((l) => ({ t: l.t, bar: l.level === 0 }));
        this.audio.schedule(notes, beats, $("claps").checked, $("metronome").checked);
        this.timeline.draw();
        $("clock").textContent = fmt(now);
        this.previewDirty = true;
      }
      if (this.preview && this.previewDirty && this.editor) {
        this.previewDirty = false;
        const now = this.audio.time, mode = m.modeAt(this.editor.doc, Math.max(0, now));
        if (mode !== this._zoneShown) {
          this._zoneShown = mode;
          $("zone-name").textContent = t("mode" + mode);
          $("zone-name").style.color = MODE_COLOR[mode];
        }
        this.preview.render(now);
      }
      requestAnimationFrame(frame);
    };
    requestAnimationFrame(frame);
  }

  // ----- playability

  validateSoon() {
    clearTimeout(this._validate);
    this._validate = setTimeout(() => this.validate(), 300);
  }

  async validate() {
    if (!this.editor) return;
    try {
      this.issues = await call("validate", { chart: this.chartFile() });
    } catch {
      this.issues = {};
    }
    this.renderIssues();
    this.timeline.draw();
  }

  // ----- rendering

  renderAll() {
    const has = !!this.editor;
    $("empty").classList.toggle("hidden", has);
    for (const id of ["btn-save", "btn-send", "btn-publish", "btn-play"]) $(id).disabled = !has;
    if (this.preview) {
      $("preview-hint").textContent = t("skinHint");
      $("preview-hint").classList.toggle("hidden", skin.hasModels());
    }
    this.renderDifficulty();
    this.renderGhost();
    this.renderMeta();
    this.renderSelection();
    this.renderTools();
    this.renderIssues();
    this.renderAccount();
    this.refreshTitle();
    this.refreshTransport();
    this.timeline.layout();
    this.timeline.draw();
  }

  /** The game's difficulty selector (top) and the difficulties with their note counts (side panel). */
  renderDifficulty() {
    const i = m.DIFFICULTIES.indexOf(this.difficulty);
    const step = (k) => this.setDifficulty(m.DIFFICULTIES[(i + k + 4) % 4]);
    const arrow = (side, k, glyph) => el("button", { class: "arrow", onclick: () => step(k), title: t("kbDifficulty") },
      skin.has(`DirectionArrowDifficulty ${side}`) ? el("img", { src: skin.url(`DirectionArrowDifficulty ${side}`), alt: glyph }) : glyph);
    const bar = (n) => (skin.has("DifficultyIcon2") ? el("img", { src: skin.url("DifficultyIcon2"), class: n > i ? "off" : "", alt: "" }) : el("i", { class: n > i ? "off" : "" }));
    $("difficulty").replaceChildren(arrow("Left", -1, "‹"), el("div", { class: "bars" }, [0, 1, 2, 3].map(bar)), el("span", { class: "name" }, this.difficulty), arrow("Right", 1, "›"));
    $("difficulties").replaceChildren(...m.DIFFICULTIES.map((d) => {
      const count = this.editor ? this.editor.doc.notes[d].length : 0;
      return el("button", { class: d === this.difficulty ? "on" : "", onclick: () => this.setDifficulty(d) }, el("span", {}, d), el("b", {}, t("notesCount", count)));
    }));
  }

  renderGhost() {
    $("ghost").replaceChildren(el("option", { value: "" }, t("none")), ...m.DIFFICULTIES.map((d) => el("option", { value: d, selected: this.ghost === d }, d)));
  }

  renderMeta() {
    const doc = this.editor && this.editor.doc;
    const set = (id, v, digits) => { const e = $(id); if (document.activeElement !== e) e.value = doc ? Number(v.toFixed(digits)) : ""; e.disabled = !doc; };
    set("f-bpm", doc ? doc.bpm : 0, 3);
    set("f-first", doc ? doc.firstBeat : 0, 4);
    set("f-start", doc ? doc.start : 0, 3);
    set("f-end", doc ? doc.end : 0, 3);
    set("f-preview", doc ? doc.preview : 0, 3);
    $("f-title").disabled = $("f-artist").disabled = $("btn-tap").disabled = $("btn-first-minus").disabled = $("btn-first-plus").disabled = !doc;
    if (doc) this.renderDifficulty();
  }

  renderSelection() {
    const card = $("card-selection");
    const notes = this.selectedNotes(), d = this.difficulty;
    const kids = [el("h2", {}, t("selection"))];
    if (!this.editor) { card.replaceChildren(...kids, el("p", { class: "note-info" }, t("nothingOpen"))); return; }
    kids.push(el("p", { class: "note-info" }, notes.length === 0 ? t("selectHint") : notes.length === 1 ? describe(notes[0], this.editor.doc) : t("selectedCount", notes.length)));
    // Buttons: the current one for new notes, or the selection's.
    kids.push(el("div", { class: "buttons colors" }, BUTTON_GLYPHS.map((g, c) => el("button", {
      class: (notes.length ? notes.every((n) => !n.strum && n.color === c) : this.color === c) ? "on" : "",
      style: `border-color:${BUTTON_COLORS[c]};color:${BUTTON_COLORS[c]}`, title: t("button" + c) + ` (${c + 1})`,
      onclick: () => this.setColor(c),
    }, KEY_SPRITE[c] && skin.has(KEY_SPRITE[c]) ? el("img", { src: skin.url(KEY_SPRITE[c]), alt: g }) : g))));
    if (notes.length) {
      const ids = notes.map((n) => n.id);
      const allStrum = notes.every((n) => n.strum);
      kids.push(el("div", { class: "buttons" },
        el("button", { onclick: () => this.edit((doc) => m.setStrum(doc, d, ids, !allStrum)) }, allStrum ? t("toPickup") : t("toStrum")),
        el("button", { onclick: () => this.edit((doc) => m.makeStarSet(doc, d, ids)) }, t("starSet")),
        notes.some((n) => n.set) ? el("button", { onclick: () => this.edit((doc) => m.clearStarSet(doc, d, ids)) }, t("clearStarSet")) : null,
        el("button", { class: "danger", onclick: () => { this.edit((doc) => m.removeNotes(doc, d, ids)); this.select([]); } }, t("delete"))));
      if (notes.length === 1) {
        const n = notes[0], free = m.laneIsFree(this.editor.doc, d, n);
        if (!n.strum && free)
          kids.push(el("div", { class: "buttons" }, [0, 1, 2].map((lane) =>
            el("button", { class: n.lane === lane ? "on" : "", onclick: () => this.edit((doc) => m.setLane(doc, d, [n.id], lane)) }, laneName(t, m.modeAt(this.editor.doc, n.t), lane)))));
        else if (!n.strum) kids.push(el("p", { class: "note-info" }, t("laneFollows")));
        if (n.strum) {
          const input = el("input", { type: "number", step: "0.01", min: "0", value: n.len.toFixed(3) });
          input.onchange = () => this.edit((doc) => m.setHold(doc, d, n.id, Number(input.value) || 0));
          kids.push(el("label", { class: "field" }, el("span", {}, t("holdLength")), input));
        }
      }
    }
    card.replaceChildren(...kids);
  }

  setColor(c) {
    this.color = c;
    const ids = this.selectedNotes().filter((n) => !n.strum).map((n) => n.id);
    if (ids.length) this.edit((doc) => m.setColor(doc, this.difficulty, ids, c));
    else this.renderSelection();
  }

  renderTools() {
    const card = $("card-tools");
    const kids = [el("h2", {}, t("tools"))];
    if (!this.editor) { card.replaceChildren(...kids, el("p", { class: "note-info" }, t("nothingOpen"))); return; }
    const target = el("select", {}, m.DIFFICULTIES.filter((d) => d !== this.difficulty).map((d) => el("option", { value: d }, d)));
    kids.push(el("p", { class: "note-info" }, t("toolsHint")));
    kids.push(el("label", { class: "field" }, el("span", {}, t("copyTo")), target));
    kids.push(el("div", { class: "buttons" },
      el("button", { onclick: () => this.copyTo(target.value, "selection") }, t("copySelection")),
      el("button", { onclick: () => this.copyTo(target.value, "zone") }, t("copyZone"))));
    kids.push(el("p", { class: "note-info" }, this.settings.modGenerator ? t("regenModHint") : t("regenLocalHint")));
    kids.push(el("div", { class: "buttons" },
      el("button", { onclick: () => this.regenerate("difficulty") }, t("regenDifficulty")),
      el("button", { onclick: () => this.regenerate("zone") }, t("regenZone")),
      el("button", { onclick: () => this.regenerate("selection") }, t("regenSelection"))));
    card.replaceChildren(...kids);
  }

  /** The time range of the selection, or of the zone under the playhead. */
  range(scope) {
    const doc = this.editor.doc;
    if (scope === "difficulty") return { from: 0, to: doc.end + 1 };
    if (scope === "selection") {
      const notes = this.selectedNotes();
      if (!notes.length) return null;
      return { from: notes[0].t, to: notes[notes.length - 1].t + 1e-3 };
    }
    return m.sectionRange(doc, m.sectionIndexAt(doc, this.audio.time));
  }

  copyTo(target, scope) {
    const r = this.range(scope);
    if (!r) { this.status(t("selectFirst"), true); return; }
    this.edit((doc) => m.copyRange(doc, this.difficulty, target, r.from, r.to));
    this.status(t("copied", target));
  }

  async regenerate(scope) {
    const r = this.range(scope);
    if (!r) { this.status(t("selectFirst"), true); return; }
    this.busy(t("generating"));
    try {
      const generated = m.fromFile(await call("regenerate", { audioPath: this.file.audioPath }));
      this.edit((doc) => m.replaceRange(doc, this.difficulty, generated.notes[this.difficulty], r.from, r.to));
      this.status(t("regenerated"));
    } catch (e) {
      this.status(t("error", e.message), true);
    }
    this.closeModal();
  }

  renderIssues() {
    const list = (this.issues && this.issues[this.difficulty]) || [];
    this.issueTimes = new Set(list.map((i) => Math.round(i.time * 1000)));
    $("issues-title").textContent = t("issues", list.length);
    $("issues-title").classList.toggle("ok", list.length === 0);
    $("issues").replaceChildren(...(this.editor ? list.slice(0, 300).map((i) => el("li", {
      onclick: () => {
        const n = this.editor.doc.notes[this.difficulty].find((n) => Math.abs(n.t - i.time) < 0.0015);
        if (n) this.select([n.id]);
        this.timeline.reveal(i.time, true);
        this.seek(i.time);
      },
    }, el("b", {}, fmt(i.time)), issueText(i))) : []));
    if (this.editor && !list.length) $("issues").replaceChildren(el("li", {}, t("noIssues")));
  }

  renderAccount() {
    $("account").textContent = this.me ? t("signedInAs", this.me.name) : "";
  }

  refreshTitle() {
    const name = this.editor ? `${$("f-title").value || "?"}${$("f-artist").value ? " — " + $("f-artist").value : ""}` : "";
    const dirty = this.editor && this.editor.dirty ? " *" : "";
    const title = (name ? name + dirty + " · " : "") + t("appName");
    document.title = title;
    if (inHost) call("host.title", { text: title }).catch(() => {});
  }

  showCursor(time, h) {
    if (!this.editor) return;
    const doc = this.editor.doc;
    const beat = (time - doc.firstBeat) / m.secondsPerBeat(doc);
    $("status-info").textContent = `${fmt(time)} · ${t("beat")} ${(Math.floor(beat / 4) + 1)}.${(((Math.floor(beat) % 4) + 4) % 4) + 1} · ${t("mode" + m.modeAt(doc, Math.max(0, time)))}`;
  }

  // ----- menus and dialogs

  zoneMenu(index, x, y) {
    const doc = this.editor.doc, menu = $("menu"), r = m.sectionRange(doc, index);
    const item = (label, fn, disabled = false) => el("button", { disabled, onclick: () => { this.closeMenu(); fn(); } }, label);
    menu.replaceChildren(
      ...m.MODES.map((mode) => item((doc.sections[index].mode === mode ? "● " : "○ ") + t("mode" + mode), () => this.edit((d) => m.setSectionMode(d, index, mode)))),
      el("hr"),
      item(t("selectZone"), () => this.select(m.notesIn(doc, this.difficulty, r.from, r.to).map((n) => n.id))),
      item(t("regenThisZone"), () => { this.seek(r.from + 0.01); this.regenerate("zone"); }),
      item(t("removeBoundary"), () => this.edit((d) => m.removeSection(d, index)), index === 0),
    );
    menu.style.left = `${Math.min(x, window.innerWidth - 220)}px`;
    menu.style.top = `${Math.min(y, window.innerHeight - 240)}px`;
    menu.classList.remove("hidden");
  }

  closeMenu() { $("menu").classList.add("hidden"); }

  modal(...kids) {
    $("modal-box").replaceChildren(...kids);
    $("modal").classList.remove("hidden");
  }

  closeModal() { $("modal").classList.add("hidden"); }

  busy(text) { this.modal(el("h1", {}, text), el("div", { class: "spinner" })); }

  /** A question with buttons; resolves with the chosen key (null = cancel). */
  choose(title, text, options) {
    return new Promise((resolve) => {
      const done = (v) => { this.closeModal(); resolve(v); };
      this.modal(el("h1", {}, title), el("p", {}, text), el("div", { class: "actions" },
        el("button", { onclick: () => done(null) }, t("cancel")),
        options.map(([key, label], i) => el("button", { class: i === options.length - 1 ? "accent" : "", onclick: () => done(key) }, label))));
    });
  }

  async openDialog() {
    let songs = [];
    try { songs = await call("songs"); } catch { /* no game folder */ }
    const list = el("ul", { class: "songs" }, songs.map((s) => el("li", { onclick: () => this.openSong(s.audioPath) },
      el("span", {}, s.title), el("span", { class: "sub" }, s.artist), el("span", { class: "tag" }, s.hasChart ? t("hasChart") : t("noChart")))));
    this.modal(el("h1", {}, t("openTitle")),
      el("p", {}, this.settings.gameFound ? t("openText") : t("gameNotFound")),
      songs.length ? list : el("p", { class: "muted" }, t("noSongs")),
      el("div", { class: "actions" },
        el("button", { onclick: () => this.closeModal() }, t("cancel")),
        el("button", { class: "accent", onclick: () => this.pickAudio() }, t("otherFile"))));
  }

  async pickAudio(fresh = null) {
    const path = await call("host.openAudio").catch(() => null);
    if (path) await this.openSong(path, fresh);
  }

  async newDialog() {
    const mode = await this.choose(t("newTitle"), t("newText"), [["blank", t("blankChart")], ["generate", t("autoChart")]]);
    if (mode) await this.pickAudio(mode);
  }

  async settingsDialog() {
    const lang = el("select", {}, LANGUAGES.map(([code, name]) => el("option", { value: code, selected: code === langCode() }, name)));
    lang.onchange = async () => {
      setLang(lang.value);
      try { this.settings = await call("setLang", { lang: lang.value }); } catch { }
      this.translate();
      this.renderAll();
      this.settingsDialog();
    };
    const gamePath = el("p", { class: this.settings.gameFound ? "" : "muted" }, this.settings.gameFound ? this.settings.gameDir : t("gameNotFound"));
    const account = this.me
      ? el("div", { class: "actions" }, el("span", { class: "spacer" }, t("signedInAs", this.me.name)), el("button", { onclick: async () => { await call("signOut"); this.me = null; this.renderAccount(); this.settingsDialog(); } }, t("signOut")))
      : el("div", { class: "actions" }, el("button", { class: "accent", onclick: () => this.signIn().then(() => this.settingsDialog()) }, t("signIn")));
    this.modal(el("h1", {}, t("settings")),
      el("label", { class: "field" }, el("span", {}, t("language")), lang),
      el("h2", {}, t("gameFolder")), gamePath,
      el("div", { class: "buttons" }, el("button", { onclick: async () => {
        const dir = await call("host.pickGameDir").catch(() => null);
        if (!dir) return;
        try { this.settings = await call("setGameDir", { path: dir }); this.renderTools(); this.settingsDialog(); }
        catch (e) { this.status(t("notGameFolder"), true); }
      } }, t("chooseFolder"))),
      el("h2", {}, t("account")), account,
      el("div", { class: "actions" }, el("button", { onclick: () => this.closeModal() }, t("close"))));
  }

  async signIn() {
    this.modal(el("h1", {}, t("signingIn")), el("p", {}, t("signingInText")), el("div", { class: "spinner" }));
    try {
      this.me = await call("signIn");
      this.renderAccount();
      this.status(t("signedInAs", this.me.name));
    } catch (e) {
      this.status(t("error", e.message), true);
    }
    this.closeModal();
  }

  async publishDialog(acceptRules = false) {
    if (!this.editor) return;
    if (!this.me) {
      await this.signIn();
      if (!this.me) return;
    }
    const link = el("input", { type: "url", placeholder: "https://www.youtube.com/watch?v=…", value: this._lastLink || "" });
    const error = el("p", { class: "muted" }, t("publishHint"));
    const go = async () => {
      if (!/^(https?:\/\/)?(www\.|m\.|music\.)?(youtube\.com|youtu\.be)\//i.test(link.value.trim())) { error.textContent = t("badLink"); error.className = ""; return; }
      this._lastLink = link.value.trim();
      this.busy(t("publishing"));
      try {
        const r = await call("publish", { audioPath: this.file.audioPath, chart: this.chartFile(), title: $("f-title").value, artist: $("f-artist").value, youtubeUrl: this._lastLink, acceptRules });
        this.closeModal();
        if (r.needRules) return this.rulesDialog();
        if (this.file.inCustomSongs) this.editor.dirty = false;
        this.refreshTitle();
        this.status(t("published", r.version));
      } catch (e) {
        this.closeModal();
        this.status(t("error", e.message), true);
      }
    };
    this.modal(el("h1", {}, t("publishTitle")),
      el("p", {}, t("publishText", $("f-title").value || "?", $("f-artist").value || "?")),
      el("label", { class: "field" }, el("span", {}, t("youtubeLink")), link), error,
      el("div", { class: "actions" }, el("button", { onclick: () => this.closeModal() }, t("cancel")), el("button", { class: "accent", onclick: go }, t("publish"))));
    link.focus();
  }

  async rulesDialog() {
    const answer = await this.choose(t("rulesTitle"), t("rulesText"), [["read", t("readRules")], ["accept", t("acceptRules")]]);
    if (answer === "read") {
      call("rulesUrl", { lang: langCode() }).then((url) => call("openUrl", { url })).catch(() => {});
      return this.rulesDialog();
    }
    if (answer === "accept") return this.publishDialog(true);
  }

  // ----- keyboard

  key(e) {
    const typing = e.target instanceof HTMLInputElement || e.target instanceof HTMLSelectElement;
    if (!$("modal").classList.contains("hidden")) {
      if (e.key === "Escape") this.closeModal();
      return;
    }
    const ctrl = e.ctrlKey || e.metaKey;
    if (ctrl && e.key.toLowerCase() === "s") { e.preventDefault(); this.save(); return; }
    if (ctrl && e.key.toLowerCase() === "o") { e.preventDefault(); this.openDialog(); return; }
    if (typing || !this.editor) return;
    const d = this.difficulty, ids = [...this.selection];
    const step = m.secondsPerBeat(this.editor.doc) / this.division;
    switch (true) {
      case e.key === " ": e.preventDefault(); this.togglePlay(); break;
      case ctrl && e.key.toLowerCase() === "z" && !e.shiftKey: e.preventDefault(); if (this.editor.undo()) this.changed(); break;
      case ctrl && (e.key.toLowerCase() === "y" || (e.key.toLowerCase() === "z" && e.shiftKey)): e.preventDefault(); if (this.editor.redo()) this.changed(); break;
      case ctrl && e.key.toLowerCase() === "a": e.preventDefault(); this.select(this.editor.doc.notes[d].map((n) => n.id)); break;
      case ctrl && e.key.toLowerCase() === "c": this.clipboard = m.copyNotes(this.editor.doc, d, ids); this.status(t("copiedNotes", this.clipboard.length)); break;
      case ctrl && e.key.toLowerCase() === "x": this.clipboard = m.copyNotes(this.editor.doc, d, ids); this.edit((doc) => m.removeNotes(doc, d, ids)); break;
      case ctrl && e.key.toLowerCase() === "v": {
        if (!this.clipboard.length) break;
        const at = m.snap(this.editor.doc, this.audio.time, this.division);
        this.select(this.edit((doc) => m.paste(doc, d, this.clipboard, at)));
        break;
      }
      case e.key === "Delete" || e.key === "Backspace": if (ids.length) { this.edit((doc) => m.removeNotes(doc, d, ids)); this.select([]); } break;
      case e.key === "Escape": this.select([]); break;
      case /^[1-6]$/.test(e.key): this.setColor(Number(e.key) - 1); break;
      case e.key.toLowerCase() === "s": if (ids.length) { const all = this.selectedNotes().every((n) => n.strum); this.edit((doc) => m.setStrum(doc, d, ids, !all)); } break;
      case e.key === "ArrowLeft" || e.key === "ArrowRight": {
        e.preventDefault();
        const dt = (e.key === "ArrowLeft" ? -1 : 1) * (e.altKey ? 0.001 : step);
        if (ids.length) this.edit((doc) => m.moveNotes(doc, d, ids, dt));
        else this.seek(Math.max(0, this.audio.time + dt));
        break;
      }
      case e.key === "ArrowUp" || e.key === "ArrowDown": if (ids.length) { e.preventDefault(); this.edit((doc) => m.moveNotes(doc, d, ids, 0, e.key === "ArrowUp" ? -1 : 1)); } break;
      case e.key === "+" || e.key === "=": this.timeline.zoom(1.25); break;
      case e.key === "-": this.timeline.zoom(1 / 1.25); break;
      case e.key.toLowerCase() === "t": this.tap(this.audio.playing ? this.audio.time : performance.now() / 1000, this.audio.playing); break;
      case e.key === "Enter": this.applyTap(); break;
      case e.key === "Home": this.seek(0); this.timeline.reveal(0, true); break;
      case e.key === "Tab": e.preventDefault(); this.setDifficulty(m.DIFFICULTIES[(m.DIFFICULTIES.indexOf(d) + (e.shiftKey ? 3 : 1)) % 4]); break;
      default: return;
    }
  }
}

function describe(n, doc) {
  const kind = n.strum ? t("strum") + (n.len > 0 ? ` · ${t("hold")} ${n.len.toFixed(2)} s` : "") : `${t("button" + n.color)} · ${laneName(t, m.modeAt(doc, n.t), n.lane)}`;
  return `${fmt(n.t)} · ${kind} · ${t("mode" + m.modeAt(doc, n.t))}${n.set ? ` · ★${n.set}${n.last ? "!" : ""}` : ""}`;
}

const app = new App();
window.app = app; // for debugging in the WebView's dev tools
app.start();

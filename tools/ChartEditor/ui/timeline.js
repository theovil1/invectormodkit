// The horizontal timeline: drawing on a canvas, hit testing, mouse gestures. Edits go through the app (history).
// It reads like the game: zones named by what the player sees (tunnel, road, free flight) with their lanes' names,
// pickups as the game's keys in their colour's glow, turns as purple rings, strums as the game's strum badge, and the
// ship's path from pickup to pickup, bending at the turns.
import * as m from "./model.js";
import { MODE_COLOR, KEY_COLOR, KEY_GLYPH, ACTION, FONT } from "./look.js";
import { skin } from "./skin.js";

const LEFT = 104; // row labels
const ROWS = { ruler: 26, zones: 26, wave: 64, lane: 64, strum: 46, scroll: 12 };
export const BUTTON_COLORS = KEY_COLOR;
export const BUTTON_GLYPHS = KEY_GLYPH;
const HIT = 14;

/** A lane's name in a zone: the tunnel's faces, the road's sides, the flight positions. */
export function laneName(t, mode, lane) {
  if (mode === "List") return t("laneFace", lane + 1);
  if (mode === "Bounce") return [t("button0"), t("center"), t("button1")][lane];
  return [t("flyBottom"), t("flyTopLeft"), t("flyTopRight")][lane];
}

const tints = new Map();
/** A white picture in a colour (cached). */
function tint(image, color) {
  const key = image.src + color;
  if (tints.has(key)) return tints.get(key);
  const c = document.createElement("canvas");
  c.width = image.naturalWidth; c.height = image.naturalHeight;
  const g = c.getContext("2d");
  g.drawImage(image, 0, 0);
  g.globalCompositeOperation = "source-in";
  g.fillStyle = color;
  g.fillRect(0, 0, c.width, c.height);
  tints.set(key, c);
  return c;
}

export class Timeline {
  constructor(canvas, app) {
    this.canvas = canvas;
    this.ctx = canvas.getContext("2d");
    this.app = app;
    this.t0 = 0; // song time at the left edge of the notes area
    this.pps = 140; // pixels per second
    this.drag = null;
    this.hover = null;
    this.layout();
    new ResizeObserver(() => { this.layout(); this.draw(); }).observe(canvas.parentElement);
    canvas.addEventListener("pointerdown", (e) => this.down(e));
    canvas.addEventListener("pointermove", (e) => this.move(e));
    canvas.addEventListener("pointerup", (e) => this.up(e));
    canvas.addEventListener("dblclick", (e) => this.dblclick(e));
    canvas.addEventListener("contextmenu", (e) => { e.preventDefault(); this.context(e); });
    canvas.addEventListener("wheel", (e) => this.wheel(e), { passive: false });
  }

  layout() {
    const rect = this.canvas.parentElement.getBoundingClientRect();
    this.w = Math.max(200, rect.width);
    this.h = Math.max(200, rect.height);
    const dpr = window.devicePixelRatio || 1;
    this.canvas.width = Math.round(this.w * dpr);
    this.canvas.height = Math.round(this.h * dpr);
    this.ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    let y = 0;
    const row = (name, h) => { const r = { y, h }; y += h; return r; };
    // Lanes share what is left (between a minimum and a comfortable size); the waveform gives way first.
    const fixed = ROWS.ruler + ROWS.zones + ROWS.strum + ROWS.scroll + 4;
    const wave = Math.max(26, Math.min(ROWS.wave, this.h - fixed - 3 * 40));
    this.rows = { ruler: row("ruler", ROWS.ruler), zones: row("zones", ROWS.zones), wave: row("wave", wave) };
    const free = this.h - y - ROWS.strum - ROWS.scroll - 4;
    const laneH = Math.max(30, Math.min(96, Math.floor(free / 3)));
    this.rows.lanes = [0, 1, 2].map(() => row("lane", laneH));
    this.rows.strum = row("strum", ROWS.strum);
    this.rows.scroll = { y: this.h - ROWS.scroll, h: ROWS.scroll };
  }

  get doc() { return this.app.editor ? this.app.editor.doc : null; }
  get width() { return this.w - LEFT; }
  get visibleSeconds() { return this.width / this.pps; }
  x(t) { return LEFT + (t - this.t0) * this.pps; }
  time(x) { return this.t0 + (x - LEFT) / this.pps; }
  laneY(lane) { const r = this.rows.lanes[lane]; return r.y + r.h / 2; }

  /** Keeps the view inside the song. */
  clamp() {
    const end = this.doc ? this.doc.end : 60;
    this.t0 = Math.max(-1, Math.min(this.t0, end - this.visibleSeconds * 0.5));
  }

  zoom(factor, aroundX = LEFT + this.width / 2) {
    const t = this.time(aroundX);
    this.pps = Math.max(12, Math.min(2400, this.pps * factor));
    this.t0 = t - (aroundX - LEFT) / this.pps;
    this.clamp();
    this.draw();
  }

  /** Brings a time into view (around a quarter from the left). */
  reveal(t, force = false) {
    if (force || t < this.t0 || t > this.t0 + this.visibleSeconds * 0.85) {
      this.t0 = t - this.visibleSeconds * 0.25;
      this.clamp();
    }
  }

  // ----- drawing

  draw() {
    const c = this.ctx, doc = this.doc;
    c.clearRect(0, 0, this.w, this.h);
    c.fillStyle = "rgba(10, 7, 16, .82)";
    c.fillRect(0, 0, this.w, this.h);
    if (!doc) return;
    const from = this.t0, to = this.t0 + this.visibleSeconds;
    const d = this.app.difficulty;
    const selection = this.app.selection;

    // Zone tint behind the lanes, fading down; the lanes' names where each zone starts.
    const lanesTop = this.rows.lanes[0].y, lanesBottom = this.rows.strum.y + this.rows.strum.h;
    doc.sections.forEach((s, i) => {
      const { from: a, to: b } = m.sectionRange(doc, i);
      const x0 = Math.max(LEFT, this.x(a)), x1 = Math.min(this.w, this.x(b));
      if (x1 <= x0) return;
      const g = c.createLinearGradient(0, lanesTop, 0, lanesBottom);
      g.addColorStop(0, MODE_COLOR[s.mode] + "2A");
      g.addColorStop(1, MODE_COLOR[s.mode] + "08");
      c.fillStyle = g;
      c.fillRect(x0, lanesTop, x1 - x0, lanesBottom - lanesTop);
      if (this.x(a) >= LEFT && this.x(a) < this.w - 60) {
        c.font = `600 11px ${FONT.semi}`;
        c.fillStyle = MODE_COLOR[s.mode] + "CC";
        for (let lane = 0; lane < 3; lane++) c.fillText(laneName(this.app.t, s.mode, lane).toUpperCase(), this.x(a) + 6, this.rows.lanes[lane].y + 13);
      }
    });
    // Lane separators.
    c.strokeStyle = "rgba(255,255,255,.08)";
    for (const r of this.rows.lanes) { c.beginPath(); c.moveTo(LEFT, r.y + r.h + 0.5); c.lineTo(this.w, r.y + r.h + 0.5); c.stroke(); }
    // Outside the song.
    c.fillStyle = "rgba(0,0,0,.45)";
    if (this.x(0) > LEFT) c.fillRect(LEFT, 0, this.x(0) - LEFT, this.rows.scroll.y);
    if (this.x(doc.end) < this.w) c.fillRect(this.x(doc.end), 0, this.w - this.x(doc.end), this.rows.scroll.y);

    // Grid.
    const lines = m.gridLines(doc, Math.max(0, from), Math.min(doc.end, to), this.app.division);
    const minGap = (m.secondsPerBeat(doc) / this.app.division) * this.pps;
    for (const l of lines) {
      if (l.level === 2 && minGap < 6) continue;
      const x = Math.round(this.x(l.t)) + 0.5;
      c.strokeStyle = l.level === 0 ? "rgba(255,255,255,.42)" : l.level === 1 ? "rgba(255,255,255,.18)" : "rgba(255,255,255,.07)";
      c.beginPath();
      c.moveTo(x, this.rows.ruler.y + (l.level === 0 ? 4 : l.level === 1 ? 16 : 22));
      c.lineTo(x, lanesBottom);
      c.stroke();
      if (l.level === 0 && (l.bar % Math.max(1, Math.round(60 / (m.secondsPerBeat(doc) * 4 * this.pps))) === 0 || this.pps > 30)) {
        c.fillStyle = "#FFFFFF";
        c.font = `700 14px ${FONT.bold}`;
        c.fillText(String(l.bar), x + 4, this.rows.ruler.y + 15);
      }
    }

    this.drawRows();
    this.drawZones(doc);
    this.drawWave(from);
    this.drawMarkers(doc);
    this.drawPath(doc, d, from, to);

    // Ghost difficulty.
    const ghost = this.app.ghost;
    if (ghost && ghost !== d) {
      c.globalAlpha = 0.22;
      for (const n of doc.notes[ghost]) if (n.t >= from - 2 && n.t <= to) this.drawNote(n, false, false);
      c.globalAlpha = 1;
    }
    // Notes.
    const issueTimes = this.app.issueTimes;
    for (const n of doc.notes[d]) {
      if (n.t + (n.len || 0) < from - 0.5 || n.t > to + 0.5) continue;
      this.drawNote(n, selection.has(n.id), issueTimes.has(Math.round(n.t * 1000)));
    }

    // Box selection.
    if (this.drag && this.drag.kind === "box" && this.drag.moved) {
      const b = this.drag;
      c.fillStyle = "rgba(203,51,255,.12)";
      c.strokeStyle = "#CB33FF";
      c.fillRect(Math.min(b.x0, b.x1), Math.min(b.y0, b.y1), Math.abs(b.x1 - b.x0), Math.abs(b.y1 - b.y0));
      c.strokeRect(Math.min(b.x0, b.x1) + 0.5, Math.min(b.y0, b.y1) + 0.5, Math.abs(b.x1 - b.x0), Math.abs(b.y1 - b.y0));
    }

    // Playhead, glowing.
    const px = this.x(this.app.audio.time);
    if (px >= LEFT && px <= this.w) {
      const g = c.createLinearGradient(px - 14, 0, px + 14, 0);
      g.addColorStop(0, "rgba(203,51,255,0)"); g.addColorStop(0.5, "rgba(203,51,255,.32)"); g.addColorStop(1, "rgba(203,51,255,0)");
      c.fillStyle = g;
      c.fillRect(px - 14, 0, 28, lanesBottom);
      c.fillStyle = "#FFFFFF";
      c.fillRect(px - 1, 0, 2, lanesBottom);
      c.beginPath();
      c.moveTo(px - 7, 0); c.lineTo(px + 7, 0); c.lineTo(px, 9); c.fill();
    }

    this.drawScrollbar(doc);
    this.drawLabels(doc);
  }

  /** The ship's path: from pickup to pickup in the tunnel and on the road, bending where a turn moves it. */
  drawPath(doc, d, from, to) {
    const c = this.ctx;
    c.lineWidth = 3;
    c.strokeStyle = "rgba(255,255,255,.2)";
    c.beginPath();
    let prev = null;
    for (const n of doc.notes[d]) {
      if (n.t < from - 3) continue;
      if (n.t > to + 3) break;
      if (n.strum) continue;
      const mode = m.modeAt(doc, n.t);
      if (mode === "Freefly") { prev = null; continue; } // flying: no track to follow
      const x = this.x(n.t), y = this.laneY(n.lane);
      if (!prev || m.modeAt(doc, prev.t) !== mode) c.moveTo(x, y);
      else if (m.isTurn(prev.color)) {
        const xm = (this.x(prev.t) + x) / 2;
        c.bezierCurveTo(xm, this.laneY(prev.lane), xm, y, x, y);
      } else c.lineTo(x, y);
      prev = n;
    }
    c.stroke();
    c.lineWidth = 1;
  }

  drawRows() {
    const c = this.ctx;
    c.strokeStyle = "rgba(255,255,255,.14)";
    for (const r of [this.rows.zones, this.rows.wave, ...this.rows.lanes, this.rows.strum]) {
      c.beginPath();
      c.moveTo(LEFT, r.y + 0.5);
      c.lineTo(this.w, r.y + 0.5);
      c.stroke();
    }
  }

  /** Row names; the lanes are named after the zone at the playhead (or at the left edge). */
  drawLabels(doc) {
    const c = this.ctx, t = this.app.t;
    c.fillStyle = "rgba(10, 7, 16, .96)";
    c.fillRect(0, 0, LEFT, this.rows.scroll.y);
    c.strokeStyle = "rgba(255,255,255,.3)";
    c.beginPath(); c.moveTo(LEFT - 0.5, 0); c.lineTo(LEFT - 0.5, this.rows.scroll.y); c.stroke();
    c.font = `700 13px ${FONT.bold}`;
    c.fillStyle = "#E1E1E1";
    const label = (text, r, color) => { c.fillStyle = color || "#E1E1E1"; c.fillText(text.toUpperCase(), 10, r.y + r.h / 2 + 5); };
    label(t("rowTime"), this.rows.ruler);
    label(t("rowZones"), this.rows.zones);
    label(t("rowWave"), this.rows.wave);
    const now = this.app.audio.time, at = now >= this.t0 && now <= this.t0 + this.visibleSeconds ? now : Math.max(0, this.t0);
    const mode = m.modeAt(doc, at);
    this.rows.lanes.forEach((r, i) => label(laneName(t, mode, i), r, MODE_COLOR[mode]));
    label(t("rowStrum"), this.rows.strum);
  }

  drawZones(doc) {
    const c = this.ctx, r = this.rows.zones, t = this.app.t;
    doc.sections.forEach((s, i) => {
      const { from: a, to: b } = m.sectionRange(doc, i);
      const x0 = Math.max(LEFT, this.x(a)), x1 = Math.min(this.w, this.x(b));
      if (x1 <= x0) return;
      const g = c.createLinearGradient(0, r.y, 0, r.y + r.h);
      g.addColorStop(0, MODE_COLOR[s.mode]); g.addColorStop(1, MODE_COLOR[s.mode] + "AA");
      c.fillStyle = g;
      c.fillRect(x0, r.y + 4, x1 - x0 - 2, r.h - 8);
      c.fillStyle = "#FFFFFF";
      c.font = `700 14px ${FONT.bold}`;
      c.save();
      c.beginPath(); c.rect(x0, r.y, x1 - x0 - 4, r.h); c.clip();
      c.fillText(t("mode" + s.mode).toUpperCase(), Math.max(x0, this.x(a)) + 8, r.y + r.h / 2 + 5);
      c.restore();
      if (i > 0 && this.x(a) >= LEFT) {
        c.fillStyle = this.hover && this.hover.kind === "boundary" && this.hover.index === i ? "#FFFFFF" : "rgba(255,255,255,.7)";
        c.fillRect(this.x(a) - 2, r.y, 4, r.h);
      }
    });
  }

  drawWave(from) {
    const c = this.ctx, r = this.rows.wave;
    const peaks = this.app.audio.peaks(from, 1 / this.pps, Math.ceil(this.width));
    if (!peaks) return;
    const mid = r.y + r.h / 2, amp = r.h / 2 - 4, played = this.app.audio.time;
    for (let x = 0; x < peaks.min.length; x++) {
      const t = from + x / this.pps;
      c.fillStyle = t < played ? "#CB33FF" : "rgba(203,51,255,.55)";
      const y0 = mid - peaks.max[x] * amp, y1 = mid - peaks.min[x] * amp;
      c.fillRect(LEFT + x, y0, 1, Math.max(1, y1 - y0));
    }
  }

  drawMarkers(doc) {
    const c = this.ctx, r = this.rows.ruler, t = this.app.t;
    const markers = [["start", doc.start, "#7CFF6B"], ["preview", doc.preview, "#4FC3F7"], ["end", doc.end, "#FF6B6B"]];
    for (const [key, time, color] of markers) {
      const x = this.x(time);
      if (x < LEFT - 40 || x > this.w + 40) continue;
      c.strokeStyle = color;
      c.beginPath(); c.moveTo(x + 0.5, r.y); c.lineTo(x + 0.5, this.rows.strum.y + this.rows.strum.h); c.stroke();
      c.fillStyle = color;
      c.font = `700 10px ${FONT.bold}`;
      const label = t("marker_" + key).toUpperCase(), w = c.measureText(label).width + 8;
      c.fillRect(x, r.y + r.h - 13, w, 12);
      c.fillStyle = "#0C0912";
      c.fillText(label, x + 4, r.y + r.h - 4);
    }
  }

  /** A soft glow in a colour: the game's Glow sprite, or a gradient. */
  glow(x, y, r, color, alpha = 0.9) {
    const c = this.ctx, image = skin.img("Glow");
    c.globalAlpha *= alpha;
    if (skin.ready(image)) c.drawImage(tint(image, color), x - r, y - r, r * 2, r * 2);
    else {
      const g = c.createRadialGradient(x, y, 0, x, y, r);
      g.addColorStop(0, color); g.addColorStop(1, color + "00");
      c.fillStyle = g;
      c.fillRect(x - r, y - r, r * 2, r * 2);
    }
    c.globalAlpha /= alpha;
  }

  /**
   * A turn: a purple tile with a big double chevron pointing the way the ship goes (left or right), and the key
   * that turns on PC beside it when the mod has told which one the player uses.
   */
  drawTurn(n, x, y, size, selected) {
    const c = this.ctx, color = KEY_COLOR[n.color], dir = n.color === m.Color.TurnLeft ? -1 : 1, w = size * 1.3, h = size;
    this.glow(x, y, size * 1.05, color);
    c.fillStyle = color;
    c.beginPath();
    c.roundRect(x - w / 2, y - h / 2, w, h, 5);
    c.fill();
    c.strokeStyle = "#FFFFFF";
    c.lineWidth = Math.max(2.5, size * 0.13);
    c.lineCap = "round";
    c.lineJoin = "round";
    for (const off of [-0.2, 0.2]) {
      const cx = x + off * w * 0.9, a = h * 0.28;
      c.beginPath();
      c.moveTo(cx - dir * a * 0.7, y - a);
      c.lineTo(cx + dir * a * 0.7, y);
      c.lineTo(cx - dir * a * 0.7, y + a);
      c.stroke();
    }
    c.lineCap = "butt";
    c.lineWidth = 1;
    const name = skin.keySprite(ACTION[n.color]), key = name ? skin.img(name) : null;
    if (skin.ready(key)) { // the player's turn key, on the side the ship goes
      const k = size * 0.95;
      c.drawImage(key, x + dir * (w / 2 + 3) - (dir < 0 ? k : 0), y - k / 2, k, k);
    }
    if (selected) {
      c.strokeStyle = "#FFFFFF";
      c.lineWidth = 2.5;
      c.strokeRect(x - w / 2 - 4, y - h / 2 - 4, w + 8, h + 8);
      c.lineWidth = 1;
    }
  }

  drawNote(n, selected, issue) {
    const c = this.ctx, x = this.x(n.t);
    const size = Math.max(18, Math.min(28, this.rows.lanes[0].h * 0.42)); // a key's size
    if (n.strum) {
      const r = this.rows.strum, y = r.y + r.h / 2;
      if (n.len > 0) {
        const x1 = this.x(n.t + n.len);
        const g = c.createLinearGradient(x, 0, x1, 0);
        g.addColorStop(0, selected ? "rgba(255,255,255,.95)" : "rgba(255,255,255,.8)"); g.addColorStop(1, "rgba(203,51,255,.45)");
        c.fillStyle = g;
        c.fillRect(x, y - 7, Math.max(0, x1 - x), 14);
        c.fillStyle = "#FFFFFF";
        c.fillRect(x1 - 2, y - 13, 4, 26);
      }
      // the key that strums on PC (Space, or the player's own), in the strum's ring
      const ring = skin.img("PickupStrum Air Glow"), name = skin.keySprite("strum"), key = name ? skin.img(name) : null;
      if (skin.ready(ring)) c.drawImage(ring, x - 24, y - 24, 48, 48);
      else this.glow(x, y, 22, "#FFFFFF", 0.35);
      const kw = 36, kh = skin.ready(key) ? (kw * key.naturalHeight) / key.naturalWidth : 22;
      if (skin.ready(key)) c.drawImage(key, x - kw / 2, y - kh / 2, kw, kh);
      else {
        c.fillStyle = "#FFFFFF";
        c.beginPath();
        c.roundRect(x - kw / 2, y - 9, kw, 18, 4);
        c.fill();
        c.fillStyle = "#0C0912";
        c.fillRect(x - 10, y + 2, 20, 2); // a space bar
      }
      if (selected) {
        c.strokeStyle = "#FFFFFF";
        c.lineWidth = 2.5;
        c.strokeRect(x - 21, y - 16, 42, 32);
        c.lineWidth = 1;
      }
    } else if (m.isTurn(n.color)) {
      this.drawTurn(n, x, this.laneY(n.lane), size, selected);
    } else {
      const y = this.laneY(n.lane), color = KEY_COLOR[n.color], half = size / 2;
      this.glow(x, y, size, color);
      const name = skin.keySprite(ACTION[n.color]), key = name ? skin.img(name) : null;
      if (skin.ready(key)) c.drawImage(key, x - half, y - half, size, size);
      else {
        c.fillStyle = "#FFFFFF";
        c.beginPath();
        c.roundRect(x - half, y - half, size, size, 4);
        c.fill();
        c.fillStyle = "#0C0912";
        c.font = `800 ${Math.round(size * 0.68)}px 'Segoe UI Symbol', sans-serif`;
        c.textAlign = "center";
        c.fillText(BUTTON_GLYPHS[n.color], x, y + size * 0.24);
        c.textAlign = "left";
      }
      // the key's colour under it, as on the game's pickups
      c.fillStyle = color;
      c.fillRect(x - half + 2, y + half - 1, size - 4, 3);
      if (selected) {
        c.strokeStyle = "#FFFFFF";
        c.lineWidth = 2.5;
        c.strokeRect(x - half - 4, y - half - 4, size + 8, size + 8);
        c.lineWidth = 1;
      }
    }
    const y = n.strum ? this.rows.strum.y + 8 : this.laneY(n.lane) - size / 2 - 3;
    if (n.set > 0) {
      c.fillStyle = "#FFD23F";
      c.font = (n.last ? "800 15px" : "700 12px") + " 'Segoe UI Symbol', sans-serif";
      c.textAlign = "center";
      c.fillText("★", x + size / 2, y);
      c.textAlign = "left";
    }
    if (issue) {
      c.strokeStyle = "#FF5252";
      c.lineWidth = 2;
      c.beginPath();
      c.arc(x, n.strum ? this.rows.strum.y + this.rows.strum.h / 2 : this.laneY(n.lane), 20, 0, Math.PI * 2);
      c.stroke();
      c.lineWidth = 1;
    }
  }

  drawScrollbar(doc) {
    const c = this.ctx, r = this.rows.scroll;
    c.fillStyle = "#181619";
    c.fillRect(LEFT, r.y, this.width, r.h);
    const total = Math.max(doc.end, 1);
    const a = LEFT + (Math.max(0, this.t0) / total) * this.width;
    const w = Math.max(24, (this.visibleSeconds / total) * this.width);
    c.fillStyle = this.drag && this.drag.kind === "scroll" ? "#FFFFFF" : "rgba(255,255,255,.55)";
    c.fillRect(a, r.y + 3, Math.min(w, LEFT + this.width - a), r.h - 6);
  }

  // ----- hit testing

  hit(x, y) {
    const doc = this.doc;
    if (!doc) return { kind: "none" };
    const t = this.time(x), R = this.rows, d = this.app.difficulty;
    if (y >= R.scroll.y) return { kind: "scroll" };
    if (x < LEFT) return { kind: "none" };
    if (y < R.ruler.y + R.ruler.h) {
      for (const key of ["start", "preview", "end"]) if (Math.abs(this.x(doc[key]) - x) < 7) return { kind: "marker", key };
      return { kind: "ruler", t };
    }
    if (y < R.zones.y + R.zones.h) {
      for (let i = 1; i < doc.sections.length; i++) if (Math.abs(this.x(doc.sections[i].t) - x) < 6) return { kind: "boundary", index: i };
      return { kind: "zone", index: m.sectionIndexAt(doc, t), t };
    }
    if (y < R.wave.y + R.wave.h) return { kind: "wave", t };
    if (y >= R.strum.y && y < R.strum.y + R.strum.h) {
      const notes = doc.notes[d].filter((n) => n.strum);
      for (const n of notes) if (n.len > 0 && Math.abs(this.x(n.t + n.len) - x) < 7) return { kind: "hold", note: n };
      const n = nearest(notes, (n) => Math.abs(this.x(n.t) - x), HIT);
      return n ? { kind: "note", note: n } : { kind: "empty", row: "strum", t };
    }
    const lane = R.lanes.findIndex((r) => y >= r.y && y < r.y + r.h);
    if (lane >= 0) {
      const n = nearest(doc.notes[d].filter((n) => !n.strum && n.lane === lane), (n) => Math.abs(this.x(n.t) - x), HIT);
      return n ? { kind: "note", note: n } : { kind: "empty", row: "lane", lane, t };
    }
    return { kind: "none" };
  }

  laneAt(y) {
    const i = this.rows.lanes.findIndex((r) => y >= r.y && y < r.y + r.h);
    return i >= 0 ? i : y < this.rows.lanes[0].y ? 0 : 2;
  }

  // ----- gestures

  pos(e) { const r = this.canvas.getBoundingClientRect(); return { x: e.clientX - r.left, y: e.clientY - r.top }; }

  down(e) {
    if (e.button !== 0 || !this.doc) return;
    this.app.closeMenu();
    const { x, y } = this.pos(e), h = this.hit(x, y), app = this.app, d = app.difficulty;
    this.canvas.setPointerCapture(e.pointerId);
    switch (h.kind) {
      case "note": {
        const id = h.note.id;
        if (e.ctrlKey || e.shiftKey) { app.toggleSelect(id); this.draw(); return; }
        if (!app.selection.has(id)) app.select([id]);
        const ids = [...app.selection];
        const origin = new Map(app.editor.doc.notes[d].filter((n) => ids.includes(n.id)).map((n) => [n.id, { t: n.t, lane: n.lane }]));
        this.drag = { kind: "move", x0: x, y0: y, grab: h.note.t, lane0: this.laneAt(y), ids, origin, snap: app.editor.snapshot() };
        app.seekPreview(h.note.t);
        break;
      }
      case "hold":
        app.select([h.note.id]);
        this.drag = { kind: "hold", id: h.note.id, snap: app.editor.snapshot() };
        break;
      case "empty":
        this.drag = { kind: "box", x0: x, y0: y, x1: x, y1: y, hit: h, moved: false, additive: e.ctrlKey || e.shiftKey };
        break;
      case "boundary":
        this.drag = { kind: "boundary", index: h.index, snap: app.editor.snapshot() };
        break;
      case "marker":
        this.drag = { kind: "marker", key: h.key, snap: app.editor.snapshot() };
        break;
      case "ruler":
      case "wave":
      case "zone":
        this.drag = { kind: "seek" };
        app.seek(Math.max(0, h.t));
        break;
      case "scroll":
        this.drag = { kind: "scroll", x0: x, t00: this.t0 };
        break;
    }
    this.draw();
  }

  move(e) {
    const { x, y } = this.pos(e), app = this.app, doc = this.doc;
    if (!doc) return;
    const drag = this.drag;
    if (!drag) {
      const h = this.hit(x, y);
      this.hover = h;
      this.canvas.style.cursor = h.kind === "boundary" || h.kind === "marker" || h.kind === "hold" ? "ew-resize" : h.kind === "note" ? "grab" : h.kind === "empty" ? "crosshair" : "default";
      app.showCursor(this.time(x), h);
      return;
    }
    const d = app.difficulty, t = this.time(x);
    switch (drag.kind) {
      case "move": {
        if (Math.abs(x - drag.x0) < 3 && Math.abs(y - drag.y0) < 3 && !drag.moved) return;
        drag.moved = true;
        const target = e.altKey ? drag.grab + (x - drag.x0) / this.pps : m.snap(doc, drag.grab + (x - drag.x0) / this.pps, app.division);
        const dt = target - drag.grab, dLane = this.laneAt(y) - drag.lane0;
        for (const n of doc.notes[d]) {
          const o = drag.origin.get(n.id);
          if (!o) continue;
          n.t = Math.max(0, Math.min(doc.end, o.t + dt));
          if (!n.strum) n.lane = Math.max(0, Math.min(2, o.lane + dLane));
        }
        m.normalize(doc);
        app.changed(false);
        break;
      }
      case "hold": {
        const n = doc.notes[d].find((n) => n.id === drag.id);
        if (n) m.setHold(doc, d, n.id, (e.altKey ? t : m.snap(doc, t, app.division)) - n.t);
        app.changed(false);
        break;
      }
      case "box":
        drag.x1 = x;
        drag.y1 = y;
        drag.moved = drag.moved || Math.abs(x - drag.x0) > 4 || Math.abs(y - drag.y0) > 4;
        break;
      case "boundary":
        m.moveSection(doc, drag.index, e.altKey ? t : m.snap(doc, t, app.division));
        m.normalize(doc);
        app.changed(false);
        break;
      case "marker":
        m.setMeta(doc, { [drag.key]: e.altKey ? t : m.snap(doc, t, app.division) });
        app.changed(false);
        break;
      case "seek":
        app.seek(Math.max(0, t));
        break;
      case "scroll":
        this.t0 = drag.t00 + ((x - drag.x0) / this.width) * Math.max(doc.end, 1);
        this.clamp();
        break;
    }
    this.draw();
  }

  up(e) {
    const drag = this.drag, app = this.app, doc = this.doc;
    this.drag = null;
    if (!drag || !doc) return;
    const d = app.difficulty;
    switch (drag.kind) {
      case "move":
      case "hold":
      case "boundary":
      case "marker":
        if (app.editor.commit(drag.snap)) app.changed(true);
        break;
      case "box":
        if (drag.moved) {
          const ta = this.time(Math.min(drag.x0, drag.x1)), tb = this.time(Math.max(drag.x0, drag.x1));
          const ya = Math.min(drag.y0, drag.y1), yb = Math.max(drag.y0, drag.y1);
          const ids = doc.notes[d].filter((n) => {
            if (n.t < ta || n.t > tb) return false;
            const y = n.strum ? this.rows.strum.y + this.rows.strum.h / 2 : this.laneY(n.lane);
            return y >= ya - 10 && y <= yb + 10;
          }).map((n) => n.id);
          app.select(drag.additive ? [...app.selection, ...ids] : ids);
        } else {
          const h = drag.hit, t = e.altKey ? h.t : m.snap(doc, h.t, app.division);
          app.addNoteAt(t, h.row === "strum" ? null : h.lane);
        }
        break;
    }
    this.draw();
  }

  dblclick(e) {
    const { x, y } = this.pos(e), h = this.hit(x, y), app = this.app, doc = this.doc;
    if (!doc || h.kind !== "zone") return;
    const mode = doc.sections[h.index].mode;
    const next = m.MODES[(m.MODES.indexOf(mode) + 1) % m.MODES.length];
    app.edit((doc) => m.addSection(doc, m.snap(doc, h.t, app.division), next));
  }

  context(e) {
    const { x, y } = this.pos(e), h = this.hit(x, y), app = this.app;
    if (!this.doc) return;
    if (h.kind === "note") {
      const ids = app.selection.has(h.note.id) ? [...app.selection] : [h.note.id];
      app.edit((doc) => m.removeNotes(doc, app.difficulty, ids));
      app.select([]);
    } else if (h.kind === "zone" || h.kind === "boundary") {
      app.zoneMenu(h.index, e.clientX, e.clientY);
    }
  }

  wheel(e) {
    e.preventDefault();
    if (!this.doc) return;
    const { x } = this.pos(e);
    if (e.ctrlKey) this.zoom(e.deltaY < 0 ? 1.2 : 1 / 1.2, Math.max(LEFT, x));
    else {
      this.t0 += ((e.deltaY + e.deltaX) / this.pps) * 0.9;
      this.clamp();
      this.draw();
    }
  }
}

function nearest(items, distance, max) {
  let best = null, bestD = max;
  for (const it of items) {
    const dd = distance(it);
    if (dd <= bestD) { best = it; bestD = dd; }
  }
  return best;
}

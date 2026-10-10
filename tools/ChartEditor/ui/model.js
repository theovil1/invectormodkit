// The chart being edited, and every edit as a plain function. No DOM here: tested with `node --test`.
// A document is the *.chart.json content (ChartJson) with every note spelled out and given an id.

export const DIFFICULTIES = ["Casual", "Normal", "Hard", "Ultra"];
export const MODES = ["List", "Bounce", "Freefly"];
/** Buttons a pickup asks for (the game's EntityType values). */
export const Color = { Left: 0, Right: 1, Down: 2, Up: 3, TurnLeft: 4, TurnRight: 5 };
export const isTurn = (c) => c === Color.TurnLeft || c === Color.TurnRight;

let nextId = 1;
const newId = () => nextId++;

const r = (v, digits) => Math.round(v * 10 ** digits) / 10 ** digits;

/** A note with every field present. */
export function makeNote(fields) {
  const strum = !!fields.strum;
  return {
    id: newId(),
    t: fields.t,
    strum,
    len: strum ? Math.max(0, fields.len || 0) : 0,
    lane: Number.isInteger(fields.lane) ? fields.lane : 1,
    color: strum ? Color.Down : Number.isInteger(fields.color) ? fields.color : Color.Down,
    set: fields.set || 0,
    last: !!fields.last,
  };
}

/** From the file format (omitted fields take their defaults) to an editable document. */
export function fromFile(json) {
  const doc = {
    format: json.format || 1,
    bpm: json.bpm,
    firstBeat: json.firstBeat || 0,
    start: json.start || 0,
    end: json.end,
    preview: json.preview ?? json.end * 0.3,
    gain: json.gain,
    sections: (json.sections && json.sections.length ? json.sections : [{ t: 0, mode: "List" }]).map((s) => ({ t: s.t, mode: s.mode })),
    notes: {},
  };
  for (const d of DIFFICULTIES) doc.notes[d] = ((json.notes && json.notes[d]) || []).map(makeNote);
  return normalize(doc);
}

/** Back to the file format, as ChartJson writes it (defaults left out). */
export function toFile(doc) {
  const out = {
    format: doc.format || 1,
    bpm: r(doc.bpm, 4),
    firstBeat: r(doc.firstBeat, 4),
    start: r(doc.start, 3),
    end: r(doc.end, 3),
    preview: r(doc.preview, 3),
  };
  if (doc.gain !== undefined && doc.gain !== null) out.gain = doc.gain;
  out.sections = doc.sections.map((s) => ({ t: r(s.t, 3), mode: s.mode }));
  out.notes = {};
  for (const d of DIFFICULTIES)
    out.notes[d] = doc.notes[d].map((n) => {
      const o = { t: r(n.t, 4) };
      if (n.strum) {
        o.strum = true;
        if (n.len > 0) o.len = r(n.len, 3);
      } else {
        if (n.lane !== 1) o.lane = n.lane;
        if (n.color !== Color.Down) o.color = n.color;
      }
      if (n.set > 0) o.set = n.set;
      if (n.last) o.last = true;
      return o;
    });
  return out;
}

// ----- time and grid

export const secondsPerBeat = (doc) => 60 / doc.bpm;

/**
 * Nearest grid time. `division` = grid lines per beat (1 = beats, 2 = eighths, 3 = triplets, 4 = sixteenths…).
 */
export function snap(doc, t, division) {
  const step = secondsPerBeat(doc) / division;
  return doc.firstBeat + Math.round((t - doc.firstBeat) / step) * step;
}

/** Grid lines between two times: { t, level } with level 0 = bar (4 beats), 1 = beat, 2 = subdivision. */
export function gridLines(doc, from, to, division) {
  const step = secondsPerBeat(doc) / division;
  const lines = [];
  let k = Math.ceil((from - doc.firstBeat) / step);
  for (let t = doc.firstBeat + k * step; t <= to && lines.length < 20000; k++, t = doc.firstBeat + k * step) {
    const level = k % division !== 0 ? 2 : Math.round(k / division) % 4 === 0 ? 0 : 1;
    lines.push({ t, level, bar: level === 0 ? Math.round(k / division / 4) + 1 : 0 });
  }
  return lines;
}

/** BPM from tapped times (seconds): median interval, so one late tap does not matter. */
export function tapTempo(times) {
  if (times.length < 3) return null;
  const gaps = times.slice(1).map((t, i) => t - times[i]).sort((a, b) => a - b);
  const median = gaps[Math.floor(gaps.length / 2)];
  return median > 0 ? r(60 / median, 2) : null;
}

// ----- zones

export function sectionIndexAt(doc, t) {
  let index = 0;
  doc.sections.forEach((s, i) => { if (t >= s.t - 1e-6) index = i; });
  return index;
}

export const modeAt = (doc, t) => doc.sections[sectionIndexAt(doc, t)].mode;

/** Start and end of a zone. */
export function sectionRange(doc, index) {
  return { from: doc.sections[index].t, to: index + 1 < doc.sections.length ? doc.sections[index + 1].t : doc.end };
}

// ----- the game's lane rule

/** Lane of the pickups that follow a turn (List wraps around the tube, Bounce's road does not). */
export function laneAfterTurn(mode, lane, color) {
  const delta = color === Color.TurnLeft ? -1 : 1;
  return mode === "List" ? (lane + delta + 3) % 3 : Math.max(0, Math.min(2, lane + delta));
}

/**
 * In List and Bounce a pickup's lane only changes through a turn: every pickup after the first of a run in the
 * same mode takes the lane the previous one leads to. Freefly positions are free.
 */
export function relane(doc, d) {
  let prev = null, prevMode = null;
  for (const n of doc.notes[d]) {
    if (n.strum) continue;
    const mode = modeAt(doc, n.t);
    if (mode !== "Freefly" && prev && prevMode === mode)
      n.lane = isTurn(prev.color) ? laneAfterTurn(mode, prev.lane, prev.color) : prev.lane;
    prev = n;
    prevMode = mode;
  }
}

/** Is this note's lane chosen by hand (Freefly, or the first pickup of a run) rather than by the turns? */
export function laneIsFree(doc, d, note) {
  if (note.strum) return false;
  const mode = modeAt(doc, note.t);
  if (mode === "Freefly") return true;
  const pickups = doc.notes[d].filter((n) => !n.strum && n.t < note.t);
  const prev = pickups[pickups.length - 1];
  return !prev || modeAt(doc, prev.t) !== mode;
}

/** Sorted notes, sorted zones starting at 0, lanes following the turns, times inside the song. */
export function normalize(doc) {
  doc.sections.sort((a, b) => a.t - b.t);
  if (!doc.sections.length || doc.sections[0].t > 0) doc.sections.unshift({ t: 0, mode: doc.sections[0]?.mode || "List" });
  doc.sections[0].t = 0;
  for (const d of DIFFICULTIES) {
    doc.notes[d] = doc.notes[d].filter((n) => n.t >= 0 && n.t <= doc.end).sort((a, b) => a.t - b.t || a.id - b.id);
    relane(doc, d);
  }
  return doc;
}

// ----- edits (each mutates the document; Editor.apply keeps the history)

export function addNote(doc, d, fields) {
  const note = makeNote(fields);
  doc.notes[d].push(note);
  return note.id;
}

export function removeNotes(doc, d, ids) {
  const set = new Set(ids);
  doc.notes[d] = doc.notes[d].filter((n) => !set.has(n.id));
}

const byIds = (doc, d, ids) => { const set = new Set(ids); return doc.notes[d].filter((n) => set.has(n.id)); };

/** Shifts notes in time; `dLane` moves the lanes that are chosen by hand (the others follow the turns). */
export function moveNotes(doc, d, ids, dt, dLane = 0) {
  for (const n of byIds(doc, d, ids)) {
    n.t = Math.max(0, Math.min(doc.end, n.t + dt));
    if (dLane && !n.strum) n.lane = Math.max(0, Math.min(2, n.lane + dLane));
  }
}

/** Hold length of strums (pickups cannot be held). */
export function setHold(doc, d, id, len) {
  const n = byIds(doc, d, [id])[0];
  if (n && n.strum) n.len = Math.max(0, Math.min(len, doc.end - n.t));
}

export function setColor(doc, d, ids, color) {
  for (const n of byIds(doc, d, ids)) if (!n.strum) n.color = color;
}

/** Turns notes into strums (or back into pickups). */
export function setStrum(doc, d, ids, strum) {
  for (const n of byIds(doc, d, ids)) {
    n.strum = strum;
    if (!strum) n.len = 0;
    else n.color = Color.Down;
  }
}

export function setLane(doc, d, ids, lane) {
  for (const n of byIds(doc, d, ids)) if (!n.strum) n.lane = Math.max(0, Math.min(2, lane));
}

/** The chosen notes become one power-up star set (its last note marked). */
export function makeStarSet(doc, d, ids) {
  const notes = byIds(doc, d, ids).sort((a, b) => a.t - b.t);
  if (!notes.length) return 0;
  const id = Math.max(0, ...doc.notes[d].map((n) => n.set)) + 1;
  for (const n of notes) { n.set = id; n.last = false; }
  notes[notes.length - 1].last = true;
  return id;
}

export function clearStarSet(doc, d, ids) {
  for (const n of byIds(doc, d, ids)) { n.set = 0; n.last = false; }
}

/** Notes with from <= t < to. */
export const notesIn = (doc, d, from, to) => doc.notes[d].filter((n) => n.t >= from - 1e-6 && n.t < to - 1e-6);

/** Replaces the notes of `d` between from and to by copies of `notes` in that range (regeneration, copy). */
export function replaceRange(doc, d, notes, from, to) {
  doc.notes[d] = doc.notes[d].filter((n) => n.t < from - 1e-6 || n.t >= to - 1e-6);
  for (const n of notes) if (n.t >= from - 1e-6 && n.t < to - 1e-6) doc.notes[d].push(makeNote(n));
}

export function copyRange(doc, fromD, toD, from, to) {
  if (fromD !== toD) replaceRange(doc, toD, notesIn(doc, fromD, from, to), from, to);
}

/** Clipboard: notes with times relative to the first one. */
export function copyNotes(doc, d, ids) {
  const notes = byIds(doc, d, ids).sort((a, b) => a.t - b.t);
  if (!notes.length) return [];
  const t0 = notes[0].t;
  return notes.map((n) => ({ ...n, t: n.t - t0 }));
}

export function paste(doc, d, clipboard, at) {
  return clipboard.map((n) => addNote(doc, d, { ...n, t: at + n.t }));
}

// zones

/** Cuts the zone playing at t in two (the new part takes `mode`). */
export function addSection(doc, t, mode) {
  if (t <= 0.05 || t >= doc.end) return;
  if (doc.sections.some((s) => Math.abs(s.t - t) < 0.05)) return;
  doc.sections.push({ t, mode });
}

/** Removes a zone boundary (the first zone always stays). */
export function removeSection(doc, index) {
  if (index > 0 && index < doc.sections.length) doc.sections.splice(index, 1);
}

/** Moves a boundary between its neighbours. */
export function moveSection(doc, index, t) {
  if (index <= 0 || index >= doc.sections.length) return;
  const lo = doc.sections[index - 1].t + 0.1;
  const hi = index + 1 < doc.sections.length ? doc.sections[index + 1].t - 0.1 : doc.end - 0.1;
  doc.sections[index].t = Math.max(lo, Math.min(hi, t));
}

export function setSectionMode(doc, index, mode) {
  if (doc.sections[index] && MODES.includes(mode)) doc.sections[index].mode = mode;
}

/** BPM, first beat, start, end, preview. */
export function setMeta(doc, fields) {
  for (const key of ["bpm", "firstBeat", "start", "end", "preview"])
    if (typeof fields[key] === "number" && Number.isFinite(fields[key])) doc[key] = fields[key];
  doc.bpm = Math.max(20, Math.min(400, doc.bpm));
  doc.end = Math.max(1, doc.end);
  doc.start = Math.max(0, Math.min(doc.end, doc.start));
  doc.preview = Math.max(0, Math.min(doc.end, doc.preview));
}

// ----- history

const clone = (doc) => (typeof structuredClone === "function" ? structuredClone(doc) : JSON.parse(JSON.stringify(doc)));

/** The document with unlimited undo/redo (snapshots; a chart is small). */
export class Editor {
  constructor(doc) {
    this.doc = doc;
    this.undoStack = [];
    this.redoStack = [];
    this.dirty = false;
  }

  /** A copy of the document before a gesture (drag) that edits it live. */
  snapshot() { return clone(this.doc); }

  /** Ends a live gesture: the snapshot becomes one undo step (nothing happens if the document did not change). */
  commit(snapshot) {
    normalize(this.doc);
    if (JSON.stringify(snapshot) === JSON.stringify(this.doc)) return false;
    this.undoStack.push(snapshot);
    if (this.undoStack.length > 1000) this.undoStack.shift();
    this.redoStack = [];
    this.dirty = true;
    return true;
  }

  /** Runs an edit; returns what the edit returned. */
  apply(edit) {
    const before = clone(this.doc);
    const result = edit(this.doc);
    normalize(this.doc);
    this.undoStack.push(before);
    if (this.undoStack.length > 1000) this.undoStack.shift();
    this.redoStack = [];
    this.dirty = true;
    return result;
  }

  undo() {
    if (!this.undoStack.length) return false;
    this.redoStack.push(clone(this.doc));
    this.doc = this.undoStack.pop();
    this.dirty = true;
    return true;
  }

  redo() {
    if (!this.redoStack.length) return false;
    this.undoStack.push(clone(this.doc));
    this.doc = this.redoStack.pop();
    this.dirty = true;
    return true;
  }
}

// A perfect run through a chart, by the game's rules, as functions of song time: score level, walker speed and
// field of view, distance travelled, where each note sits, which notes are in view.
//
// The level (PlayerLevelPump): every note taken perfectly adds 3 to the pump (the game's own max score simulation
// counts every note); the pump stops at the level's limit; when it fills, the first pickupable note (not a plain
// strum) after the nearest beat plus 3 beats (4 if that beat was already behind) becomes the level-up pickup
// (PlacePickupsOnPath.MakeLevelUpPickup), and taking it raises the level and empties the pump. At the top level the
// pump keeps filling and marking level-up pickups, without raising the level.
// A level change tweens the walker's speed (Walker.SetSpeed) and the field of view (GameCamera.AnimateGroundValues)
// linearly over 1 s, from wherever they were.
import { modeAt as modeOf, sectionIndexAt } from "../model.js";

const TWEEN = 1;

/** A value that changes by linear tweens: keypoints [t, v], constant before the first and after the last. */
function tweened(start) {
  const pts = [[0, start]];
  const at = (t) => {
    if (t <= pts[0][0]) return pts[0][1];
    for (let i = 1; i < pts.length; i++) {
      const [t1, v1] = pts[i];
      if (t <= t1) { const [t0, v0] = pts[i - 1]; return t1 > t0 ? v0 + ((v1 - v0) * (t - t0)) / (t1 - t0) : v1; }
    }
    return pts[pts.length - 1][1];
  };
  const tweenTo = (t, v) => {
    const from = at(t);
    while (pts.length > 1 && pts[pts.length - 1][0] > t) pts.pop(); // a tween still running is replaced
    pts.push([t, from], [t + TWEEN, v]);
  };
  return { pts, at, tweenTo };
}

/** The integral of a piecewise linear function given by keypoints, from 0 to t (constant outside the points). */
function integral(pts) {
  const prefix = [0];
  for (let i = 1; i < pts.length; i++) prefix.push(prefix[i - 1] + ((pts[i][1] + pts[i - 1][1]) / 2) * (pts[i][0] - pts[i - 1][0]));
  return (t) => {
    if (t <= pts[0][0]) return pts[0][1] * (t - pts[0][0]);
    let lo = 0, hi = pts.length - 1;
    if (t >= pts[hi][0]) return prefix[hi] + pts[hi][1] * (t - pts[hi][0]);
    while (hi - lo > 1) { const mid = (lo + hi) >> 1; if (pts[mid][0] <= t) lo = mid; else hi = mid; }
    const [t0, v0] = pts[lo], [t1, v1] = pts[hi], v = v0 + ((v1 - v0) * (t - t0)) / (t1 - t0);
    return prefix[lo] + ((v0 + v) / 2) * (t - t0);
  };
}

/** True for the notes PlacePickupsOnPath can make a level-up pickup of: anything but a plain strum. */
const pickupable = (n) => !n.strum || n.len > 0;

export function prepareRun(doc, diff, rig, { tier = null } = {}) {
  const tiers = rig.tiers;
  const sections = doc.sections.map((s, i) => ({ mode: s.mode, from: s.t, to: i + 1 < doc.sections.length ? doc.sections[i + 1].t : doc.end }));
  const notes = (doc.notes[diff] || []).map((n) => ({ ...n, mode: modeOf(doc, n.t), levelUp: false })).sort((a, b) => a.t - b.t);
  const spb = 60 / doc.bpm, fb = doc.firstBeat || 0;

  const startLevel = tier === null ? 0 : tier;
  const speed = tweened(tiers[startLevel].speed), fov = tweened(tiers[startLevel].fov);
  const levels = [{ t: 0, level: startLevel }];

  if (tier === null) {
    let level = 0, pump = 0, levelUpAfter = -1;
    for (const n of notes) {
      if (n.t < 0) continue; // the game's simulation skips notes before the song
      const limit = tiers[level].limit;
      if (pump >= limit) {
        if (levelUpAfter >= 0 && n.t > levelUpAfter + 1e-9 && pickupable(n)) {
          n.levelUp = true;
          pump = 0;
          levelUpAfter = -1;
          if (level < tiers.length - 1) {
            level++;
            levels.push({ t: n.t, level });
            speed.tweenTo(n.t, tiers[level].speed);
            fov.tweenTo(n.t, tiers[level].fov);
          }
        }
      } else {
        pump = Math.min(limit, pump + 3);
        if (pump >= limit) {
          const beat = fb + Math.round((n.t - fb) / spb) * spb;
          levelUpAfter = beat >= n.t - 1e-9 ? beat + 3 * spb : beat + 4 * spb;
        }
      }
    }
  }

  const distAt = integral(speed.pts);
  const levelAt = (t) => { let l = levels[0].level; for (const e of levels) if (t >= e.t) l = e.level; return l; };
  const sectionIndex = (t) => sectionIndexAt(doc, t);

  return {
    notes,
    levels,
    sections,
    levelAt,
    speedAt: speed.at,
    fovAt: fov.at,
    distAt,
    /** Where a note at noteT sits at time t: ahead of the walker by its time to come at the current speed (Pickup.UpdatePosition). */
    noteDist: (noteT, t) => distAt(t) + (noteT - t) * speed.at(t) + rig.hitAhead,
    /** The notes in view at t: from 0.15 s past to 3 s ahead, Freefly rings 5 s ahead, holds until 1 s after their end. */
    visible: (t) => notes.filter((n) => {
      const ahead = n.mode === "Freefly" && !n.strum ? 5 : 3;
      if (n.t > t + ahead) return false;
      return n.t >= t - 0.15 || (n.len > 0 && n.t + n.len + 1 >= t);
    }),
    modeAt: (t) => modeOf(doc, t),
    sectionIndex,
  };
}

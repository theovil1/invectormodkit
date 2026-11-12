// The ship through a perfect run: its lane (turns move it), the Bounce sway, the Freefly flight simulated from the
// game's steering, the barrel rolls after each Freefly ring, and its place and turn in the world. All functions of
// time: the Freefly flight is simulated once per stretch from its start and then read at any time.
import { laneAfterTurn, isTurn } from "../model.js";
import { hermite } from "./curves.js";
import { clamp01, hash01, qmul, qfromAxisAngle, qfromEulerYXZ, qfromBasis } from "./math.js";

const STEER = { Freefly: "Fly", Bounce: "Coast", List: "Roll" }; // each generator's SteerMode
const rad = (d) => (d * Math.PI) / 180;
const SHIP_DY = -1; // the prototype's ship height fix, kept as it matched the recorded frames
/** The ship model's scale: the ship prefab's mount (x1.4) inside the walker's ShipCoaster (x1.5), the prototype's measure. */
export const SHIP_SCALE = 2.1;

/** A roll clip's rotation track as the unwrapped roll angle (degrees) at 60 frames a second. */
export function rollAngles(clip) {
  const track = clip.tracks.find((t) => t.attribute === "rotation");
  const angles = [];
  let prev = null, acc = 0;
  for (let i = 0; i + 3 < track.values.length; i += 4) {
    const qz = track.values[i + 2], qw = track.values[i + 3];
    const a = (2 * Math.atan2(qz, qw) * 180) / Math.PI;
    if (prev !== null) {
      let d = a - prev;
      while (d > 180) d -= 360;
      while (d < -180) d += 360;
      acc += d;
    }
    prev = a;
    angles.push(acc);
  }
  return { length: clip.length, fps: clip.fps || 60, angles };
}

/** A fly area from the level file: { z, circleY, circleRadius, groundY, pickup: { x, y } } as an array. */
const flyRow = (f) => (Array.isArray(f) ? f : [f.z, f.circleY, f.circleRadius, f.groundY, f.pickup.x, f.pickup.y]);

export function prepareShip(run, rig, level, rolls) {
  const notes = run.notes, sections = run.sections, L = level.length;
  const areas = (level.fly || []).map(flyRow);
  const transition = hermite(rig.shipTransition), flyControl = hermite(rig.flyControl), flyOffsetCurve = hermite(rig.flyOffset);
  const rollClips = Object.fromEntries(Object.entries(rolls).map(([k, c]) => [k, rollAngles(c)]));
  const walkerDist = (t) => level.start + run.distAt(t);
  const plain = notes.filter((n) => !n.strum);

  function laneAfter(n, mode) { return isTurn(n.color) ? laneAfterTurn(mode, n.lane ?? 1, n.color) : n.lane ?? 1; }

  function lane(t, mode) {
    let prev = null, next = null;
    for (const n of plain) { if (n.mode !== mode) continue; if (n.t <= t) prev = n; else { next = n; break; } }
    const l = prev ? laneAfter(prev, mode) : next ? next.lane ?? 1 : 1;
    return { lane: l, prev, next };
  }

  /** Bounce: the ship eases to a turn's new lane over the steering transition, and to the next pickup's lane before it. */
  function bounceX(t) {
    const { lane: l, prev, next } = lane(t, "Bounce");
    let x = rig.laneX * (l - 1);
    if (prev && isTurn(prev.color)) {
      const u = (t - prev.t) / rig.steer.transition;
      if (u < 1) {
        const before = rig.laneX * ((prev.lane ?? 1) - 1), k = transition(clamp01(u));
        x = before + (x - before) * k;
      }
    }
    if (next && (next.lane ?? 1) !== l) {
      const start = next.t - Math.min(0.3, next.t - (prev ? prev.t : next.t - 0.3));
      const k = clamp01((t - start) / Math.max(0.05, next.t - start)), s = k * k * (3 - 2 * k);
      x += rig.laneX * ((next.lane ?? 1) - l) * s;
    }
    return x;
  }

  /** SplineEditor.GetFlyArea + FlyArea.InverseLerp at a distance along the level. */
  function flyArea(dist) {
    if (!areas.length) return { circleY: 1, radius: 20, groundY: -20, px: 0, py: 0 };
    const z = dist - Math.floor(dist / L) * L;
    const i = areas.findIndex((f) => z <= f[0]);
    let a, b;
    if (i === 0) { a = areas[areas.length - 1].slice(); a[0] -= L; b = areas[0]; }
    else if (i > 0) { a = areas[i - 1]; b = areas[i]; }
    else { a = areas[areas.length - 1]; b = areas[0].slice(); b[0] += L; }
    const k = b[0] > a[0] ? clamp01((z - a[0]) / (b[0] - a[0])) : 0, l = (j) => a[j] + (b[j] - a[j]) * k;
    return { circleY: l(1), radius: l(2), groundY: l(3), px: l(4), py: l(5) };
  }
  const ringAt = (t) => flyArea(walkerDist(t) + rig.hitAhead);

  // Freefly, frame by frame at 60 fps from the start of the stretch (Steering.UpdateFreeFly with the AutoPilot's
  // stick): the stick points at the next ring (once spawned, 5 s ahead) at 0.1 x the distance, shaped by FlyControl,
  // smoothed by the spring (10 ms steps), moves the ship at FlySteerSpeed, kept in the fly area 10 ahead; it leans by
  // the stick. A ring is taken within 1/30 s of its time; the ship then barrel-rolls 1, 2 or 3 turns.
  const flights = new Map();
  function flight(si) {
    if (flights.has(si)) return flights.get(si);
    const sec = sections[si], dt = 1 / 60, S = rig.steer;
    const rings = plain.filter((n) => n.t >= sec.from && n.t < sec.to);
    let x = si > 0 && sections[si - 1].mode === "Bounce" ? bounceX(sec.from - 1e-3) : 0, y = 0;
    let vx = 0, vy = 0, acc = 0, pitch = 0, yaw = 0, roll = 0, ri = 0, prevTx = 0;
    const frames = [], picks = [];
    const aimY = -rig.pickupY - 0.15; // the rings' point less the pickups' vertical offset and 0.15
    for (let f = 0; sec.from + f * dt <= sec.to + 1e-6; f++) {
      const now = sec.from + f * dt;
      let tx = 0, ty = 0;
      if (ri < rings.length && rings[ri].t - now <= 5) {
        const a = ringAt(rings[ri].t);
        tx = (a.px - x) * 0.1; ty = (a.py + aimY - y) * 0.1;
      }
      while (ri < rings.length && now >= rings[ri].t - 1 / 30) {
        const ra = Math.floor(hash01(si * 1000 + ri) * 30);
        let right = prevTx > 0 || (prevTx === 0 && hash01(si * 1000 + ri + 500) < 0.5);
        // the roll sub-state machine: RandomAnimation 1-9: 3 turns, 11-19: 1 turn, 21-29: 2 turns, else "Right 3 Turns"
        let turns = ra > 0 && ra < 10 ? 3 : ra > 10 && ra < 20 ? 1 : ra > 20 ? 2 : 0;
        if (!turns) { turns = 3; right = true; }
        picks.push({ t: now, right, turns });
        ri++;
      }
      const m = Math.hypot(tx, ty);
      if (m > 0) { const c = flyControl(m) / m; tx *= c; ty *= c; }
      acc += dt;
      while (acc > 0.01) {
        vx = (vx + (tx - vx) * S.spring * 0.01) * (1 - S.damp * 0.01);
        vy = (vy + (ty - vy) * S.spring * 0.01) * (1 - S.damp * 0.01);
        acc -= 0.01;
      }
      x += vx * S.speed * dt; y += vy * S.speed * dt;
      const ar = flyArea(walkerDist(now) + 10);
      let cx = x, cy = Math.max(y, ar.groundY + 1);
      const dy = cy - ar.circleY, r = Math.hypot(cx, dy);
      if (r > ar.radius) { cx = (cx / r) * ar.radius; cy = ar.circleY + (dy / r) * ar.radius; }
      if (cx !== x || cy !== y) { x = cx + (x - cx) * 0.7; y = cy + (y - cy) * 0.7; }
      pitch += ((-ty - vy) * 0.5 * S.limitPitch - pitch) * Math.min(1, dt * S.pitchSpeed);
      yaw += ((tx + vx) * 0.5 * S.limitYaw - yaw) * Math.min(1, dt * S.yawSpeed);
      roll += ((-tx - vx) * 0.5 * S.limitRoll - roll) * Math.min(1, dt * S.rollSpeed);
      frames.push({ t: now, x, y, pitch, yaw, roll, steer: tx * tx + ty * ty > 0.1 });
      prevTx = tx;
    }
    const result = { frames, picks };
    flights.set(si, result);
    return result;
  }

  function flyFrame(si, t) {
    const fr = flight(si).frames;
    if (!fr.length) return { x: 0, y: 0, pitch: 0, yaw: 0, roll: 0, steer: false };
    const p = Math.min(fr.length - 1, Math.max(0, (t - sections[si].from) * 60));
    const i = Math.floor(p), a = fr[i], b = fr[Math.min(fr.length - 1, i + 1)], u = p - i, l = (k) => a[k] + (b[k] - a[k]) * u;
    return { x: l("x"), y: l("y"), pitch: l("pitch"), yaw: l("yaw"), roll: l("roll"), steer: a.steer };
  }

  /** The barrel roll's angle: the last roll clip started (state speed 0.65), cross-faded over 0.1 s from the one before. */
  function rollClip(p, t) {
    const c = rollClips[p.turns];
    if (!c) return 0;
    const ct = (t - p.t) * 0.65;
    if (ct < 0) return 0;
    if (ct >= c.length) {
      // the animator blends back to level: the clip's last angle, nearest to 0, fades out over 0.1 s
      const last = c.angles[c.angles.length - 1], end = last - Math.round(last / 360) * 360;
      const u = (t - p.t - c.length / 0.65) / 0.1;
      return u < 1 ? (p.right ? end : -end) * (1 - u) : 0;
    }
    const x = Math.min(c.angles.length - 1, ct * c.fps), i = Math.floor(x), j = Math.min(c.angles.length - 1, i + 1);
    const v = c.angles[i] + (c.angles[j] - c.angles[i]) * (x - i);
    return p.right ? v : -v;
  }
  function barrelAt(si, t) {
    const picks = flight(si).picks;
    let n = 0;
    while (n < picks.length && picks[n].t <= t) n++;
    const angle = (k, at) => {
      if (k < 0) return 0;
      const own = rollClip(picks[k], at), w = clamp01((at - picks[k].t) / 0.1);
      if (w >= 1) return own;
      const from = angle(k - 1, picks[k].t);
      let d = own - from;
      d -= Math.round(d / 360) * 360;
      return from + d * w;
    };
    return angle(n - 1, t);
  }

  /** How many rings the stretch has taken (the Freefly trail tier), and the OnSteer trails' fade. */
  function flyStretch(si, t) {
    const sim = flight(si), sec = sections[si];
    const taken = sim.picks.filter((p) => p.t <= t);
    const tier = Math.min(3, taken.length), tierSince = tier ? taken[tier - 1].t : sec.from;
    let show = false, since = sec.from, lastShow = -Infinity;
    for (const f of sim.frames) { if (f.t > t) break; if (f.steer !== show) { show = f.steer; since = f.t; if (show) lastShow = f.t; } }
    const u = clamp01((t - since) / 0.5);
    if (lastShow === -Infinity) return { tier, tierSince, steer: 0 };
    return show ? { tier, tierSince, steer: u, steerTime: 1, steerSince: since } : { tier, tierSince, steer: 1 - u, steerTime: 1 - u, steerSince: lastShow };
  }

  /** CameraController.DidPickupInFlyMode: a ring starts the pull-back curve unless one is already running. */
  function flyOffsetAt(t) {
    const F = rig.fly;
    let start = -1000, tAt = 0;
    for (const n of plain) {
      if (n.mode !== "Freefly") continue;
      const tp = n.t - 1 / 30;
      if (tp > t) break;
      const u = (tp - start) / F.pickupDuration;
      if (u > 0 && u < 1) tAt = flyOffsetCurve(u); else { tAt = 0; start = tp; }
    }
    const u = (t - start) / F.pickupDuration;
    if (!(u > 0 && u < 1)) return F.offset;
    let k = flyOffsetCurve(u);
    if (u < 0.25 && tAt > 0) k = tAt + (k - tAt) * (u + (1 - u) / 2);
    return F.offset.map((v, i) => v + (F.offsetAtPickup[i] - v) * k);
  }

  function shipEnd(si) {
    const s = sections[si];
    if (s.mode === "Freefly") { const f = flyFrame(si, s.to); return { x: f.x, y: f.y }; }
    return { x: s.mode === "Bounce" ? bounceX(s.to - 1e-3) : 0, y: 0 };
  }

  /** The ship at t: its place under the walker (Unity axes), its lean (pitch, yaw, roll) and barrel roll (degrees). */
  function state(t) {
    const si = run.sectionIndex(t), sec = sections[si], mode = sec.mode;
    let x = 0, y = 0, rot = [0, 0, 0], barrel = 0;
    if (mode === "Freefly") {
      const f = flyFrame(si, t);
      x = f.x; y = f.y; rot = [f.pitch, f.yaw, f.roll]; barrel = barrelAt(si, t);
    } else {
      if (mode === "Bounce") x = bounceX(t);
      // a new (non-Fly) mode eases the ship from where the last one left it (Steering.RepositionTargetPosition)
      if (si > 0 && STEER[sections[si - 1].mode] !== STEER[mode]) {
        const u = (t - sec.from) / (sections[si - 1].mode === "Freefly" ? rig.steer.transitionFly : rig.steer.transition);
        if (u >= 0 && u < 1) { const e = shipEnd(si - 1), k = transition(u); x = e.x + (x - e.x) * k; y = e.y * (1 - k); }
      }
    }
    return { si, mode, sl: [x, rig.shipLocal[1] + y, rig.shipLocal[2]], rot, barrel };
  }

  /** The ship model's place and turn in three's axes, carried along the path by the walker. */
  function world(t, path) {
    const st = state(t), d = walkerDist(t);
    const pos = path.place([st.sl[0], rig.walkerY + st.sl[1] + SHIP_DY, -st.sl[2]], d);
    const f = path.frameAt(d);
    const local = qmul(qfromEulerYXZ(-rad(st.rot[0]), -rad(st.rot[1]), rad(st.rot[2])), qfromAxisAngle([0, 0, 1], rad(st.barrel)));
    return { pos, q: qmul(qfromBasis(f.right, f.up, f.back), local), st };
  }

  // The List tunnel's turn (Walker.rotation, here radians about the track's axis, as notes.js rolls the List notes).
  // Walker eases it once a frame towards -lane x 120 degrees, by rotationLerp x 0.75 / (seconds per beat) of the way
  // (Mathf.LerpAngle; the game ran at 60 frames a second when recorded). The tunnel, its hold walls and the List notes
  // turn with it; the camera does not (Walker.rotateSelf is off). The autopilot turns in a turn's perfect window, 1/30 s
  // before it; a song that starts in List turns from lane 0 to its first note's lane when that note spawns, 3 s before
  // it; a List after Bounce or Freefly is set to its first note's lane at once when its notes spawn (ModeSelector).
  const THIRD = (2 * Math.PI) / 3, SPAWN_AHEAD = 3, PERFECT = 1 / 30, FPS = 60;
  const spinStep = Math.min(1, ((rig.rotationLerp ?? 12) * 0.75 * (run.doc.bpm / 60)) / FPS);
  const spinRate = spinStep >= 1 ? Infinity : -Math.log(1 - spinStep) * FPS;
  const wrapPi = (a) => { a %= 2 * Math.PI; if (a > Math.PI) a -= 2 * Math.PI; if (a <= -Math.PI) a += 2 * Math.PI; return a; };
  const spinEvents = [];
  for (let i = 0; i < sections.length; i++) {
    const sec = sections[i];
    if (sec.mode !== "List") continue;
    const inSec = plain.filter((n) => n.mode === "List" && n.t >= sec.from && n.t < sec.to);
    if (!inSec.length) continue;
    const firstLane = inSec[0].lane ?? 1;
    if (i === 0) spinEvents.push({ t: inSec[0].t - SPAWN_AHEAD, lane: firstLane, instant: false });
    else if (sections[i - 1].mode !== "List") spinEvents.push({ t: Math.max(sections[i - 1].from, sec.from - SPAWN_AHEAD), lane: firstLane, instant: true });
    for (const n of inSec) if (isTurn(n.color)) spinEvents.push({ t: n.t - PERFECT, lane: laneAfter(n, "List"), instant: false });
  }
  spinEvents.sort((a, b) => a.t - b.t);
  const spinAt = (e, t) => (t <= e.t ? e.from : spinRate === Infinity ? e.target : e.target + (e.from - e.target) * Math.exp(-spinRate * (t - e.t)));
  let reached = 0; // Steering.Reset: lane 0, angle 0
  for (let i = 0; i < spinEvents.length; i++) {
    const e = spinEvents[i];
    if (i > 0) reached = spinAt(spinEvents[i - 1], e.t);
    e.target = reached + wrapPi(-e.lane * THIRD - reached); // the shorter way round, as LerpAngle
    e.from = e.instant ? e.target : reached;
  }

  /** The List tunnel's turn at t (radians): the List notes on lane L roll by spin(t) + L x 120 degrees. */
  function spin(t) {
    if (!spinEvents.length || t < spinEvents[0].t) return 0;
    let lo = 0, hi = spinEvents.length - 1;
    while (lo < hi) { const mid = (lo + hi + 1) >> 1; if (spinEvents[mid].t <= t) lo = mid; else hi = mid - 1; }
    return spinAt(spinEvents[lo], t);
  }

  return { lane, laneAfter, bounceX, flyArea, ringAt, flight, barrelAt, flyStretch, flyOffsetAt, state, world, spin };
}

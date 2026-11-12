// The camera, as the game's rig places it (CameraController + Walker.UpdateCamera): per steer mode an offset from the
// camera mount that follows the ship in some axes, an aim ahead along the path (pulled half way to the ship in
// Freefly), a 1 s eased move between modes, then carried along the path by the walker, sliding sideways toward the
// bend, banking with the path's up.
import { hermite } from "./curves.js";
import { clamp01, dot, normalize, sub, lerp } from "./math.js";

const STEER = { Freefly: "Fly", Bounce: "Coast", List: "Roll" };

/** The rig's place for a steer mode under the walker (Unity axes), given the ship's local place (CameraController.GetPositionInMode). */
export function rigPos(rig, run, ship, steer, sl, t) {
  const r = steer === "Fly" ? rig.fly : steer === "Roll" ? rig.roll : rig.coast;
  const off = steer === "Fly" ? ship.flyOffsetAt(t) : r.offset;
  const zc = ((run.fovAt(t) - 60) / 10) * rig.zComp;
  // the code follows ShipMount's local place (x, y, 0); following MountParent's z as well is what matched the recorded frames
  return [0, 1, 2].map((i) => rig.camOrigin[i] + sl[i] * r.follow[i] + off[i] + (i === 2 ? zc : 0));
}

/** The camera under the walker at t (Unity axes), its look-ahead distance and how far its aim is pulled to the ship. */
export function cameraLocal(run, ship, rig, t) {
  const st = ship.state(t), sec = run.sections[st.si], steer = STEER[sec.mode];
  const prevSteer = st.si > 0 ? STEER[run.sections[st.si - 1].mode] : steer;
  const u = sec.from > 1 && prevSteer !== steer ? clamp01((t - sec.from) / rig.camTransition) : 1;
  const k = hermite(rig.transition)(u);
  const a = rigPos(rig, run, ship, prevSteer, st.sl, t), b = rigPos(rig, run, ship, steer, st.sl, t);
  const lookOf = (s) => (s === "Fly" ? rig.fly.look : s === "Roll" ? rig.roll.look : rig.coast.look);
  const look = lookOf(prevSteer) + (lookOf(steer) - lookOf(prevSteer)) * k;
  const lookLerp = steer === "Fly" ? u * rig.lookLerp : prevSteer === "Fly" && u < 1 ? (1 - u) * rig.lookLerp : 0;
  return { cam: a.map((v, i) => v + (b[i] - v) * k), look, lookLerp, st };
}

/** The camera at t in three's axes: place, aim, up and field of view. */
export function cameraPose(run, ship, rig, path, t) {
  const { cam, look, lookLerp, st } = cameraLocal(run, ship, rig, t);
  const d = path.start + run.distAt(t), f = path.frameAt(d);
  const walker = path.place([0, rig.walkerY, 0], d);
  const ahead = path.place([0, rig.walkerY, 0], d + look);
  const shipRig = path.place([st.sl[0], rig.walkerY + st.sl[1], -st.sl[2]], d);
  const target = lerp(ahead, shipRig, lookLerp);
  // Walker.UpdateCamera: the camera slides sideways toward the bend
  const side = dot(f.right, normalize(sub(target, walker)));
  const x = cam[0] + 0.06 * (rig.sideways * side - cam[0]);
  return { position: path.place([x, rig.walkerY + cam[1], -cam[2]], d), target, up: f.up, fov: run.fovAt(t) };
}

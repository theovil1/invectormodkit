// The walker's numbers: camera rig per steer mode, Freefly steering, ship mount, pickup heights and lanes. Read from
// the game data cache (settings.json's walker components, the PlayerWalkerGlobal tree) where it has them; the rest
// are the game's values written here, with where they come from. All in Unity's axes (x right, y up, z forward).

const key = (time, value, inSlope = 0, outSlope = inSlope) => ({ time, value, inSlope, outSlope });
const TRANSITION = [key(0, 0), key(0.2, 0.19843943, 2.3194094), key(1, 1)];

/** The score levels (PlayerLevelPump's PlayerLevelDefinition): walker speed, field of view (60 + ground FOV), pump limit. */
export const TIERS = [
  { speed: 30, fov: 60, limit: 5 },
  { speed: 35, fov: 70, limit: 5 },
  { speed: 43, fov: 75, limit: 5 },
  { speed: 49, fov: 90, limit: 30 },
];

export const RIG_CONSTANTS = {
  walkerY: -2.5, // the walker's height over the path (the level's spline)
  pickupY: -3, // PlacePickupsOnPath.VerticalOffset
  roadY: -3.3333, // SplineMesh.splineOffsetY of the void road
  hitAhead: 1, // Walker.pickupDistanceOffset: pickups are taken 1 unit ahead of the walker
  camOrigin: [0, 2.67, -5.39], // Walker.cameraOriginPos (the CameraMount node)
  shipLocal: [0, 0.12, -2.723], // the MountParent node
  coast: { follow: [1, 0.5, 1], offset: [0, 1.5, -3.5], look: 10 }, // CameraController, Bounce
  roll: { follow: [0, 0, 1], offset: [0, 1.5, -2], look: 6 }, // CameraController, List
  fly: { follow: [0.9, 0.98, 0], offset: [0, -2.5, -5], offsetAtPickup: [0, -1, -15], pickupDuration: 4.5, look: 50 }, // CameraController, Freefly
  camTransition: 1, // CameraController.cameraTransitiontime
  zComp: 0.05, // CameraController.zCompensationMultiplier (single player)
  laneX: 4.4933, // the ship's Bounce lane spacing (Steering)
  pickupLaneX: 4.5, // the pickups' Bounce lane spacing (GeneratorBounce)
  tri: [[0, 7.62], [-6.57, -3.81], [6.57, -3.81]], // the List tunnel's corner lines (Toblerone)
  sideways: 3, // Walker.SidewaysMovementAmount
  lookLerp: 0.5, // Walker.lookAtTransformLerp (Freefly aim pulled half way to the ship)
  steer: { speed: 100, damp: 0.1, spring: 1, pitchSpeed: 7, yawSpeed: 10, rollSpeed: 15, limitPitch: 20, limitYaw: 30, limitRoll: 60, transition: 1 / 3, transitionFly: 0.5 }, // Steering
  transition: TRANSITION, // CameraController.cameraTransitionCurve
  shipTransition: TRANSITION, // Steering.horizontalTransitionCurve
  flyOffset: [key(0, 0, 0.9196367), key(0.19845526, 0.99847794, 0.2943929), key(0.62792987, 0.82336414, -1.2110564), key(1, 0, 0.16261473)], // CameraController.flyOffsetCurve
  flyControl: [key(0, 0), key(1, 1)], // Steering.FlyControl
};

const vec = (o) => (o && typeof o.x === "number" ? [o.x, o.y, o.z] : undefined);
const first = (o) => (o && Array.isArray(o.values) ? o.values[0] : o); // per player values: the first is single player
const keys = (c) => (c && Array.isArray(c.m_Curve) && c.m_Curve.length ? c.m_Curve.map((k) => key(k.time, k.value, k.inSlope, k.outSlope)) : undefined);
const num = (v) => (typeof v === "number" ? v : undefined);
const or = (v, fallback) => (v === undefined ? fallback : v);

function findNode(node, name) {
  if (!node) return null;
  if (node.name === name) return node;
  for (const c of node.children || []) { const f = findNode(c, name); if (f) return f; }
  return null;
}

/** The walker's numbers from settings.json and the PlayerWalkerGlobal tree; RIG_CONSTANTS for what they lack. */
export function readRig(settings, walkerRoot) {
  const C = RIG_CONSTANTS, w = (settings && settings.walker) || {};
  const cam = (w.CameraController && w.CameraController[0] && w.CameraController[0].fields) || {};
  const steer = (w.Steering && w.Steering[0] && w.Steering[0].fields) || {};
  const walker = (w.Walker && w.Walker[0] && w.Walker[0].fields) || {};
  const place = (w.PlacePickupsOnPath && w.PlacePickupsOnPath[0] && w.PlacePickupsOnPath[0].fields) || {};
  const mount = findNode(walkerRoot, "MountParent");
  return {
    ...C,
    pickupY: or(num(place.VerticalOffset), C.pickupY),
    hitAhead: or(num(walker.pickupDistanceOffset), C.hitAhead),
    camOrigin: or(vec(walker.cameraOriginPos), C.camOrigin),
    shipLocal: or(mount ? mount.position : undefined, C.shipLocal),
    coast: {
      follow: or(vec(first(cam.coastCameraFollow)), C.coast.follow),
      offset: or(vec(first(cam.coastOffset)), C.coast.offset),
      look: or(num((first(cam.coastLookAhead) || {}).z), C.coast.look),
    },
    roll: {
      follow: or(vec(first(cam.rollCameraFollow)), C.roll.follow),
      offset: or(vec(first(cam.rollOffset)), C.roll.offset),
      look: or(num((first(cam.rollLookAhead) || {}).z), C.roll.look),
    },
    fly: {
      follow: or(vec(first(cam.flyCameraFollow)), C.fly.follow),
      offset: or(vec(cam.flyOffset), C.fly.offset),
      offsetAtPickup: or(vec(cam.flyOffsetAtPickup), C.fly.offsetAtPickup),
      pickupDuration: or(num(cam.flyOffsetAtPickupDuration), C.fly.pickupDuration),
      look: or(num((cam.flyLookAhead || {}).z), C.fly.look),
    },
    camTransition: or(num(cam.cameraTransitiontime), C.camTransition),
    zComp: or(num(first(cam.zCompensationMultiplier)), C.zComp),
    sideways: or(num(walker.SidewaysMovementAmount), C.sideways),
    lookLerp: or(num(walker.lookAtTransformLerp), C.lookLerp),
    steer: {
      speed: or(num(steer.FlySteerSpeed), C.steer.speed),
      damp: or(num(steer.FlyDamp), C.steer.damp),
      spring: or(num(steer.FlySpring), C.steer.spring),
      pitchSpeed: or(num(steer.pitchSpeed), C.steer.pitchSpeed),
      yawSpeed: or(num(steer.yawSpeed), C.steer.yawSpeed),
      rollSpeed: or(num(steer.rollSpeed), C.steer.rollSpeed),
      limitPitch: or(num(steer.limitPitch), C.steer.limitPitch),
      limitYaw: or(num(steer.limitYaw), C.steer.limitYaw),
      limitRoll: or(num(steer.limitRoll), C.steer.limitRoll),
      transition: or(num(steer.transitionDuration), C.steer.transition),
      transitionFly: or(num(steer.transitionDurationFreefly), C.steer.transitionFly),
    },
    transition: or(keys(cam.cameraTransitionCurve), C.transition),
    shipTransition: or(keys(steer.horizontalTransitionCurve), C.shipTransition),
    flyOffset: or(keys(cam.flyOffsetCurve), C.flyOffset),
    flyControl: or(keys(steer.FlyControl), C.flyControl),
    tiers: TIERS,
  };
}

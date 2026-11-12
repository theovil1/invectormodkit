// Where a strum's hold is pulled from on the timeline: a hold from its end, a plain strum from a handle just right of
// its ring (drawn while the strum is selected or under the pointer), so turning a strum into a hold is a drag.

/** Pixels from a plain strum's centre to its handle: clear of its 48 px ring. */
export const GRIP_GAP = 30;
/** How close the pointer must be to a grip to take it, in pixels. */
const REACH = 7;

/** The grip's x: the hold's end, or the handle right of a plain strum. */
export function gripX(n, xOf) {
  return n.len > 0 ? xOf(n.t + n.len) : xOf(n.t) + GRIP_GAP;
}

/** The hold whose end is under the pointer, if any. */
export function holdEndAt(strums, xOf, x) {
  return closest(strums.filter((n) => n.len > 0), xOf, x);
}

/** The plain strum whose handle is under the pointer, if any. */
export function handleAt(strums, xOf, x) {
  return closest(strums.filter((n) => !(n.len > 0)), xOf, x);
}

function closest(strums, xOf, x) {
  let best = null, bestD = REACH;
  for (const n of strums) {
    const d = Math.abs(gripX(n, xOf) - x);
    if (d < bestD) { best = n; bestD = d; }
  }
  return best;
}

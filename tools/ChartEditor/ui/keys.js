// What the arrow keys do: ← / → move in the song, Ctrl + ← / → move the selected notes, Alt for 1 ms steps.

/** `{ seek: seconds }` or `{ move: seconds }` for an ArrowLeft / ArrowRight key event. */
export function arrowAction(e, step, hasSelection) {
  const dt = (e.key === "ArrowLeft" ? -1 : 1) * (e.altKey ? 0.001 : step);
  return (e.ctrlKey || e.metaKey) && hasSelection ? { move: dt } : { seek: dt };
}

// The side panel: its tabs (Song, Notes, Tools, Playability), the panels that fold in a tab they share, and the band
// on its bottom edge that names what is hidden below.

/**
 * What is below the visible part of the panel: `more` while it is not scrolled to the end, and the panels whose
 * title is hidden under the band or further down. Panel tops are offsets inside the scrolled content.
 */
export function hiddenBelow(view, panels, { band = 44, title = 32 } = {}) {
  const more = view.top + view.height < view.total - 2;
  const edge = view.top + view.height - band;
  return { more, ids: more ? panels.filter((p) => p.top + title > edge).map((p) => p.id) : [] };
}

/** The folded panels' ids, as remembered (an empty set for anything unreadable). */
export function readFolded(text) {
  try {
    const ids = JSON.parse(text);
    return new Set(Array.isArray(ids) ? ids.filter((id) => typeof id === "string") : []);
  } catch {
    return new Set();
  }
}

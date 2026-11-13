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

const FOLDED = "imk.side.folded";

function localStore() {
  try { return window.localStorage; } catch { return null; }
}

/** The side panel in the page: `#side-tabs`, the panels in `#side-body` (each with its `data-tab`), the band `#side-more`. */
export class SidePanel {
  constructor(root, storage = localStore()) {
    this.root = root;
    this.body = root.querySelector("#side-body");
    this.band = root.querySelector("#side-more");
    this.storage = storage;
    this.shown = null;
    let remembered = null;
    try { remembered = this.storage.getItem(FOLDED); } catch { /* nothing remembered */ }
    const folded = readFolded(remembered);
    for (const card of this.cards()) card.classList.toggle("folded", card.classList.contains("foldable") && folded.has(card.id));
    root.querySelector("#side-tabs").addEventListener("click", (e) => {
      const tab = e.target.closest("button[data-tab]");
      if (tab) this.show(tab.dataset.tab);
    });
    this.body.addEventListener("click", (e) => {
      const title = e.target.closest(".foldable > h2");
      if (title) this.fold(title.parentElement);
    });
    // a panel's name brings it to the top; the rest of the band scrolls down by most of a screen
    this.band.addEventListener("click", (e) => {
      const name = e.target.closest("button[data-card]");
      if (name) this.body.scrollTo({ top: document.getElementById(name.dataset.card).offsetTop, behavior: "smooth" });
      else this.body.scrollBy({ top: this.body.clientHeight * 0.8, behavior: "smooth" });
    });
    this.body.addEventListener("scroll", () => this.refresh());
    const sizes = new ResizeObserver(() => this.refresh());
    sizes.observe(this.body);
    for (const card of this.cards()) sizes.observe(card);
    this.show("notes");
  }

  cards() { return [...this.body.querySelectorAll(":scope > .frame")]; }

  /** Shows one tab's panels, from their top. */
  show(tab) {
    for (const button of this.root.querySelectorAll("#side-tabs button")) {
      button.classList.toggle("on", button.dataset.tab === tab);
      button.setAttribute("aria-selected", button.dataset.tab === tab);
    }
    for (const card of this.cards()) card.classList.toggle("hidden", card.dataset.tab !== tab);
    this.body.scrollTop = 0;
    this.refresh();
  }

  fold(card) {
    card.classList.toggle("folded");
    const ids = this.cards().filter((c) => c.classList.contains("folded")).map((c) => c.id);
    try { this.storage.setItem(FOLDED, JSON.stringify(ids)); } catch { /* not remembered, still folded */ }
    this.refresh();
  }

  /** The Playability tab's badge: the number of problems (none while no song is open). */
  count(n) {
    const badge = this.root.querySelector("#issues-count");
    badge.textContent = n === null ? "" : String(n);
    badge.classList.toggle("bad", n > 0);
    this.refresh();
  }

  /** The band: shown while there is more below, with a button per panel whose title is hidden. */
  refresh() {
    const panels = this.cards().filter((c) => !c.classList.contains("hidden")).map((c) => ({ id: c.id, top: c.offsetTop }));
    const { more, ids } = hiddenBelow({ top: this.body.scrollTop, height: this.body.clientHeight, total: this.body.scrollHeight }, panels);
    this.band.classList.toggle("hidden", !more);
    const names = ids.map((id) => [id, document.getElementById(id).querySelector("h2")?.textContent || ""]);
    const key = JSON.stringify(names);
    if (key === this.shown) return;
    this.shown = key;
    this.band.querySelector("#side-more-list").replaceChildren(...names.map(([id, name]) => {
      const button = document.createElement("button");
      button.dataset.card = id;
      button.textContent = name;
      return button;
    }));
  }
}

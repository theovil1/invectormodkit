// The game's look, when it is on this computer: the pictures the mod exports in game (skin/manifest.json, sprites/…,
// models/…, ships/…) and the game's fonts (skin/fonts/…, cut out of its bundles by the editor). Everything has a
// drawn or CSS fallback, so the editor looks right without them too.

const BASE = "skin/";
const FONTS = [["Exo2BC", "Exo2-BoldCondensed.otf"], ["Exo2SC", "Exo2-SemiBoldCondensed.otf"], ["Exo2MC", "Exo2-MediumCondensed.otf"], ["Exo2RC", "Exo2-RegularCondensed.otf"]];

export const skin = {
  base: BASE,
  manifest: { sprites: {}, backgrounds: {}, models: {}, ships: {} },
  images: new Map(),
  onChange: () => {},

  async load() {
    try {
      const r = await fetch(BASE + "manifest.json", { cache: "no-store" });
      if (r.ok) this.manifest = { ...this.manifest, ...(await r.json()) };
    } catch { /* no skin yet: fallbacks */ }
    await this.loadFonts();
    const root = document.documentElement;
    root.classList.toggle("skinned", this.has("InfoFrameSimple"));
    for (const [variable, name] of [["--frame", "InfoFrameSimple"], ["--button", "MenuButtonGradient New"], ["--headline", "NewsHeaderLine"],
      ["--hud-tl", "HUD_TopLeft"], ["--hud-tr", "HUD_TopRight"], ["--hud-bl", "HUD_BottomLeft"], ["--hud-br", "HUD_BottomRight"]])
      if (this.has(name)) root.style.setProperty(variable, `url("${this.url(name)}")`);
    return this;
  },

  /** The game's fonts as soon as they are there (the editor cuts them out at first launch). */
  async loadFonts() {
    await Promise.all(FONTS.map(async ([family, file]) => {
      if ([...document.fonts].some((f) => f.family === family && f.status === "loaded")) return;
      try {
        const face = new FontFace(family, `url("${BASE}fonts/${file}")`);
        await face.load();
        document.fonts.add(face);
      } catch { /* not extracted (yet): the look-alike fonts */ }
    }));
    document.documentElement.classList.toggle("game-fonts", document.fonts.check("12px Exo2BC"));
  },

  has(name) { return !!this.manifest.sprites[name]; },
  url(name) { const s = this.manifest.sprites[name]; return s ? BASE + s.file : null; },
  hasModels() { return Object.keys(this.manifest.models || {}).length > 0; },

  /** A sprite as an <img> for canvas drawing (null when the game's picture is not there); redraws when loaded. */
  img(name) {
    if (this.images.has(name)) return this.images.get(name);
    const url = this.url(name);
    let image = null;
    if (url) {
      image = new Image();
      image.onload = () => this.onChange();
      image.src = url;
    }
    this.images.set(name, image);
    return image;
  },

  /** Is this picture ready to draw? */
  ready(image) { return !!(image && image.complete && image.naturalWidth); },
};

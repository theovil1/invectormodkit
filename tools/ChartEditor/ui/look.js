// What the timeline and the 3D preview share: the zones' and buttons' colours, the game's key pictures.

/** Zone colours: tunnel (List), road (Bounce), free flight (Freefly). */
export const MODE_COLOR = { List: "#A040FF", Bounce: "#19C8E6", Freefly: "#FF8A2C" };
/** Button colours: left, right, down, up, turn left, turn right. */
export const KEY_COLOR = ["#3FB7FF", "#FF4F6D", "#5CFF8A", "#FFD23F", "#CB33FF", "#CB33FF"];
export const KEY_GLYPH = ["←", "→", "↓", "↑", "«", "»"];
/** What each note asks the player for (the mod exports the player's own keys under these names). */
export const ACTION = ["left", "right", "down", "up", "turnLeft", "turnRight"];
/** The game's default PC keys (pictures exported by the mod), until the mod says what this player uses. */
export const DEFAULT_KEY_SPRITE = { left: "Keyboard_LeftArrow", right: "Keyboard_RightArrow", down: "Keyboard_DownArrow", up: "Keyboard_UpArrow", strum: "Keyboard_Space" };
export const KEY_SPRITE = ACTION.slice(0, 4).map((a) => DEFAULT_KEY_SPRITE[a]);

/** The picture of the key for an action: the player's own (from the mod's manifest), else the default; null if unknown. */
export function keySprite(manifest, action) {
  const own = manifest && manifest.keys && manifest.keys[action], sprites = (manifest && manifest.sprites) || {};
  if (own) {
    // the game's own picture for it, else the keyboard key it shows (Q on AZERTY), else the key's name (Alpha1…)
    const names = [own.sprite, own.label && "Keyboard_" + own.label.toUpperCase(), own.key && "Keyboard_" + own.key];
    for (const name of names) if (name && sprites[name]) return name;
  }
  return DEFAULT_KEY_SPRITE[action] || null;
}

/** The game's fonts (cut out of its bundles by the editor), then look-alikes Windows has. */
export const FONT = {
  bold: "Exo2BC, 'Bahnschrift SemiBold Condensed', Bahnschrift, 'Segoe UI', sans-serif",
  semi: "Exo2SC, 'Bahnschrift SemiBold SemiCondensed', Bahnschrift, 'Segoe UI', sans-serif",
  regular: "Exo2RC, 'Bahnschrift SemiCondensed', Bahnschrift, 'Segoe UI', sans-serif",
};

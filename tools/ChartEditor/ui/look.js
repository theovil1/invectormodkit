// What the timeline and the 3D preview share: the zones' and buttons' colours, the game's key pictures.

/** Zone colours: tunnel (List), road (Bounce), free flight (Freefly). */
export const MODE_COLOR = { List: "#A040FF", Bounce: "#19C8E6", Freefly: "#FF8A2C" };
/** Button colours: left, right, down, up, turn left, turn right. */
export const KEY_COLOR = ["#3FB7FF", "#FF4F6D", "#5CFF8A", "#FFD23F", "#CB33FF", "#CB33FF"];
export const KEY_GLYPH = ["←", "→", "↓", "↑", "↺", "↻"];
/** The game's keyboard key pictures (exported by the mod) for the four arrows. */
export const KEY_SPRITE = ["Keyboard_LeftArrow", "Keyboard_RightArrow", "Keyboard_DownArrow", "Keyboard_UpArrow"];
export const STRUM_SPRITE = "Strum R Generic";

/** The game's fonts (cut out of its bundles by the editor), then look-alikes Windows has. */
export const FONT = {
  bold: "Exo2BC, 'Bahnschrift SemiBold Condensed', Bahnschrift, 'Segoe UI', sans-serif",
  semi: "Exo2SC, 'Bahnschrift SemiBold SemiCondensed', Bahnschrift, 'Segoe UI', sans-serif",
  regular: "Exo2RC, 'Bahnschrift SemiCondensed', Bahnschrift, 'Segoe UI', sans-serif",
};

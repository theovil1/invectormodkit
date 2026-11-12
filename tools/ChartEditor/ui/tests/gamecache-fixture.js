// The owner's game data cache (part 1), for the tests that check the preview against the real game. Those tests are
// skipped where the cache is absent (CI): nothing from the game is in the repository.
import { existsSync, readFileSync } from "node:fs";
import { join } from "node:path";

const dir = process.env.LOCALAPPDATA ? join(process.env.LOCALAPPDATA, "InvectorModKit", "LevelEditor", "game") : "";

export const HAS_CACHE = !!dir && existsSync(join(dir, "index.json"));

export const cache = {
  dir,
  json: (relative) => JSON.parse(readFileSync(join(dir, relative), "utf8")),
  bytes: (relative) => { const b = readFileSync(join(dir, relative)); return b.buffer.slice(b.byteOffset, b.byteOffset + b.byteLength); },
};

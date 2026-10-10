import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { _rows, LANGUAGES, setLang, t, issueText, langCode } from "../i18n.js";

const dir = fileURLToPath(new URL("..", import.meta.url));
const read = (name) => readFileSync(dir + name, "utf8");

test("every text exists in the 11 languages", () => {
  for (const [key, row] of Object.entries(_rows)) {
    assert.equal(row.length, LANGUAGES.length, key);
    assert.ok(row.every((s) => typeof s === "string" && s.length > 0), key);
    const holes = (s) => (s.match(/\{\d\}/g) || []).sort().join();
    assert.ok(row.every((s) => holes(s) === holes(row[0]) || key.startsWith("issue_")), `${key}: placeholders differ`);
  }
});

test("every key the interface uses is defined", () => {
  const used = new Set();
  for (const file of ["app.js", "timeline.js"]) for (const m of read(file).matchAll(/\bt\("([\w-]+)"\s*[,)]/g)) used.add(m[1]);
  for (const m of read("index.html").matchAll(/data-t="([\w-]+)"/g)) used.add(m[1]);
  for (const mode of ["List", "Bounce", "Freefly"]) used.add("mode" + mode);
  for (let c = 0; c < 6; c++) used.add("button" + c);
  for (const k of ["start", "end", "preview"]) used.add("marker_" + k);
  const missing = [...used].filter((k) => !_rows[k]);
  assert.deepEqual(missing, []);
});

test("language codes from Windows or the game", () => {
  for (const [code, expected] of [["fr-FR", "fr"], ["pt-PT", "pt-BR"], ["zh-CN", "zh"], ["ja", "ja"], ["xx", "en"], [undefined, "en"]]) {
    setLang(code);
    assert.equal(langCode(), expected, String(code));
  }
});

test("playability issues are translated, with their numbers", () => {
  setLang("fr");
  assert.equal(issueText({ code: "pair", gap: 0.12, detail: "ArrowTurn" }), "Trop rapide : flèche → virage (0.12 s)");
  assert.equal(issueText({ code: "lane-change", mode: "Bounce" }), "Route : changement de voie sans virage");
  assert.equal(issueText({ code: "something-new", text: "12.00s: raw" }), "12.00s: raw");
  assert.equal(t("rowLane", 2), "Voie 2");
});

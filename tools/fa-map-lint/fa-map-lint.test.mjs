// tools/fa-map-lint/fa-map-lint.test.mjs — red-fixture both directions (§12).
// Run: node --test tools/fa-map-lint/fa-map-lint.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import {
  extractGlyphEnum,
  pascalToCamel,
  setDiff,
  checkMap,
  checkParity,
} from "./fa-map-lint.mjs";

const SWIFT = `
public enum Glyph: Sendable, CaseIterable {
    case tabConnect
    case tabExplore
    case checkmark
}
public enum IconStyle { case playful }
`;
const KOTLIN = `
enum class Glyph {
    TabConnect,
    TabExplore,
    Checkmark,
}
enum class IconRegister { Playful, Neutral }
`;

test("extractGlyphEnum: pulls only the Glyph enum's cases (not neighbouring enums)", () => {
  assert.deepEqual(extractGlyphEnum(SWIFT, "swift"), ["tabConnect", "tabExplore", "checkmark"]);
  assert.deepEqual(extractGlyphEnum(KOTLIN, "kotlin"), ["TabConnect", "TabExplore", "Checkmark"]);
});

test("pascalToCamel: Android case -> map key", () => {
  assert.equal(pascalToCamel("TabConnect"), "tabConnect");
  assert.equal(pascalToCamel("Checkmark"), "checkmark");
});

test("setDiff: reports missing + extra", () => {
  const d = setDiff(["a", "b", "c"], ["a", "c", "d"]);
  assert.deepEqual(d.missing, ["b"]);
  assert.deepEqual(d.extra, ["d"]);
});

test("checkMap GREEN: well-formed map passes", () => {
  const map = { faces: { playful: {}, neutral: {} }, glyphs: { tabConnect: { fa: "users", unicode: "f0c0" } } };
  assert.deepEqual(checkMap(map), []);
});

test("checkMap RED: non-hex unicode + missing fa name are caught", () => {
  const map = { faces: { playful: {}, neutral: {} }, glyphs: { x: { fa: "", unicode: "U+F0C0" } } };
  const e = checkMap(map);
  assert.ok(e.some((s) => s.includes("missing FA name")));
  assert.ok(e.some((s) => s.includes("unicode not lowercase hex")));
});

test("checkMap RED: missing faces block is caught", () => {
  assert.ok(checkMap({ glyphs: {} }).some((s) => s.includes("faces")));
});

test("checkParity GREEN: map keys == iOS cases == Android(normalized)", () => {
  const keys = ["tabConnect", "tabExplore", "checkmark"];
  assert.deepEqual(checkParity(keys, extractGlyphEnum(SWIFT, "swift"), extractGlyphEnum(KOTLIN, "kotlin")), []);
});

test("checkParity RED: a Glyph added to iOS but not the map (or Android) is caught", () => {
  const keys = ["tabConnect", "tabExplore", "checkmark"];
  const iosPlusOne = [...extractGlyphEnum(SWIFT, "swift"), "newBadge"];
  const e = checkParity(keys, iosPlusOne, extractGlyphEnum(KOTLIN, "kotlin"));
  assert.ok(e.some((s) => s.includes("iOS Glyph enum has cases absent from the map")));
  assert.ok(e.some((s) => s.includes("newBadge")));
});

test("checkParity RED: a Glyph in the map but missing from Android is caught", () => {
  const keys = ["tabConnect", "tabExplore", "checkmark", "extraOnlyInMap"];
  const e = checkParity(keys, extractGlyphEnum(SWIFT, "swift"), extractGlyphEnum(KOTLIN, "kotlin"));
  assert.ok(e.some((s) => s.includes("iOS Glyph enum missing map keys")));
  assert.ok(e.some((s) => s.includes("Android Glyph enum missing map keys")));
});

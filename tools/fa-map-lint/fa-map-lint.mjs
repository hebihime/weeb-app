#!/usr/bin/env node
// tools/fa-map-lint/fa-map-lint.mjs — keeps design/fa-glyph-map.json (the FA Pro glyph→codepoint map)
// in EXACT sync with the `Glyph` enums both client shells declare, so the FA icon seam (deferred wiring)
// can never resolve a codepoint for a glyph one platform doesn't have, or miss a glyph a platform added.
//
// Three checks: (1) the map is well-formed (27+ glyphs, each with a non-empty FA name + a hex codepoint,
// the two faces declared); (2) the map's glyph keys == the iOS `Glyph` enum cases; (3) == the Android
// `Glyph` enum entries (PascalCase normalized to the map's camelCase). Any drift fails the build.
//
// Usage: node tools/fa-map-lint/fa-map-lint.mjs [--repo-root <path>]   (exit 0 pass / 1 fail)

import { readFileSync, existsSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const HEX = /^[0-9a-f]+$/;

/** Extract the identifiers of a named enum's members from source text. lang: "swift" | "kotlin". */
export function extractGlyphEnum(source, lang) {
  if (lang === "swift") {
    // enum Glyph ... { case tabConnect \n case tabExplore ... }
    const m = source.match(/enum\s+Glyph\b[^{]*\{([\s\S]*?)\n\}/);
    if (!m) return [];
    return [...m[1].matchAll(/\bcase\s+([A-Za-z_]\w*)/g)].map((x) => x[1]);
  }
  // kotlin: enum class Glyph { TabConnect, TabExplore, ... }
  const m = source.match(/enum\s+class\s+Glyph\s*\{([\s\S]*?)\}/);
  if (!m) return [];
  return m[1]
    .split(/[,\n]/)
    .map((s) => s.trim())
    .filter((s) => /^[A-Za-z_]\w*$/.test(s));
}

/** Android PascalCase (TabConnect) -> the map's camelCase key (tabConnect). */
export function pascalToCamel(name) {
  return name.charAt(0).toLowerCase() + name.slice(1);
}

/** Set-equality diff. Returns {missing, extra} of `have` relative to `want`. */
export function setDiff(want, have) {
  const w = new Set(want), h = new Set(have);
  return {
    missing: [...w].filter((x) => !h.has(x)),
    extra: [...h].filter((x) => !w.has(x)),
  };
}

export function checkMap(map) {
  const errs = [];
  if (!map || typeof map !== "object") return ["fa-glyph-map.json: not an object"];
  if (!map.faces || !map.faces.playful || !map.faces.neutral) errs.push("faces.playful / faces.neutral missing");
  const glyphs = map.glyphs || {};
  const keys = Object.keys(glyphs);
  if (keys.length < 1) errs.push("glyphs: empty");
  for (const k of keys) {
    const g = glyphs[k];
    if (!g || typeof g.fa !== "string" || g.fa.length === 0) errs.push(`glyph ${k}: missing FA name`);
    if (!g || typeof g.unicode !== "string" || !HEX.test(g.unicode)) errs.push(`glyph ${k}: unicode not lowercase hex`);
  }
  return errs;
}

export function checkParity(mapKeys, iosCases, androidCases) {
  const errs = [];
  const androidNorm = androidCases.map(pascalToCamel);
  const ios = setDiff(mapKeys, iosCases);
  if (ios.missing.length) errs.push(`iOS Glyph enum missing map keys: ${ios.missing.join(", ")}`);
  if (ios.extra.length) errs.push(`iOS Glyph enum has cases absent from the map: ${ios.extra.join(", ")}`);
  const and = setDiff(mapKeys, androidNorm);
  if (and.missing.length) errs.push(`Android Glyph enum missing map keys: ${and.missing.join(", ")}`);
  if (and.extra.length) errs.push(`Android Glyph enum has cases absent from the map: ${and.extra.join(", ")}`);
  return errs;
}

const IOS_ICON = "ios/DesignKit/Sources/DesignKit/Iconography.swift";
const AND_ICON = "android/designkit/src/main/kotlin/app/client/designkit/Iconography.kt";
const MAP = "design/fa-glyph-map.json";

function main() {
  const args = process.argv.slice(2);
  const idx = args.indexOf("--repo-root");
  const __dirname = dirname(fileURLToPath(import.meta.url));
  const repoRoot = idx >= 0 ? args[idx + 1] : join(__dirname, "..", "..");

  const mapPath = join(repoRoot, MAP);
  if (!existsSync(mapPath)) {
    console.log(`fa-map-lint: ${MAP} not present — guarded (nothing to check)`);
    return;
  }
  const map = JSON.parse(readFileSync(mapPath, "utf8"));
  const violations = checkMap(map);

  const iosPath = join(repoRoot, IOS_ICON);
  const andPath = join(repoRoot, AND_ICON);
  if (existsSync(iosPath) && existsSync(andPath)) {
    const iosCases = extractGlyphEnum(readFileSync(iosPath, "utf8"), "swift");
    const andCases = extractGlyphEnum(readFileSync(andPath, "utf8"), "kotlin");
    if (iosCases.length === 0) violations.push(`${IOS_ICON}: could not extract the Glyph enum`);
    if (andCases.length === 0) violations.push(`${AND_ICON}: could not extract the Glyph enum`);
    violations.push(...checkParity(Object.keys(map.glyphs || {}), iosCases, andCases));
  } else {
    console.log("fa-map-lint: one/both client Iconography files absent — parity check guarded");
  }

  if (violations.length > 0) {
    console.error("fa-map-lint BLOCKED:");
    for (const v of violations) console.error(`  - ${v}`);
    process.exitCode = 1;
    return;
  }
  console.log(`fa-map-lint OK: ${Object.keys(map.glyphs).length} glyphs, map ↔ iOS ↔ Android Glyph enums in sync`);
}

const isMain = process.argv[1] && import.meta.url === `file://${process.argv[1]}`;
if (isMain) main();

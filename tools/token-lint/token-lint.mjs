#!/usr/bin/env node
// tools/token-lint/token-lint.mjs — SLICE_S7_CONTRACT.md §9a.
//
// DESIGN.md is authoritative PROSE; design/tokens.v1.json is its machine-readable mirror; the platform
// token layers (ios DesignKit/Tokens.swift, android designkit/Tokens.kt) are hand-written and hold
// only values from the manifest. This lint proves the whole chain agrees, four ways:
//   (a) manifest  ↔  DESIGN.md's Color / Type Scale / Brand Delta tables (value-for-value);
//   (b) each platform token layer  ↔  manifest palette (every manifest hex appears; no unknown hex);
//   (c) brand-delta keys  ↔  brands/*.json (brand_primary / brand_celebration / string_pack_id);
//   (d) the manifest declares NO forbidden token group (law 3/6: no disabled/locked/decay/expiry token).
//
// Lint-verify, not generate (the S0-ratified brand-gate pattern; §13). Pure functions + golden vectors;
// main() runs against the real repo with guarded activation (platform checks arm when ios/android land).
//
// Usage: node tools/token-lint/token-lint.mjs [--repo-root <path>]   (exit 0 pass / 1 fail; prints all)

import { readFileSync, existsSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const HEX = /#[0-9A-Fa-f]{6}/g;

// ---------- DESIGN.md parsers (the authoritative prose → structured values) ----------

/** Color table rows: `| **Bubblegum** | \`#F7568F\` | ... |` and `| good | \`#3FB950\` | ... |`. */
export function parseDesignColorTable(md) {
  const colors = {};
  const re = /^\|\s*\**([A-Za-z][\w ]*?)\**\s*\|\s*`(#[0-9A-Fa-f]{6})`/gm;
  let m;
  while ((m = re.exec(md)) !== null) {
    colors[m[1].trim()] = m[2].toUpperCase();
  }
  return colors;
}

function grab(line, re) {
  const m = re.exec(line);
  return m ? m[1].toUpperCase() : undefined;
}

/** The single "Light mode (default):" line → {ground,surface,surface_2,hairline,text,dim,outline}. */
export function parseDesignLight(md) {
  const line = md.split("\n").find((l) => l.includes("Light mode (default)")) ?? "";
  return {
    ground: grab(line, /ground `(#[0-9A-Fa-f]{6})`/),
    surface: grab(line, /(?<!-\d )surface `(#[0-9A-Fa-f]{6})`/),
    surface_2: grab(line, /surface-2 `(#[0-9A-Fa-f]{6})`/),
    hairline: grab(line, /`(#[0-9A-Fa-f]{6})` hairlines/),
    text: grab(line, /text `(#[0-9A-Fa-f]{6})`/),
    dim: grab(line, /dim `(#[0-9A-Fa-f]{6})`/),
    outline: grab(line, /outline `(#[0-9A-Fa-f]{6})`/),
  };
}

/** The single 'Dark mode ("Choco"):' line → {ground,surface,surface_2,line,text,dim,outline}. */
export function parseDesignDark(md) {
  const line = md.split("\n").find((l) => l.includes('Dark mode ("Choco")')) ?? "";
  return {
    ground: grab(line, /ground `(#[0-9A-Fa-f]{6})`/),
    surface: grab(line, /(?<!-2 )surface `(#[0-9A-Fa-f]{6})`/),
    surface_2: grab(line, /surface-2 `(#[0-9A-Fa-f]{6})`/),
    line: grab(line, / line `(#[0-9A-Fa-f]{6})`/),
    text: grab(line, /text `(#[0-9A-Fa-f]{6})`/),
    dim: grab(line, /dim `(#[0-9A-Fa-f]{6})`/),
    outline: grab(line, /outline (?:flips to )?`(#[0-9A-Fa-f]{6})`/),
  };
}

/** Mobile type-scale table: `| display-xl | 34/40 | 900 |`. Roles normalised: display-xl → display_xl. */
export function parseDesignTypeScale(md) {
  const scale = {};
  const re = /^\|\s*([a-z][\w-]*)\s*\|\s*(\d+)\/(\d+)\s*\|\s*(\d+)/gm;
  let m;
  while ((m = re.exec(md)) !== null) {
    scale[m[1].replace(/-/g, "_")] = { size: +m[2], line: +m[3], weight: +m[4] };
  }
  return scale;
}

/** Brand Delta table → {primary:{weeb,friki}, celebration:{weeb,friki}} (the two rows that carry hexes). */
export function parseDesignBrandDelta(md) {
  const out = {};
  for (const line of md.split("\n")) {
    let key;
    if (line.includes("`brand.primary`")) key = "primary";
    else if (line.includes("`brand.celebration`")) key = "celebration";
    else continue;
    const hexes = (line.match(HEX) ?? []).map((h) => h.toUpperCase());
    if (hexes.length >= 2) out[key] = { weeb: hexes[0], friki: hexes[1] };
  }
  return out;
}

export function parseDesign(md) {
  return {
    colors: parseDesignColorTable(md),
    light: parseDesignLight(md),
    dark: parseDesignDark(md),
    typeScale: parseDesignTypeScale(md),
    brandDelta: parseDesignBrandDelta(md),
  };
}

// ---------- checks ----------

const COLOR_MAP = {
  "candy.bubblegum": "Bubblegum",
  "candy.sky": "Sky",
  "candy.mikan": "Mikan",
  "candy.foil": "Foil",
  "candy.choco": "Choco",
  "semantic.good": "good",
  "semantic.warn": "warn",
  "semantic.danger": "danger",
};

function get(obj, path) {
  return path.split(".").reduce((o, k) => (o == null ? undefined : o[k]), obj);
}

/** (a) manifest palette + type scale + brand delta ↔ DESIGN.md tables. */
export function checkManifestAgainstDesign(manifest, design) {
  const v = [];
  for (const [mPath, dName] of Object.entries(COLOR_MAP)) {
    const mVal = (get(manifest.palette, mPath) ?? "").toUpperCase();
    const dVal = design.colors[dName];
    if (!dVal) v.push(`DESIGN.md color table has no row for "${dName}"`);
    else if (mVal !== dVal) v.push(`palette.${mPath} is ${mVal || "(missing)"}, DESIGN.md says ${dVal}`);
  }
  for (const mode of ["light", "dark"]) {
    for (const [k, dVal] of Object.entries(design[mode])) {
      const mVal = (get(manifest.palette[mode], k) ?? "").toUpperCase();
      if (dVal && mVal !== dVal) v.push(`palette.${mode}.${k} is ${mVal || "(missing)"}, DESIGN.md says ${dVal}`);
    }
  }
  for (const [role, spec] of Object.entries(design.typeScale)) {
    const m = manifest.type_scale?.[role];
    if (!m) { v.push(`type_scale.${role} missing from manifest (DESIGN.md has it)`); continue; }
    if (m.size !== spec.size || m.line !== spec.line || m.weight !== spec.weight)
      v.push(`type_scale.${role} is ${m.size}/${m.line} w${m.weight}, DESIGN.md says ${spec.size}/${spec.line} w${spec.weight}`);
  }
  for (const key of ["primary", "celebration"]) {
    for (const brand of ["weeb", "friki"]) {
      const mVal = (manifest.brand_delta?.[brand]?.[key] ?? "").toUpperCase();
      const dVal = design.brandDelta[key]?.[brand];
      if (dVal && mVal !== dVal) v.push(`brand_delta.${brand}.${key} is ${mVal || "(missing)"}, DESIGN.md Brand Delta says ${dVal}`);
    }
  }
  return v;
}

/** (c) brand-delta keys ↔ brands/*.json canonical values. */
export function checkBrandDeltaAgainstBrands(manifest, brands) {
  const v = [];
  for (const brand of ["weeb", "friki"]) {
    const md = manifest.brand_delta?.[brand] ?? {};
    const bj = brands[brand] ?? {};
    if ((md.primary ?? "").toUpperCase() !== (bj.brand_primary ?? "").toUpperCase())
      v.push(`brand_delta.${brand}.primary ${md.primary} != brands/${brand}.json brand_primary ${bj.brand_primary}`);
    if ((md.celebration ?? "").toUpperCase() !== (bj.brand_celebration ?? "").toUpperCase())
      v.push(`brand_delta.${brand}.celebration ${md.celebration} != brands/${brand}.json brand_celebration ${bj.brand_celebration}`);
    if (md.string_pack_id !== bj.string_pack_id)
      v.push(`brand_delta.${brand}.string_pack_id ${md.string_pack_id} != brands/${brand}.json string_pack_id ${bj.string_pack_id}`);
  }
  return v;
}

/** All manifest palette hexes (candy + semantic + light + dark), uppercased — the platform-layer allowlist. */
export function manifestPaletteHexes(manifest) {
  const p = manifest.palette ?? {};
  const all = [
    ...Object.values(p.candy ?? {}),
    ...Object.values(p.semantic ?? {}),
    ...Object.values(p.light ?? {}),
    ...Object.values(p.dark ?? {}),
  ];
  return new Set(all.map((h) => String(h).toUpperCase()));
}

export function parseHexLiterals(content) {
  return (content.match(HEX) ?? []).map((h) => h.toUpperCase());
}

/**
 * (b) a platform token layer holds exactly the manifest palette: every hex it contains is a manifest
 * value (no rogue color), and every manifest value appears (nothing silently dropped).
 */
export function checkPlatformLayer(name, content, manifest) {
  const v = [];
  const allowed = manifestPaletteHexes(manifest);
  const present = new Set(parseHexLiterals(content));
  for (const hex of present) {
    if (!allowed.has(hex)) v.push(`${name}: hex ${hex} is not a design/tokens.v1.json palette value (rogue color)`);
  }
  for (const hex of allowed) {
    if (!present.has(hex)) v.push(`${name}: manifest palette value ${hex} never appears (platform layer out of sync)`);
  }
  return v;
}

/** (d) manifest declares no forbidden token group (laws 3 & 6). */
export function checkForbiddenGroups(manifest) {
  const v = [];
  const forbidden = manifest.forbidden_token_groups;
  if (!Array.isArray(forbidden) || forbidden.length === 0)
    v.push(`manifest must list forbidden_token_groups (laws 3 & 6: no disabled/decay token)`);
  const groupNames = new Set(Object.keys(manifest.palette ?? {}).concat(Object.keys(manifest.type_scale ?? {})));
  for (const bad of forbidden ?? []) {
    if (groupNames.has(bad)) v.push(`forbidden token group "${bad}" exists in the manifest (law violation)`);
  }
  return v;
}

// ---------- runner (guarded activation) ----------

const PLATFORM_TOKEN_FILES = [
  ["ios", "ios/DesignKit/Sources/DesignKit/Tokens.swift"],
  ["android", "android/designkit/src/main/kotlin/app/client/designkit/Tokens.kt"],
];

function readJson(path) {
  return JSON.parse(readFileSync(path, "utf8"));
}

async function main() {
  const args = process.argv.slice(2);
  const idx = args.indexOf("--repo-root");
  const __dirname = dirname(fileURLToPath(import.meta.url));
  const repoRoot = idx >= 0 ? args[idx + 1] : join(__dirname, "..", "..");

  const violations = [];
  let manifest, design, brands;
  try {
    manifest = readJson(join(repoRoot, "design", "tokens.v1.json"));
    design = parseDesign(readFileSync(join(repoRoot, "DESIGN.md"), "utf8"));
    brands = {
      weeb: readJson(join(repoRoot, "brands", "weeb.json")),
      friki: readJson(join(repoRoot, "brands", "friki.json")),
    };
  } catch (e) {
    console.error(`token-lint BLOCKED: ${e.message}`);
    process.exitCode = 1;
    return;
  }

  violations.push(...checkManifestAgainstDesign(manifest, design));
  violations.push(...checkBrandDeltaAgainstBrands(manifest, brands));
  violations.push(...checkForbiddenGroups(manifest));

  let anyPlatform = false;
  for (const [name, rel] of PLATFORM_TOKEN_FILES) {
    const path = join(repoRoot, rel);
    if (existsSync(path)) {
      anyPlatform = true;
      violations.push(...checkPlatformLayer(rel, readFileSync(path, "utf8"), manifest));
    } else {
      console.log(`token-lint: ${rel} not present yet — platform-layer check guarded (arms at S7 ${name})`);
    }
  }

  if (violations.length > 0) {
    console.error("token-lint BLOCKED:");
    for (const x of violations) console.error(`  - ${x}`);
    process.exitCode = 1;
    return;
  }
  console.log(
    `token-lint OK: manifest ↔ DESIGN.md tables, brand-delta ↔ brands/*.json, no forbidden groups` +
      (anyPlatform ? "; platform layers verified value-for-value" : "; platform layers guarded (native trees land in this slice)")
  );
}

const isMain = process.argv[1] && import.meta.url === `file://${process.argv[1]}`;
if (isMain) {
  main();
}

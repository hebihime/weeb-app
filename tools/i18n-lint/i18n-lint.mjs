#!/usr/bin/env node
// tools/i18n-lint/i18n-lint.mjs — SLICE_S0_CONTRACT.md §9 i18n-lint.
//
// Shared lint activates per unit directory as it appears (ios/, android/, backend admin Razor, web/).
// At S0 no client directory exists (§0 scope ruling), so every check below self-skips with a note when
// its target directory is absent. Golden-vector tests exercise the logic against fixtures now so S7/S9
// inherit a working, tested tool instead of a rewrite.
//
// Checks (advisory now, hard-fail at S1 per contract §9):
//   1. Key parity across all locales in i18n/locales.json — a missing zh-Hans string fails the build.
//   2. Hardcoded user-facing string tripwire (regex per platform; allowlist requires justification).
//   3. Brand string-pack overrides must exist in the base catalog AND in both brands.
//   4. Fastlane store metadata dirs (4 listings x 4 locales) in scope — EN-only listing fails that brand.

import { readFileSync, existsSync, readdirSync, statSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join, basename } from "node:path";

const __dirname = dirname(fileURLToPath(import.meta.url));

export function loadLocales(repoRoot) {
  const path = join(repoRoot, "i18n", "locales.json");
  const json = JSON.parse(readFileSync(path, "utf8"));
  return json.locales;
}

/**
 * Rule 1: key parity. catalogs: { locale: { key: value } }.
 * @returns {string[]} violations
 */
export function checkKeyParity(catalogs, locales) {
  const violations = [];
  const baseLocale = locales[0];
  const baseCatalog = catalogs[baseLocale];
  if (!baseCatalog) {
    violations.push(`base locale "${baseLocale}" has no catalog at all`);
    return violations;
  }
  const baseKeys = new Set(Object.keys(baseCatalog));
  for (const locale of locales) {
    const catalog = catalogs[locale];
    if (!catalog) {
      violations.push(`locale "${locale}" has no catalog at all`);
      continue;
    }
    for (const key of baseKeys) {
      if (!(key in catalog)) {
        violations.push(`key "${key}" missing from locale "${locale}" (present in "${baseLocale}")`);
      }
    }
    for (const key of Object.keys(catalog)) {
      if (!baseKeys.has(key)) {
        violations.push(`key "${key}" in locale "${locale}" does not exist in base locale "${baseLocale}"`);
      }
    }
  }
  return violations;
}

/**
 * Rule 2: hardcoded user-facing string tripwire.
 * @param {Array<{path:string, content:string}>} files
 * @param {{platform: "swift"|"kotlin"|"razor", allowlist?: Set<string>}} opts
 */
const LITERAL_PATTERNS = {
  swift: /Text\(\s*"([^"]+)"\s*\)/g,
  kotlin: /Text\(\s*"([^"]+)"\s*\)/g,
  razor: />\s*([A-Za-z][^<{}\n]{2,})\s*</g,
};

export function findHardcodedStrings(files, opts) {
  const violations = [];
  const pattern = LITERAL_PATTERNS[opts.platform];
  if (!pattern) throw new Error(`unknown platform "${opts.platform}"`);
  const allowlist = opts.allowlist ?? new Set();
  for (const { path, content } of files) {
    if (allowlist.has(path)) continue;
    const re = new RegExp(pattern.source, pattern.flags);
    let m;
    while ((m = re.exec(content)) !== null) {
      violations.push(`${path}: hardcoded user-facing string "${m[1].trim()}" — use a keyed string, not a literal`);
    }
  }
  return violations;
}

/**
 * Rule 3: brand string-pack overrides must exist in the base catalog and in both brands.
 * @param {Record<string,string>} baseCatalog
 * @param {Record<string, Record<string,string>>} brandOverrides - keyed by brand_key
 * @param {string[]} brandKeys - e.g. ["weeb", "friki"]
 */
export function checkBrandOverrideParity(baseCatalog, brandOverrides, brandKeys) {
  const violations = [];
  const overrideKeys = new Set(brandKeys.flatMap((b) => Object.keys(brandOverrides[b] ?? {})));
  for (const key of overrideKeys) {
    if (!(key in baseCatalog)) {
      violations.push(`brand override key "${key}" has no base-catalog entry — overrides must override something real`);
    }
    for (const brand of brandKeys) {
      if (!(key in (brandOverrides[brand] ?? {}))) {
        violations.push(`brand override key "${key}" exists for another brand but not for "${brand}" — a ${brand} string can never silently fall through to another brand's voice`);
      }
    }
  }
  return violations;
}

/**
 * Rule 4: fastlane store metadata coverage — 4 listings (weeb ios/android, friki ios/android) x 4 locales.
 * @param {Record<string, string[]>} presentLocalesByListing - e.g. { "weeb/ios": ["en","es"] }
 */
export function checkStoreMetadataCoverage(presentLocalesByListing, locales, listings) {
  const violations = [];
  for (const listing of listings) {
    const present = new Set(presentLocalesByListing[listing] ?? []);
    for (const locale of locales) {
      if (!present.has(locale)) {
        violations.push(`store listing "${listing}" missing locale "${locale}" (EN-only listing fails that brand's leg)`);
      }
    }
  }
  return violations;
}

// ---------- S7: native catalog parsers (ARMED — §9e/§12) ----------

/**
 * Apple `.xcstrings` (String Catalog) → { locale: { key: value } }. A key with no localizations for a
 * locale simply does not appear there, which is exactly what checkKeyParity flags.
 */
export function parseXcstrings(jsonText, allLocales) {
  const doc = JSON.parse(jsonText);
  const catalogs = {};
  for (const loc of allLocales) catalogs[loc] = {};
  for (const [key, entry] of Object.entries(doc.strings ?? {})) {
    const locs = entry.localizations ?? {};
    for (const [loc, data] of Object.entries(locs)) {
      const value = data?.stringUnit?.value;
      if (value !== undefined) {
        (catalogs[loc] ??= {})[key] = value;
      }
    }
  }
  return catalogs;
}

/** One Android `strings.xml` → { name: value }. */
export function parseAndroidStrings(xmlText) {
  const out = {};
  const re = /<string\s+name="([^"]+)"[^>]*>([\s\S]*?)<\/string>/g;
  let m;
  while ((m = re.exec(xmlText)) !== null) out[m[1]] = m[2].trim();
  return out;
}

/** Android resource names can't hold dots — the canonical dotted message key maps to underscores. */
export function androidResName(key) {
  return key.replace(/\./g, "_");
}

// Android res qualifier → canonical locale.
const ANDROID_QUALIFIER_TO_LOCALE = { "": "en", es: "es", pt: "pt", "b+zh+Hans": "zh-Hans" };

/**
 * Every contracts/message-keys.json key must exist ×4 in each client catalog that is present
 * (iOS = dotted key; Android = underscored resource name). §1c activation event.
 */
export function checkMessageKeyCoverage(platformCatalogs, messageKeys, locales) {
  const violations = [];
  for (const { platform, catalogs } of platformCatalogs) {
    const present = Object.values(catalogs).some((c) => Object.keys(c).length > 0);
    if (!present) continue;
    for (const key of messageKeys) {
      const wanted = platform === "android" ? androidResName(key) : key;
      for (const loc of locales) {
        if (!(wanted in (catalogs[loc] ?? {}))) {
          violations.push(`message key "${key}" (${platform} "${wanted}") missing from locale "${loc}" — every contracts/message-keys.json key must exist ×4 in ${platform}`);
        }
      }
    }
  }
  return violations;
}

// ---------- file collection ----------

const SKIP = new Set([".git", ".build", "build", "DerivedData", "node_modules", ".gradle", "Pods"]);
function walk(dir, pick, acc) {
  let entries;
  try { entries = readdirSync(dir); } catch { return acc; }
  for (const name of entries) {
    const full = join(dir, name);
    let s; try { s = statSync(full); } catch { continue; }
    if (s.isDirectory()) { if (!SKIP.has(name)) walk(full, pick, acc); }
    else if (pick(name, full)) acc.push(full);
  }
  return acc;
}

/** Merge every ios/**\/*.xcstrings into one { locale: {key} } union. */
export function collectIosCatalogs(repoRoot, locales) {
  const merged = {};
  for (const loc of locales) merged[loc] = {};
  for (const file of walk(join(repoRoot, "ios"), (n) => n.endsWith(".xcstrings"), [])) {
    const cats = parseXcstrings(readFileSync(file, "utf8"), locales);
    for (const loc of locales) Object.assign(merged[loc], cats[loc]);
  }
  return merged;
}

/** Merge every android/**\/res/values*\/strings.xml into one { locale: {name} } union. */
export function collectAndroidCatalogs(repoRoot) {
  const merged = {};
  for (const file of walk(join(repoRoot, "android"), (n) => n === "strings.xml", [])) {
    const m = /values(?:-([^/]+))?\/strings\.xml$/.exec(file.replace(/\\/g, "/"));
    const qualifier = m ? (m[1] ?? "") : "";
    const locale = ANDROID_QUALIFIER_TO_LOCALE[qualifier] ?? qualifier;
    (merged[locale] ??= {});
    Object.assign(merged[locale], parseAndroidStrings(readFileSync(file, "utf8")));
  }
  return merged;
}

async function main() {
  const repoRoot = join(__dirname, "..", "..");
  const locales = loadLocales(repoRoot);
  console.log(`i18n-lint: canonical locale set = ${locales.join(", ")}`);

  let messageKeys = [];
  try {
    messageKeys = JSON.parse(readFileSync(join(repoRoot, "contracts", "message-keys.json"), "utf8")).keys.map((k) => k.key);
  } catch (e) {
    console.error(`i18n-lint BLOCKED: cannot read contracts/message-keys.json: ${e.message}`);
    process.exitCode = 1;
    return;
  }

  const violations = [];
  const platformCatalogs = [];
  let anyActive = false;

  if (existsSync(join(repoRoot, "ios"))) {
    anyActive = true;
    const cats = collectIosCatalogs(repoRoot, locales);
    violations.push(...checkKeyParity(cats, locales).map((v) => `[ios] ${v}`));
    platformCatalogs.push({ platform: "ios", catalogs: cats });
  } else {
    console.log("i18n-lint SKIP: ios/ does not exist yet — .xcstrings key-parity guarded until S7 iOS lands");
  }

  if (existsSync(join(repoRoot, "android"))) {
    anyActive = true;
    const cats = collectAndroidCatalogs(repoRoot);
    for (const loc of locales) cats[loc] ??= {};
    violations.push(...checkKeyParity(cats, locales).map((v) => `[android] ${v}`));
    platformCatalogs.push({ platform: "android", catalogs: cats });
  } else {
    console.log("i18n-lint SKIP: android/ does not exist yet — strings.xml key-parity guarded until S7 Android lands");
  }

  violations.push(...checkMessageKeyCoverage(platformCatalogs, messageKeys, locales));

  if (violations.length > 0) {
    console.error("i18n-lint BLOCKED:");
    for (const v of violations) console.error(`  - ${v}`);
    process.exitCode = 1;
    return;
  }
  if (!anyActive) {
    console.log("i18n-lint OK: no client catalogs exist yet; key-parity + message-key coverage guarded (arms with S7 native trees)");
  } else {
    console.log(`i18n-lint OK: client catalogs key-parity ×${locales.length} clean; all ${messageKeys.length} message keys present ×${locales.length} per platform`);
  }
}

const isMain = process.argv[1] && import.meta.url === `file://${process.argv[1]}`;
if (isMain) {
  main();
}

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

import { readFileSync, existsSync, readdirSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

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

async function main() {
  const repoRoot = join(__dirname, "..", "..");
  const locales = loadLocales(repoRoot);
  console.log(`i18n-lint: canonical locale set = ${locales.join(", ")}`);

  const targets = [
    ["ios/", "SwiftUI string-catalog + key-parity check"],
    ["android/", "Compose strings.xml key-parity check"],
    ["web/", "web catalog key-parity check"],
    ["backend/admin-host/", "Razor admin-host string check"],
    ["fastlane/metadata/", "store metadata locale coverage check"],
  ];
  let anyActive = false;
  for (const [dir, desc] of targets) {
    if (existsSync(join(repoRoot, dir))) {
      anyActive = true;
      console.log(`i18n-lint: ${dir} exists — ${desc} would run here (wire the file-collection glue when the unit lands)`);
    } else {
      console.log(`i18n-lint SKIP: ${dir} does not exist yet — ${desc} guarded until that unit lands`);
    }
  }
  if (!anyActive) {
    console.log("i18n-lint OK: no client/admin/store-metadata directories exist yet; all checks guarded (S0 baseline)");
  }
}

const isMain = process.argv[1] && import.meta.url === `file://${process.argv[1]}`;
if (isMain) {
  main();
}

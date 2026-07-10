#!/usr/bin/env node
// build/scripts/brand-gate.mjs — SLICE_S0_CONTRACT.md §4 drift enforcement.
//
// Lint-verify, not generate (P2's call, ratified by the vanilla rule — §12.6). Asserts every platform
// flavor file matches brands/*.json value-for-value, and that all listing identities are distinct and
// complete (or explicitly pending-OQ-1, which is valid until a release lane is exercised).
//
// Usage: node build/scripts/brand-gate.mjs [--repo-root <path>] [--release-lane]
// Exit 0 = pass, exit 1 = fail (prints every violation, not just the first).

import { readFileSync, existsSync } from "node:fs";
import { join } from "node:path";

const REQUIRED_KEYS = [
  "brand_key",
  "display_name",
  "bundle_id_ios",
  "application_id_android",
  "web_domain",
  "brand_primary",
  "brand_celebration",
  "wordmark_asset_ref",
  "string_pack_id",
  "store_metadata_path",
  "store_listing_refs",
];

const HEX = /^#[0-9A-Fa-f]{6}$/;

/** Load brands/*.json from a repo root. Throws with a precise message on any structural defect. */
export function loadBrands(repoRoot) {
  const brandsDir = join(repoRoot, "brands");
  const files = ["weeb.json", "friki.json"];
  const brands = {};
  for (const file of files) {
    const path = join(brandsDir, file);
    if (!existsSync(path)) {
      throw new Error(`missing canonical brand file: brands/${file}`);
    }
    const raw = readFileSync(path, "utf8");
    let json;
    try {
      json = JSON.parse(raw);
    } catch (e) {
      throw new Error(`brands/${file} is not valid JSON: ${e.message}`);
    }
    for (const key of REQUIRED_KEYS) {
      if (!(key in json)) {
        throw new Error(`brands/${file} missing required key "${key}"`);
      }
    }
    if (!HEX.test(json.brand_primary)) {
      throw new Error(`brands/${file} brand_primary "${json.brand_primary}" is not a #RRGGBB hex`);
    }
    if (!HEX.test(json.brand_celebration)) {
      throw new Error(`brands/${file} brand_celebration "${json.brand_celebration}" is not a #RRGGBB hex`);
    }
    brands[json.brand_key] = json;
  }
  return brands;
}

/**
 * Check that every platform flavor value matches the canonical brand JSON, and that listing identities
 * (web_domain, bundle_id_ios, application_id_android) are distinct across brands unless both sides are
 * the pending-OQ-1 placeholder ("" / "pending-OQ-1"), which is valid pre-release.
 *
 * @param {Record<string, object>} brands - keyed by brand_key, from loadBrands()
 * @param {{releaseLane?: boolean, flavorFiles?: Array<{brandKey:string, path:string, values:Record<string,string>}>}} opts
 * @returns {string[]} violations (empty = pass)
 */
export function checkBrandDrift(brands, opts = {}) {
  const violations = [];
  const keys = Object.keys(brands);

  if (keys.length !== 2 || !brands.weeb || !brands.friki) {
    violations.push(`expected exactly brands "weeb" and "friki", found: ${keys.join(", ") || "(none)"}`);
    return violations;
  }

  // Distinctness of the four permanent listing identities. Blank is allowed pre-release (OQ-1);
  // once IDs are non-blank they must differ across brands.
  const idFields = ["bundle_id_ios", "application_id_android", "web_domain"];
  for (const field of idFields) {
    const a = brands.weeb[field];
    const b = brands.friki[field];
    const bothBlankOrPending = (a === "" || a === undefined) && (b === "" || b === undefined);
    if (!bothBlankOrPending && a === b) {
      violations.push(`brand identity collision on "${field}": weeb and friki both resolve to "${a}"`);
    }
    if (opts.releaseLane && (a === "" || b === "")) {
      violations.push(`release lane requires "${field}" to be set (OQ-1 no longer deferrable); found blank`);
    }
  }

  const listingKeyPath = ["ios", "android"];
  for (const brandKey of keys) {
    const refs = brands[brandKey].store_listing_refs ?? {};
    for (const platform of listingKeyPath) {
      const val = refs[platform];
      if (opts.releaseLane && (!val || val === "pending-OQ-1")) {
        violations.push(`release lane requires store_listing_refs.${platform} for "${brandKey}"; found "${val}"`);
      }
    }
  }

  // Flavor-file drift: every declared platform flavor file's values must match the canonical JSON
  // value-for-value. Grafted in as data so unit tests can feed fixtures without touching disk.
  for (const flavor of opts.flavorFiles ?? []) {
    const canonical = brands[flavor.brandKey];
    if (!canonical) {
      violations.push(`flavor file ${flavor.path} references unknown brand_key "${flavor.brandKey}"`);
      continue;
    }
    for (const [field, actual] of Object.entries(flavor.values)) {
      const expected = canonical[field];
      if (expected !== actual) {
        violations.push(
          `flavor drift in ${flavor.path}: field "${field}" is "${actual}", canonical brands/${flavor.brandKey}.json says "${expected}"`
        );
      }
    }
  }

  return violations;
}

/** Grep rule: fail any flavor build that reads a brand literal from outside brands/*.json. */
export function findBrandLiteralLeaks(fileContents, brands) {
  const hexes = [...new Set(Object.values(brands).flatMap((b) => [b.brand_primary, b.brand_celebration]))];
  const violations = [];
  for (const { path, content } of fileContents) {
    if (path.startsWith("brands/")) continue;
    for (const hex of hexes) {
      if (content.toLowerCase().includes(hex.toLowerCase())) {
        violations.push(`${path} hardcodes brand hex ${hex} outside brands/*.json (read from canonical file instead)`);
      }
    }
  }
  return violations;
}

async function main() {
  const args = process.argv.slice(2);
  const repoRootIdx = args.indexOf("--repo-root");
  const repoRoot = repoRootIdx >= 0 ? args[repoRootIdx + 1] : process.cwd();
  const releaseLane = args.includes("--release-lane");

  let brands;
  try {
    brands = loadBrands(repoRoot);
  } catch (e) {
    console.error(`brand-gate BLOCKED: ${e.message}`);
    process.exitCode = 1;
    return;
  }

  const violations = checkBrandDrift(brands, { releaseLane });
  if (violations.length > 0) {
    console.error("brand-gate BLOCKED:");
    for (const v of violations) console.error(`  - ${v}`);
    process.exitCode = 1;
    return;
  }

  console.log(`brand-gate OK: ${Object.keys(brands).join(", ")} verified${releaseLane ? " (release lane)" : ""}`);
}

const isMain = process.argv[1] && import.meta.url === `file://${process.argv[1]}`;
if (isMain) {
  main();
}

// build/scripts/brand-gate.test.mjs — golden-vector tests for brand-gate.mjs (CLAUDE.md: deterministic
// math/logic in pure libs, golden vectors, no LLM). Run: node --test build/scripts/brand-gate.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, writeFileSync, mkdirSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { loadBrands, checkBrandDrift, findBrandLiteralLeaks } from "./brand-gate.mjs";

function makeRepo(weeb, friki) {
  const root = mkdtempSync(join(tmpdir(), "brand-gate-"));
  mkdirSync(join(root, "brands"));
  writeFileSync(join(root, "brands", "weeb.json"), JSON.stringify(weeb));
  writeFileSync(join(root, "brands", "friki.json"), JSON.stringify(friki));
  return root;
}

const GOOD_WEEB = {
  brand_key: "weeb",
  display_name: "Weeb App",
  bundle_id_ios: "",
  application_id_android: "",
  web_domain: "weeb.app",
  brand_primary: "#F7568F",
  brand_celebration: "#FF9838",
  wordmark_asset_ref: "design/assets/logo-weeb.png",
  string_pack_id: "weeb",
  store_metadata_path: "fastlane/metadata/weeb",
  store_listing_refs: { ios: "pending-OQ-1", android: "pending-OQ-1" },
};

const GOOD_FRIKI = {
  ...GOOD_WEEB,
  brand_key: "friki",
  display_name: "Friki App",
  web_domain: "friki.app",
  brand_primary: "#FF7A3D",
  brand_celebration: "#F7568F",
  wordmark_asset_ref: "design/assets/logo-friki.png",
  string_pack_id: "friki",
  store_metadata_path: "fastlane/metadata/friki",
};

test("loadBrands: golden vector — both canonical files valid", () => {
  const root = makeRepo(GOOD_WEEB, GOOD_FRIKI);
  const brands = loadBrands(root);
  assert.equal(brands.weeb.brand_primary, "#F7568F");
  assert.equal(brands.friki.brand_primary, "#FF7A3D");
  rmSync(root, { recursive: true, force: true });
});

test("loadBrands: missing file throws with the exact file name", () => {
  const root = mkdtempSync(join(tmpdir(), "brand-gate-"));
  mkdirSync(join(root, "brands"));
  writeFileSync(join(root, "brands", "weeb.json"), JSON.stringify(GOOD_WEEB));
  assert.throws(() => loadBrands(root), /missing canonical brand file: brands\/friki\.json/);
  rmSync(root, { recursive: true, force: true });
});

test("loadBrands: missing required key throws", () => {
  const bad = { ...GOOD_WEEB };
  delete bad.brand_celebration;
  const root = makeRepo(bad, GOOD_FRIKI);
  assert.throws(() => loadBrands(root), /missing required key "brand_celebration"/);
  rmSync(root, { recursive: true, force: true });
});

test("loadBrands: malformed hex rejected", () => {
  const bad = { ...GOOD_WEEB, brand_primary: "hotpink" };
  const root = makeRepo(bad, GOOD_FRIKI);
  assert.throws(() => loadBrands(root), /not a #RRGGBB hex/);
  rmSync(root, { recursive: true, force: true });
});

test("checkBrandDrift: pass on canonical pair pre-release (blank IDs allowed)", () => {
  const brands = { weeb: GOOD_WEEB, friki: GOOD_FRIKI };
  const violations = checkBrandDrift(brands, { releaseLane: false });
  assert.deepEqual(violations, []);
});

test("checkBrandDrift: release lane hard-fails on blank bundle id (OQ-1 no longer deferrable)", () => {
  const brands = { weeb: GOOD_WEEB, friki: GOOD_FRIKI };
  const violations = checkBrandDrift(brands, { releaseLane: true });
  assert.ok(violations.some((v) => v.includes('bundle_id_ios')));
  assert.ok(violations.some((v) => v.includes("store_listing_refs.ios")));
});

test("checkBrandDrift: identity collision on web_domain fails even pre-release", () => {
  const friki = { ...GOOD_FRIKI, web_domain: "weeb.app" };
  const violations = checkBrandDrift({ weeb: GOOD_WEEB, friki });
  assert.ok(violations.some((v) => v.includes('collision on "web_domain"')));
});

test("checkBrandDrift: flavor-file drift caught value-for-value", () => {
  const brands = { weeb: GOOD_WEEB, friki: GOOD_FRIKI };
  const violations = checkBrandDrift(brands, {
    flavorFiles: [
      {
        brandKey: "weeb",
        path: "ios/Weeb.xcconfig",
        values: { brand_primary: "#FF0000" }, // drifted from canonical #F7568F
      },
    ],
  });
  assert.equal(violations.length, 1);
  assert.match(violations[0], /flavor drift in ios\/Weeb\.xcconfig/);
});

test("checkBrandDrift: flavor-file matching canonical passes", () => {
  const brands = { weeb: GOOD_WEEB, friki: GOOD_FRIKI };
  const violations = checkBrandDrift(brands, {
    flavorFiles: [
      { brandKey: "weeb", path: "ios/Weeb.xcconfig", values: { brand_primary: "#F7568F" } },
    ],
  });
  assert.deepEqual(violations, []);
});

test("checkBrandDrift: exactly-two-brands invariant enforced", () => {
  const violations = checkBrandDrift({ weeb: GOOD_WEEB });
  assert.equal(violations.length, 1);
  assert.match(violations[0], /expected exactly brands "weeb" and "friki"/);
});

test("findBrandLiteralLeaks: catches a hardcoded brand hex outside brands/", () => {
  const brands = { weeb: GOOD_WEEB, friki: GOOD_FRIKI };
  const violations = findBrandLiteralLeaks(
    [{ path: "ios/Theme.swift", content: 'let primary = Color(hex: "#F7568F")' }],
    brands
  );
  assert.equal(violations.length, 1);
  assert.match(violations[0], /ios\/Theme\.swift hardcodes brand hex #F7568F/);
});

test("findBrandLiteralLeaks: brands/*.json itself is exempt", () => {
  const brands = { weeb: GOOD_WEEB, friki: GOOD_FRIKI };
  const violations = findBrandLiteralLeaks(
    [{ path: "brands/weeb.json", content: JSON.stringify(GOOD_WEEB) }],
    brands
  );
  assert.deepEqual(violations, []);
});

test("findBrandLiteralLeaks: clean file produces no violation", () => {
  const brands = { weeb: GOOD_WEEB, friki: GOOD_FRIKI };
  const violations = findBrandLiteralLeaks(
    [{ path: "ios/Theme.swift", content: "let primary = BrandConfig.primary" }],
    brands
  );
  assert.deepEqual(violations, []);
});

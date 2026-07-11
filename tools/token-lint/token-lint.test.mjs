// tools/token-lint/token-lint.test.mjs — golden-vector tests (CLAUDE.md: deterministic logic in pure
// libs, no LLM). Red-fixture-proven BOTH directions per §12. Run: node --test tools/token-lint/token-lint.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";
import {
  parseDesignColorTable,
  parseDesignLight,
  parseDesignDark,
  parseDesignTypeScale,
  parseDesignBrandDelta,
  parseDesign,
  checkManifestAgainstDesign,
  checkBrandDeltaAgainstBrands,
  checkPlatformLayer,
  checkForbiddenGroups,
} from "./token-lint.mjs";

const __dirname = dirname(fileURLToPath(import.meta.url));
const repoRoot = join(__dirname, "..", "..");
const DESIGN = readFileSync(join(repoRoot, "DESIGN.md"), "utf8");
const MANIFEST = JSON.parse(readFileSync(join(repoRoot, "design", "tokens.v1.json"), "utf8"));
const BRANDS = {
  weeb: JSON.parse(readFileSync(join(repoRoot, "brands", "weeb.json"), "utf8")),
  friki: JSON.parse(readFileSync(join(repoRoot, "brands", "friki.json"), "utf8")),
};

// ---------- parsers pull the real values out of the real DESIGN.md ----------

test("parseDesignColorTable: the five candy + three semantic colors", () => {
  const c = parseDesignColorTable(DESIGN);
  assert.equal(c.Bubblegum, "#F7568F");
  assert.equal(c.Sky, "#38BDF2");
  assert.equal(c.Mikan, "#FF9838");
  assert.equal(c.Foil, "#C99A2E");
  assert.equal(c.Choco, "#1E1410");
  assert.equal(c.good, "#3FB950");
  assert.equal(c.warn, "#F5A623");
  assert.equal(c.danger, "#ED4245");
});

test("parseDesignLight / parseDesignDark: full light + dark surface sets", () => {
  const l = parseDesignLight(DESIGN);
  assert.deepEqual(l, {
    ground: "#FFFFFF", surface: "#FFFFFF", surface_2: "#F7F5F2",
    hairline: "#E8E3DD", text: "#26170F", dim: "#8A7C72", outline: "#2B1B12",
  });
  const d = parseDesignDark(DESIGN);
  assert.deepEqual(d, {
    ground: "#1E1410", surface: "#2A1D16", surface_2: "#35251D",
    line: "#48362B", text: "#FBF3EC", dim: "#B3A093", outline: "#F5EBE2",
  });
});

test("parseDesignTypeScale: seven roles with normalised names", () => {
  const s = parseDesignTypeScale(DESIGN);
  assert.deepEqual(s.display_xl, { size: 34, line: 40, weight: 900 });
  assert.deepEqual(s.body, { size: 15, line: 22, weight: 500 });
  assert.deepEqual(s.micro_label, { size: 11, line: 14, weight: 700 });
});

test("parseDesignBrandDelta: primary + celebration per brand", () => {
  const b = parseDesignBrandDelta(DESIGN);
  assert.deepEqual(b.primary, { weeb: "#F7568F", friki: "#FF7A3D" });
  assert.deepEqual(b.celebration, { weeb: "#FF9838", friki: "#F7568F" });
});

// ---------- the shipped manifest is faithful (green direction, against real files) ----------

test("shipped tokens.v1.json matches DESIGN.md value-for-value", () => {
  assert.deepEqual(checkManifestAgainstDesign(MANIFEST, parseDesign(DESIGN)), []);
});

test("shipped tokens.v1.json brand-delta matches brands/*.json", () => {
  assert.deepEqual(checkBrandDeltaAgainstBrands(MANIFEST, BRANDS), []);
});

test("shipped tokens.v1.json declares forbidden groups and has none of them", () => {
  assert.deepEqual(checkForbiddenGroups(MANIFEST), []);
});

// ---------- red fixtures: every check bites when drift is introduced ----------

test("RED: manifest palette drifted from DESIGN.md is caught", () => {
  const bad = structuredClone(MANIFEST);
  bad.palette.candy.bubblegum = "#000000";
  const v = checkManifestAgainstDesign(bad, parseDesign(DESIGN));
  assert.ok(v.some((x) => x.includes("candy.bubblegum") && x.includes("DESIGN.md says #F7568F")));
});

test("RED: manifest type-scale drift is caught", () => {
  const bad = structuredClone(MANIFEST);
  bad.type_scale.body.size = 99;
  const v = checkManifestAgainstDesign(bad, parseDesign(DESIGN));
  assert.ok(v.some((x) => x.includes("type_scale.body")));
});

test("RED: brand-delta not matching brands/*.json is caught", () => {
  const bad = structuredClone(MANIFEST);
  bad.brand_delta.friki.primary = "#F7568F"; // friki primary is Tangerine, not Bubblegum
  const v = checkBrandDeltaAgainstBrands(bad, BRANDS);
  assert.ok(v.some((x) => x.includes("brand_delta.friki.primary")));
});

test("RED: a forbidden token group present in the manifest is caught", () => {
  const bad = structuredClone(MANIFEST);
  bad.palette.disabled = { grey: "#CCCCCC" };
  bad.forbidden_token_groups.push("disabled");
  const v = checkForbiddenGroups(bad);
  assert.ok(v.some((x) => x.includes('forbidden token group "disabled" exists')));
});

test("RED: missing forbidden_token_groups list is caught", () => {
  const bad = structuredClone(MANIFEST);
  delete bad.forbidden_token_groups;
  const v = checkForbiddenGroups(bad);
  assert.ok(v.some((x) => x.includes("must list forbidden_token_groups")));
});

// ---------- platform-layer check (b), both directions ----------

test("checkPlatformLayer: GREEN when the layer holds exactly the manifest palette", () => {
  const hexes = [
    ...Object.values(MANIFEST.palette.candy),
    ...Object.values(MANIFEST.palette.semantic),
    ...Object.values(MANIFEST.palette.light),
    ...Object.values(MANIFEST.palette.dark),
  ];
  const content = hexes.map((h, i) => `static let c${i} = "${h}"`).join("\n");
  assert.deepEqual(checkPlatformLayer("Tokens.swift", content, MANIFEST), []);
});

test("RED: platform layer with a rogue (non-manifest) hex is caught", () => {
  const content = `static let bubblegum = "#F7568F"\nstatic let rogue = "#ABCDEF"`;
  const v = checkPlatformLayer("Tokens.swift", content, MANIFEST);
  assert.ok(v.some((x) => x.includes("#ABCDEF") && x.includes("rogue color")));
});

test("RED: platform layer missing a manifest palette value is caught", () => {
  const content = `static let bubblegum = "#F7568F"`; // only one of many
  const v = checkPlatformLayer("Tokens.kt", content, MANIFEST);
  assert.ok(v.some((x) => x.includes("never appears") && x.includes("out of sync")));
});

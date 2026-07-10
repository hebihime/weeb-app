// infra/edge-guard.test.mjs — unit assertions on the rendered edge-guard rules (SLICE_S0_CONTRACT.md
// §9: "with unit assertions on the rendered rules"). No `az`/`bicep` CLI is available in every
// environment that runs this gate, so this test asserts structurally against the .bicep source itself
// (three named, prioritized, Block-action MatchRule custom rules covering exactly the traversal classes
// L17 names) AND cross-checks backend/e2e/edge-guard.mjs's adversarial path list stays in sync.
// CI additionally runs `az bicep build infra/edge-guard.bicep` (infra.yml) as the real compiler check —
// this test is the fast, dependency-free proxy that runs everywhere, including here.
//
// Run: node --test infra/edge-guard.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";
import { ADVERSARIAL_PATHS } from "../backend/e2e/edge-guard.mjs";

const __dirname = dirname(fileURLToPath(import.meta.url));
const bicepSource = readFileSync(join(__dirname, "edge-guard.bicep"), "utf8");

function extractRuleBlock(name) {
  const re = new RegExp(`name:\\s*'${name}'([\\s\\S]*?)(?=\\n\\s*\\}\\n\\s*\\{|\\n\\s*\\]\\n)`);
  const m = bicepSource.match(re);
  return m ? m[0] : null;
}

test("edge-guard.bicep declares exactly three named custom rules, priorities 1-3", () => {
  const names = [...bicepSource.matchAll(/name:\s*'(Reject\w+)'/g)].map((m) => m[1]);
  assert.deepEqual(names, ["RejectEncodedTraversal", "RejectDotSegments", "RejectInternalReachThrough"]);
  const priorities = [...bicepSource.matchAll(/priority:\s*(\d+)/g)].map((m) => Number(m[1]));
  assert.deepEqual(priorities, [1, 2, 3]);
});

test("every custom rule action is Block (reject before rewrite, never allow-and-log)", () => {
  const actions = [...bicepSource.matchAll(/action:\s*'(\w+)'/g)].map((m) => m[1]);
  assert.ok(actions.length === 3, `expected 3 action declarations, found ${actions.length}`);
  assert.ok(actions.every((a) => a === "Block"), `expected every rule action to be Block, found: ${actions.join(", ")}`);
});

test("RejectEncodedTraversal rule covers %2e/%2f/%5c in both cases", () => {
  const block = extractRuleBlock("RejectEncodedTraversal");
  assert.ok(block, "RejectEncodedTraversal rule not found");
  for (const token of ["%2e", "%2f", "%5c"]) {
    assert.ok(block.toLowerCase().includes(token), `expected ${token} in RejectEncodedTraversal matchValue`);
  }
});

test("RejectDotSegments rule covers forward- and back-slash dot-segment forms", () => {
  const block = extractRuleBlock("RejectDotSegments");
  assert.ok(block, "RejectDotSegments rule not found");
  assert.ok(block.includes("../"));
  assert.ok(block.includes(".."));
});

test("RejectInternalReachThrough rule covers /internal", () => {
  const block = extractRuleBlock("RejectInternalReachThrough");
  assert.ok(block, "RejectInternalReachThrough rule not found");
  assert.ok(block.includes("/internal"));
});

test("edge-guard.bicep policy mode is Prevention, not Detection (fail-closed, not log-only)", () => {
  assert.match(bicepSource, /mode:\s*'Prevention'/);
});

test("backend/e2e/edge-guard.mjs adversarial path list covers all three rule classes", () => {
  const hasEncodedTraversal = ADVERSARIAL_PATHS.some((p) => /%2e|%2f|%5c/i.test(p));
  const hasDotSegment = ADVERSARIAL_PATHS.some((p) => p.includes("../") || p.includes("..\\"));
  const hasInternal = ADVERSARIAL_PATHS.some((p) => p.includes("/internal"));
  assert.ok(hasEncodedTraversal, "no adversarial path exercises encoded traversal");
  assert.ok(hasDotSegment, "no adversarial path exercises dot-segments");
  assert.ok(hasInternal, "no adversarial path exercises /internal reach-through");
});

test("ADVERSARIAL_PATHS has no duplicate entries", () => {
  assert.equal(new Set(ADVERSARIAL_PATHS).size, ADVERSARIAL_PATHS.length);
});

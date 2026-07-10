// infra/edge-guard.idor-lens.test.mjs — ADVERSARIAL LENS: encoded traversal / topology-is-not-a-guard.
//
// These tests are written to FAIL against the current infra/edge-guard.bicep. They prove that the L17
// edge-guard, which SLICE_S0_CONTRACT.md §9 says makes encoded traversal and /internal reach-through
// "404 BEFORE any rewrite ever runs", only rejects SINGLE-encoded payloads. The canonical WAF bypasses
// (double URL-encoding, and percent-encoding a character inside /internal) sail straight through, because
// the rules carry a `Lowercase` transform but no `UrlDecode` transform.
//
// The matcher below faithfully replays Azure Front Door WAF custom-rule semantics against the rendered
// rules parsed out of edge-guard.bicep: for each rule, apply its declared `transforms` to the request
// URI, then Block if the transformed URI Contains any declared matchValue. Transforms apply to the
// request field, not the matchValue — exactly as Azure does.
//
// Run: node --test infra/edge-guard.idor-lens.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";
import { ADVERSARIAL_PATHS } from "../backend/e2e/edge-guard.mjs";

const __dirname = dirname(fileURLToPath(import.meta.url));
const bicepSource = readFileSync(join(__dirname, "edge-guard.bicep"), "utf8");

// --- Parse the rendered custom rules out of the bicep source -----------------------------------------
function parseRules(src) {
  const rules = [];
  const ruleRe = /name:\s*'(Reject\w+)'([\s\S]*?)(?=\n\s{8}\}\n\s{8}\{|\n\s{6}\]\n)/g;
  let m;
  while ((m = ruleRe.exec(src))) {
    const body = m[2];
    const matchValues = [...body.matchAll(/matchValue:\s*\[([^\]]*)\]/g)]
      .flatMap((x) => x[1].split(",").map((s) => s.trim().replace(/^'|'$/g, "")))
      .filter(Boolean);
    const tfMatch = body.match(/transforms:\s*\[([^\]]*)\]/);
    const transforms = tfMatch
      ? tfMatch[1].split(",").map((s) => s.trim().replace(/^'|'$/g, "")).filter(Boolean)
      : [];
    rules.push({ name: m[1], matchValues, transforms });
  }
  return rules;
}

const RULES = parseRules(bicepSource);

function applyTransforms(s, transforms) {
  let out = s;
  for (const t of transforms) {
    if (t === "Lowercase") out = out.toLowerCase();
    if (t === "Uppercase") out = out.toUpperCase();
    if (t === "UrlDecode") {
      try {
        out = decodeURIComponent(out);
      } catch {
        /* malformed stays as-is, same as WAF */
      }
    }
  }
  return out;
}

/** Returns the name of the first rule that would Block this URI, or null if it reaches the origin. */
function wafBlocks(uri) {
  for (const rule of RULES) {
    const transformed = applyTransforms(uri, rule.transforms);
    if (rule.matchValues.some((v) => transformed.includes(v.toLowerCase()))) {
      return rule.name;
    }
  }
  return null;
}

// --- Control: single-encoded payloads ARE blocked (proves the matcher is faithful) ------------------
test("[control] single-encoded traversal and plain /internal are blocked today", () => {
  assert.ok(wafBlocks("/%2e%2e/etc/passwd"), "single-encoded %2e%2e should be blocked");
  assert.ok(wafBlocks("/../../etc/passwd"), "plain dot-segments should be blocked");
  assert.ok(wafBlocks("/internal/admin"), "plain /internal should be blocked");
});

// --- BREAK 1: double URL-encoded traversal bypasses every rule --------------------------------------
// Input /%252e%252e/%252e%252e/etc/passwd : the WAF sees literal "%252e", which does not contain the
// substring "%2e", so no rule matches. The origin (Front Door -> backend, or ASP.NET path handling)
// decodes once to /%2e%2e/... and again to /../../etc/passwd. Traversal reaches the app.
test("BREAK: double URL-encoded traversal must 404 at the edge (encoded-traversal class)", () => {
  const payload = "/%252e%252e/%252e%252e/etc/passwd";
  assert.ok(
    wafBlocks(payload),
    `double-encoded traversal ${payload} reaches origin — RejectEncodedTraversal has no UrlDecode transform; add UrlDecode (and match %25) so ../.. cannot be smuggled through one decode layer`
  );
});

// --- BREAK 2: percent-encoding a char inside /internal defeats the reach-through block ---------------
// Input /%69nternal/admin : "%69" is "i". The WAF sees /%69nternal, which does not contain "/internal".
// Origin decodes to /internal/admin — the exact reach-through the rule claims to kill.
test("BREAK: encoded /internal reach-through must 404 at the edge (topology-is-not-a-guard)", () => {
  const payload = "/%69nternal/admin";
  assert.ok(
    wafBlocks(payload),
    `encoded ${payload} reaches origin as /internal/admin — RejectInternalReachThrough has no UrlDecode transform. Edge topology-string blocking is not an authz guard: this leg only works if it also normalizes encodings before matching`
  );
});

// --- BREAK 3: the "fixed adversarial contract" the S9 e2e inherits omits the double-encoding class ---
// backend/e2e/edge-guard.mjs is billed as the fixed adversarial corpus every later slice inherits. It
// contains zero double-encoded payloads, so a vulnerable S9 host would pass edge-guard e2e green.
test("BREAK: ADVERSARIAL_PATHS must include a double-encoded traversal probe", () => {
  const hasDoubleEncoded = ADVERSARIAL_PATHS.some((p) => /%25(2e|2f|5c)/i.test(p));
  assert.ok(
    hasDoubleEncoded,
    "no adversarial path exercises double URL-encoding (%252e/%252f/%255c) — the S9 reachability e2e cannot catch the most common WAF traversal bypass it inherits from S0"
  );
});

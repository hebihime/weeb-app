#!/usr/bin/env node
// backend/e2e/edge-guard.mjs — SLICE_S0_CONTRACT.md §9, L17 (S9's half; S0 ships this script so the
// adversarial contract is fixed from day one and inherited, not invented at S9).
//
// Adversarial reachability check: throws these payloads at a public host and asserts every one 404s
// BEFORE any rewrite happens. Self-skips (exit 0, printed note) until a public host answers — there is
// no host to test until S9 deploys one. Never silently "passes" by skipping the assertions; it either
// runs them for real or explicitly declines to.
//
// Usage: EDGE_GUARD_TARGET=https://weeb.app node backend/e2e/edge-guard.mjs
//        (no EDGE_GUARD_TARGET set -> SKIP, not a lie)

const TARGET = process.env.EDGE_GUARD_TARGET;

// Mirrors infra/edge-guard.bicep's three custom rules exactly — keep these in sync value-for-value;
// a drift here means the e2e stops proving what the WAF policy claims to block.
export const ADVERSARIAL_PATHS = [
  "/%2e%2e/%2e%2e/etc/passwd",
  "/..%2f..%2fetc/passwd",
  "/..%5c..%5cwindows/win.ini",
  "/../../etc/passwd",
  "/..\\..\\windows\\win.ini",
  "/internal/admin",
  "/internal/",
  "/api/internal/config",
  // Double URL-encoded traversal: WAF sees literal "%252e", origin decodes it twice to "../..".
  "/%252e%252e/%252e%252e/etc/passwd",
  "/..%252f..%252fetc/passwd",
  // Percent-encoding a single character of "/internal" defeats literal topology-string blocking.
  "/%69nternal/admin",
  "/api/%69nternal/config",
];

async function probe(baseUrl, path) {
  const url = new URL(path, baseUrl);
  const res = await fetch(url, { redirect: "manual" });
  return { path, status: res.status };
}

async function main() {
  if (!TARGET) {
    console.log(
      "edge-guard e2e SKIP: EDGE_GUARD_TARGET not set — no public host exists yet (guarded until S9 deploys one). This is a documented skip, not a pass."
    );
    return;
  }

  console.log(`edge-guard e2e: probing ${TARGET} with ${ADVERSARIAL_PATHS.length} adversarial paths`);
  const failures = [];
  for (const path of ADVERSARIAL_PATHS) {
    const { status } = await probe(TARGET, path);
    if (status !== 404) {
      failures.push(`${path} -> HTTP ${status} (expected 404 before any rewrite)`);
    }
  }

  if (failures.length > 0) {
    console.error("edge-guard e2e FAILED:");
    for (const f of failures) console.error(`  - ${f}`);
    process.exitCode = 1;
    return;
  }
  console.log(`edge-guard e2e OK: all ${ADVERSARIAL_PATHS.length} adversarial paths 404'd`);
}

const isMain = process.argv[1] && import.meta.url === `file://${process.argv[1]}`;
if (isMain) {
  main();
}

#!/usr/bin/env node
// backend/e2e/identity.e2e.mjs — SLICE_S3_CONTRACT.md §10.3, Phase 1 (SLICE_PLAYBOOK.md scaffold gate).
//
// PLACEHOLDER ONLY. The real live E2E (§10.3's full signup→verified→delete drill: Mailpit-sourced codes,
// refresh-reuse alarm, IDOR drills, export/deletion pipeline, purge read-back, ×2 identical runs) is S3
// BUILD-phase work (SLICE_PLAYBOOK.md Phase 2) and is NOT run by the gate yet — this file exists only so
// the module has a committed e2e entry point from its first commit, mirroring the style of
// backend/e2e/substrate.e2e.mjs (self-skip until a target is set, guarded, never a lie about what ran).
//
// AT PHASE 1, identity ships ZERO HTTP endpoints (SLICE_S3_CONTRACT.md §0 DO-NOT list: "NO HTTP
// endpoints / MapPost / MapGet for identity") — AddIdentityModule only registers DI-resolvable stub
// services (backend/modules/identity/Svac.Identity/DependencyInjection/IdentityServiceCollectionExtensions.cs).
// So this placeholder asserts only that the host it is wired into still boots healthy with the module
// registered — the "empty-but-running skeleton" gate, not a feature test.
//
// Usage: IDENTITY_E2E_TARGET=http://localhost:8090 node backend/e2e/identity.e2e.mjs
//        (no IDENTITY_E2E_TARGET set -> SKIP, not a lie)

const TARGET = process.env.IDENTITY_E2E_TARGET;

async function assertHealthOk(baseUrl) {
  const res = await fetch(new URL("/health", baseUrl));
  if (res.status !== 200) {
    throw new Error(`GET /health -> HTTP ${res.status}, expected 200`);
  }
  const body = await res.json();
  if (body.status !== "healthy") {
    throw new Error(`GET /health body.status = "${body.status}", expected "healthy"`);
  }
  return body;
}

async function main() {
  if (!TARGET) {
    console.log(
      "identity e2e SKIP: IDENTITY_E2E_TARGET not set — Phase 1 placeholder only, the real signup-" +
        "verified-delete E2E (SLICE_S3_CONTRACT.md §10.3) lands in the S3 BUILD phase. This is a " +
        "documented skip, not a pass."
    );
    return;
  }

  console.log(`identity e2e (Phase 1 placeholder): probing ${TARGET}`);

  try {
    const health = await assertHealthOk(TARGET);
    console.log(`  [ok] GET /health -> ${JSON.stringify(health)} (identity module registered, zero routes mapped)`);
  } catch (err) {
    console.error("identity e2e FAILED:");
    console.error(`  - ${err.message}`);
    process.exitCode = 1;
    return;
  }

  console.log("identity e2e OK (Phase 1 placeholder): host healthy with the identity module wired in.");
}

const isMain = process.argv[1] && import.meta.url === `file://${process.argv[1]}`;
if (isMain) {
  main();
}

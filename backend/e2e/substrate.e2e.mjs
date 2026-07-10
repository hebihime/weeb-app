#!/usr/bin/env node
// backend/e2e/substrate.e2e.mjs — SLICE_S1_CONTRACT.md §10.4.
//
// Live E2E against the compose stack's Svac.PublicApi host. Self-skips (exit 0, printed note) until a
// public host answers — mirrors backend/e2e/edge-guard.mjs's own guarded pattern.
//
// SCOPE AT S1: health 200 and client-config matching i18n/locales.json are real end-to-end assertions.
// §10.4 ALSO requires "behavioral event emitted on the request and read back with watermark advanced" —
// this file drives that for real (real HTTP request, real Postgres read-back via `docker compose exec
// postgres psql`, never a mocked sink) rather than deferring it, per SLICE_PLAYBOOK's L30: "front-load
// the real end-to-end flow ... assume a done report hides a stubbed piece until the live E2E proves
// otherwise." AS OF THIS COMMIT THIS ASSERTION IS RED, ON PURPOSE: GET /v1/client-config
// (backend/public-host/Svac.PublicApi/Endpoints.cs) does not call IBehavioralStream.Emit anywhere in
// its handler, so zero rows land in core.events_behavioral for this request — a real, observable gap
// between the contract's §10.4 sign-off list and the shipped host, not a test-authoring gap. The fix
// (out of this test-author's "own the test tree only" scope) is a few lines in Endpoints.cs: inject
// IBehavioralStream + IRequestContextAccessor and Emit e.g. "client_config.fetched" before returning.
//
// The purge-completeness half of §10.4 ("purge class run leaving zero residue") is proven instead by the
// xUnit integration suite (backend/tests/Svac.Tests.Architecture/PurgeCompletenessTests.cs) against a
// real Postgres via Testcontainers — an equally real "committed as a test" satisfaction of that clause,
// since S1 ships no HTTP surface to trigger a purge run from outside the process (§0: zero consumer
// mutation endpoints, no admin host yet). That suite currently also finds real bugs in the executor
// itself — see its own doc comment.
//
// Usage: SUBSTRATE_E2E_TARGET=http://localhost:8090 node backend/e2e/substrate.e2e.mjs
//        (no SUBSTRATE_E2E_TARGET set -> SKIP, not a lie)
//        Requires a local `docker compose` with the `postgres` service from this repo's
//        docker-compose.yml up, for the behavioral read-back assertion's DB probe.

import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";
import { execFile } from "node:child_process";
import { promisify } from "node:util";

const execFileAsync = promisify(execFile);
const __dirname = dirname(fileURLToPath(import.meta.url));
const TARGET = process.env.SUBSTRATE_E2E_TARGET;

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

async function assertClientConfigMatchesLocalesFile(baseUrl, repoRoot) {
  const res = await fetch(new URL("/v1/client-config", baseUrl));
  if (res.status !== 200) {
    throw new Error(`GET /v1/client-config -> HTTP ${res.status}, expected 200`);
  }
  const body = await res.json();

  const localesPath = join(repoRoot, "i18n", "locales.json");
  const expected = JSON.parse(readFileSync(localesPath, "utf8"));

  const actualLocales = JSON.stringify([...body.locales].sort());
  const expectedLocales = JSON.stringify([...expected.locales].sort());
  if (actualLocales !== expectedLocales) {
    throw new Error(`GET /v1/client-config locales ${actualLocales} != i18n/locales.json locales ${expectedLocales}`);
  }
  if (body.defaultLocale !== expected.default) {
    throw new Error(`GET /v1/client-config defaultLocale "${body.defaultLocale}" != i18n/locales.json default "${expected.default}"`);
  }
  return body;
}

/// Counts core.events_behavioral rows recorded strictly after `sinceIso`, via a real read-only query
/// against the compose Postgres container — zero npm dependencies (BUILD.md §1: "node:test + stdlib
/// only"), so this shells `docker compose exec` + the container's own `psql`, never a `pg` package.
/// This is a READ, never a write — it never fakes state, it only observes what the real request path
/// actually persisted (or, today, did not).
async function countBehavioralEventsSince(repoRoot, sinceIso) {
  const sql = `SELECT count(*) FROM core.events_behavioral WHERE recorded_at > '${sinceIso}'::timestamptz;`;
  const { stdout } = await execFileAsync(
    "docker",
    ["compose", "exec", "-T", "postgres", "psql", "-U", "svac", "-d", "svac", "-t", "-A", "-c", sql],
    { cwd: repoRoot }
  );
  const count = Number.parseInt(stdout.trim(), 10);
  if (Number.isNaN(count)) {
    throw new Error(`could not parse events_behavioral count from psql output: ${JSON.stringify(stdout)}`);
  }
  return count;
}

async function assertBehavioralEventEmittedAndReadBack(baseUrl, repoRoot) {
  const before = new Date();
  const res = await fetch(new URL("/v1/client-config", baseUrl));
  if (res.status !== 200) {
    throw new Error(`GET /v1/client-config -> HTTP ${res.status}, expected 200 (behavioral-emit probe request)`);
  }
  await res.json(); // drain the body; the response shape itself is covered by assertClientConfigMatchesLocalesFile.

  const count = await countBehavioralEventsSince(repoRoot, before.toISOString());
  if (count < 1) {
    throw new Error(
      "GET /v1/client-config recorded ZERO rows in core.events_behavioral (SLICE_S1_CONTRACT.md §10.4: " +
        "\"behavioral event emitted on the request and read back with watermark advanced\"). " +
        "backend/public-host/Svac.PublicApi/Endpoints.cs's GET /v1/client-config handler never calls " +
        "IBehavioralStream.Emit — this is the real, currently-missing wiring, not a flaky read."
    );
  }
  return count;
}

async function main() {
  if (!TARGET) {
    console.log(
      "substrate e2e SKIP: SUBSTRATE_E2E_TARGET not set — no public host running (guarded until compose is up). This is a documented skip, not a pass."
    );
    return;
  }

  const repoRoot = join(__dirname, "..", "..");
  console.log(`substrate e2e: probing ${TARGET}`);

  const failures = [];

  try {
    const health = await assertHealthOk(TARGET);
    console.log(`  [ok] GET /health -> ${JSON.stringify(health)}`);
  } catch (err) {
    failures.push(err.message);
  }

  try {
    const clientConfig = await assertClientConfigMatchesLocalesFile(TARGET, repoRoot);
    console.log(`  [ok] GET /v1/client-config -> ${JSON.stringify(clientConfig)}`);
  } catch (err) {
    failures.push(err.message);
  }

  try {
    const count = await assertBehavioralEventEmittedAndReadBack(TARGET, repoRoot);
    console.log(`  [ok] GET /v1/client-config recorded ${count} core.events_behavioral row(s)`);
  } catch (err) {
    failures.push(err.message);
  }

  // Purge-completeness ("a purge class run leaving zero residue") is proven by the xUnit integration
  // suite backend/tests/Svac.Tests.Architecture/PurgeCompletenessTests.cs against a real Postgres via
  // Testcontainers, not here — S1 ships no HTTP surface to trigger a purge run from outside the process
  // (§0: zero consumer mutation endpoints, no admin host yet). See that file's own doc comment for what
  // it found.
  // TODO(S5+, BUILD.md §8 clause 2): fresh-boot clause (down -v -> up, migrations under advisory lock,
  // consumers registered after) — covered today by build/scripts/compose-smoke.sh; folding that
  // assertion into this file specifically is deferred until a real stream consumer exists to order
  // against the migration service.

  if (failures.length > 0) {
    console.error("substrate e2e FAILED:");
    for (const f of failures) console.error(`  - ${f}`);
    process.exitCode = 1;
    return;
  }
  console.log("substrate e2e OK: health 200, client-config matches i18n/locales.json");
}

const isMain = process.argv[1] && import.meta.url === `file://${process.argv[1]}`;
if (isMain) {
  main();
}

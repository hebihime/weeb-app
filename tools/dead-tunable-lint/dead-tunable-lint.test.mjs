// tools/dead-tunable-lint/dead-tunable-lint.test.mjs — golden-vector + red-fixture tests, both
// directions (SLICE_S5_CONTRACT.md §4/§10.2: "pending_consumer_slice lint honest both directions").
// Run: node --test tools/dead-tunable-lint/dead-tunable-lint.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";
import {
  parseLedgerSlices,
  parseDoneSlices,
  checkManifestEntries,
  findConfigManifests,
  loadManifestEntries,
} from "./dead-tunable-lint.mjs";

const __dirname = dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = join(__dirname, "..", "..");

// ---------- parseLedgerSlices ----------

test("parseLedgerSlices: extracts slice ids from table rows only, never from prose", () => {
  const doc = [
    "Some prose mentioning S99 and S7 outside any table (must NOT count).",
    "| Entity | Slice |",
    "|---|---|",
    "| Account | S3 |",
    "| Devices | S3/S4 |",
  ].join("\n");
  const slices = parseLedgerSlices(doc);
  assert.deepEqual([...slices].sort(), ["S3", "S4"]);
});

test("parseLedgerSlices: against the REAL BUILD.md, contains every RealLedgerSlices id the C# test transcribes", () => {
  const buildMd = readFileSync(join(REPO_ROOT, "BUILD.md"), "utf8");
  const slices = parseLedgerSlices(buildMd);
  const expectedSubset = ["S12", "S14", "S18", "S19", "S20", "S22", "S23", "S25", "S27", "S28", "S30", "S32", "S33", "S34", "S35"];
  for (const s of expectedSubset) {
    assert.ok(slices.has(s), `expected BUILD.md's own ledger to name "${s}" in a table row`);
  }
});

// ---------- parseDoneSlices ----------

test("parseDoneSlices: golden PASS — a header naming DONE is captured", () => {
  const doc = "### S0 retro (repo-ci-iac — DONE, 2026-07-10)\n\nsome body text\n";
  assert.deepEqual([...parseDoneSlices(doc)], ["S0"]);
});

test("parseDoneSlices: a header with NO 'DONE' in it is never counted", () => {
  const doc = "### S40 retro (future-slice — BLOCKED, missing OQ)\n\nsome body text\n";
  assert.deepEqual([...parseDoneSlices(doc)], []);
});

test("parseDoneSlices: multiple real-shaped headers, only the DONE ones counted", () => {
  const doc = [
    "### S2 retro — aiml-router (G0) — DONE (CI green on c2ad189)",
    "",
    "### S41 retro (not-started-yet — planned)",
    "",
    "### S7 retro — client-skeletons (S7i ios + S7a android) (G0) — Phase 2 build DONE (HARDENED GATE green on be07774)",
  ].join("\n");
  assert.deepEqual([...parseDoneSlices(doc)].sort(), ["S2", "S7"]);
});

test("parseDoneSlices: against the REAL SLICE_PLAYBOOK.md, S0/S1/S2/S3 are DONE", () => {
  const playbook = readFileSync(join(REPO_ROOT, "SLICE_PLAYBOOK.md"), "utf8");
  const done = parseDoneSlices(playbook);
  for (const s of ["S0", "S1", "S2", "S3"]) {
    assert.ok(done.has(s), `expected SLICE_PLAYBOOK.md's own retro to mark "${s}" DONE`);
  }
});

// SECURITY_REVIEW_S5.md S5-13 (LOW, Lens5 F3, DEFERRED): parseDoneSlices' own doc comment calls its
// /\bDONE\b/ match "deliberately loose (substring, not a fixed template)" -- loose enough that it never
// handles NEGATION. A retro header phrased "... NOT DONE yet ..." (or "not yet DONE") contains the whole
// word "DONE" and is misread as a completed slice, exactly the opposite of what the header says.
test(
  "parseDoneSlices: RED FIXTURE (deferred SECURITY_REVIEW_S5.md S5-13) -- 'NOT DONE yet' in a header's own text is misread as DONE",
  { skip: "deferred: SECURITY_REVIEW_S5.md S5-13 (parseDoneSlices' \\bDONE\\b substring match has no negation handling -- 'NOT DONE yet' / 'not yet DONE' in a retro header is misread as a completed slice) -> harden the token match" },
  () => {
    const doc = "### S41 retro (blocked -- NOT DONE yet, missing OQ)\n\nsome body text\n";
    const done = parseDoneSlices(doc);
    // Desired: a header saying the slice is NOT done must never be treated as done. Today this returns
    // ["S41"] -- the bare \bDONE\b match has no idea "NOT " precedes it.
    assert.deepEqual([...done], []);
  }
);

// ---------- checkManifestEntries: honest both directions ----------

const LEDGER = { knownSlices: new Set(["S12", "S18"]), doneSlices: new Set(["S12"]) };

test("checkManifestEntries: a row naming a real, NOT-yet-done slice PASSES", () => {
  const violations = checkManifestEntries([{ key: "verification.age_gate_challenge_threshold", consumer: "", pending_consumer_slice: "S18" }], LEDGER);
  assert.deepEqual(violations, []);
});

test("checkManifestEntries: RED FIXTURE — a row naming an invented/typo'd slice id FAILS", () => {
  const violations = checkManifestEntries([{ key: "test.typo_slice_key", consumer: "", pending_consumer_slice: "S999" }], LEDGER);
  assert.equal(violations.length, 1);
  assert.match(violations[0], /test\.typo_slice_key/);
  assert.match(violations[0], /not a real/);
});

test("checkManifestEntries: RED FIXTURE — a row naming a slice ALREADY MARKED DONE FAILS distinctly", () => {
  const violations = checkManifestEntries([{ key: "test.stale_pending_key", consumer: "", pending_consumer_slice: "S12" }], LEDGER);
  assert.equal(violations.length, 1);
  assert.match(violations[0], /test\.stale_pending_key/);
  assert.match(violations[0], /DONE/);
});

test("checkManifestEntries: RED FIXTURE — neither a real consumer nor a pending slice FAILS", () => {
  const violations = checkManifestEntries([{ key: "test.totally_dead_key", consumer: "", pending_consumer_slice: undefined }], LEDGER);
  assert.equal(violations.length, 1);
  assert.match(violations[0], /test\.totally_dead_key/);
  assert.match(violations[0], /dead tunable/);
});

test("checkManifestEntries: a row with a real, non-empty consumer and no pending slice PASSES", () => {
  const violations = checkManifestEntries([{ key: "admin.session_lifetime_hours", consumer: "cookie ticket lifetime (AddStaffAuth)", pending_consumer_slice: undefined }], LEDGER);
  assert.deepEqual(violations, []);
});

test("checkManifestEntries: a whitespace-only consumer with no pending slice still FAILS (not merely absent)", () => {
  const violations = checkManifestEntries([{ key: "test.blank_consumer_key", consumer: "   ", pending_consumer_slice: undefined }], LEDGER);
  assert.equal(violations.length, 1);
  assert.match(violations[0], /dead tunable/);
});

// ---------- manifest discovery + the real repo, green ----------

test("findConfigManifests: discovers the real S1/S5 manifests under backend/", () => {
  const paths = findConfigManifests(REPO_ROOT).map((p) => p.replace(/\\/g, "/"));
  assert.ok(paths.some((p) => p.endsWith("backend/domain-core/Svac.DomainCore/Config/manifests/domain-core.config.json")));
  assert.ok(paths.some((p) => p.endsWith("backend/admin-host/Svac.AdminHost/config/admin-host.config.json")));
  assert.ok(paths.some((p) => p.endsWith("backend/admin-host/Svac.AdminHost/config/v0-batch.config.json")));
  // bin/obj copies of the SAME files must never double-count.
  assert.ok(!paths.some((p) => p.includes("/bin/") || p.includes("/obj/")));
});

test("the REAL repo sweep (BUILD.md + SLICE_PLAYBOOK.md + every real manifest) has ZERO violations", () => {
  const realKnownSlices = parseLedgerSlices(readFileSync(join(REPO_ROOT, "BUILD.md"), "utf8"));
  const realDoneSlices = parseDoneSlices(readFileSync(join(REPO_ROOT, "SLICE_PLAYBOOK.md"), "utf8"));

  let allViolations = [];
  for (const path of findConfigManifests(REPO_ROOT)) {
    const entries = loadManifestEntries(path);
    allViolations = allViolations.concat(checkManifestEntries(entries, { knownSlices: realKnownSlices, doneSlices: realDoneSlices }).map((v) => `${path}: ${v}`));
  }

  assert.deepEqual(allViolations, []);
});

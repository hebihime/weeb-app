#!/usr/bin/env node
// tools/dead-tunable-lint/dead-tunable-lint.mjs — SLICE_S5_CONTRACT.md §4 judge synthesis §12.7 /
// PHASE_2A_SUBSTRATE.md §6 (Pass C's own deliverable: "this field + its CI wiring is shared node
// tooling under tools/... only S5 exercises it").
//
// The manifest format's `pending_consumer_slice` field (SLICE_S5_CONTRACT.md §4): a 9A config key with
// no registered consumer fails the dead-tunable lint UNLESS its manifest row names a `pending_consumer_
// slice` that is (a) a REAL BUILD.md §7 ledger slice id, AND (b) NOT a slice SLICE_PLAYBOOK.md's own
// per-slice retro already marks DONE — a slice that shipped without actually claiming the key it was
// supposed to is exactly as dead as a key that never named a slice at all. P2's `desk_rendered`
// satisfaction mode is REJECTED (§12.7): the desk renders every key regardless, so rendering can never
// be what makes a key legal.
//
// Mirrors Svac.AdminHost.Domain.Config.PendingConsumerSliceLint's pure decision function (the C# gate-
// lane proof of the SAME "consumer OR validly-pending" rule, exercised by Svac.Tests.AdminHost.
// V0BatchManifestTests.cs) — this tool adds the repo-wide sweep across EVERY committed config manifest
// PLUS the DONE-without-claiming check, which needs BUILD.md/SLICE_PLAYBOOK.md text a single C# unit
// test has no reason to parse.
//
// Usage: node tools/dead-tunable-lint/dead-tunable-lint.mjs
//        (no args -- always sweeps every backend/**/*.config.json against the real BUILD.md/
//        SLICE_PLAYBOOK.md at repo root; see the exported functions below for the fixture-driven unit
//        tests in dead-tunable-lint.test.mjs)

import { readFileSync, readdirSync, statSync, existsSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const __dirname = dirname(fileURLToPath(import.meta.url));

/**
 * Every real BUILD.md §7 ledger slice id — parsed from the doc's own table rows (lines starting with
 * "|"), never a hand-maintained duplicate list that could silently drift from the doc. A slice id
 * appearing ONLY in prose (never in a table row) is not counted — BUILD.md's ledger tables are the
 * canonical slice registry; prose mentions are commentary, not the source of truth.
 * @param {string} buildMdText
 * @returns {Set<string>}
 */
export function parseLedgerSlices(buildMdText) {
  const slices = new Set();
  for (const line of buildMdText.split("\n")) {
    if (!line.startsWith("|")) continue;
    for (const m of line.matchAll(/\bS[0-9]{1,2}\b/g)) {
      slices.add(m[0]);
    }
  }
  return slices;
}

/**
 * Every slice SLICE_PLAYBOOK.md's own "### S<N> retro (...)" per-slice header already marks DONE —
 * parsed from the header line itself (the word "DONE" appearing anywhere after "retro" on that same
 * line), mirroring the four real headers landed so far (S0/S1/S2/S3 all say "DONE" in slightly different
 * prose shapes: "(repo-ci-iac — DONE, ...)", "— aiml-router (G0) — DONE (...)", etc.) — deliberately
 * loose (substring, not a fixed template) so a future retro's own phrasing is never a silent miss.
 * @param {string} playbookText
 * @returns {Set<string>}
 */
export function parseDoneSlices(playbookText) {
  const done = new Set();
  const headerRe = /^### (S[0-9]{1,2}) retro\b(.*)$/gm;
  let m;
  while ((m = headerRe.exec(playbookText)) !== null) {
    const [, slice, rest] = m;
    if (/\bDONE\b/.test(rest)) {
      done.add(slice);
    }
  }
  return done;
}

/**
 * The decision function (mirrors Svac.AdminHost.Domain.Config.PendingConsumerSliceLint.Validate exactly,
 * PLUS the DONE-without-claiming teeth): a key passes iff EITHER it names no pending slice and carries a
 * non-empty `consumer`, OR it names a pending slice that is a real, NOT-YET-DONE ledger slice. A pending
 * slice that is real but ALREADY DONE fails distinctly from an invented/typo'd one, so CI output always
 * names the actual defect.
 * @param {Array<{key:string, consumer?:string, pending_consumer_slice?:string}>} entries
 * @param {{knownSlices: Set<string>, doneSlices: Set<string>}} ledger
 * @returns {string[]} violations
 */
export function checkManifestEntries(entries, { knownSlices, doneSlices }) {
  const violations = [];
  for (const entry of entries) {
    const pending = entry.pending_consumer_slice;
    if (pending) {
      if (!knownSlices.has(pending)) {
        violations.push(
          `config key "${entry.key}" names pending_consumer_slice "${pending}", which is not a real ` +
            "BUILD.md §7 ledger slice — a typo'd or invented slice id is not a free pass."
        );
        continue;
      }
      if (doneSlices.has(pending)) {
        violations.push(
          `config key "${entry.key}" names pending_consumer_slice "${pending}", but SLICE_PLAYBOOK.md ` +
            `already marks ${pending} DONE with no registered consumer claiming the key — clear ` +
            `pending_consumer_slice and give it a real "consumer" now that ${pending} has landed.`
        );
      }
      continue; // a validly-pending, not-yet-done key needs no real consumer YET.
    }

    if (!entry.consumer || !entry.consumer.trim()) {
      violations.push(
        `config key "${entry.key}" has neither a real consumer nor a pending_consumer_slice naming a ` +
          "real BUILD.md §7 ledger slice — a dead tunable."
      );
    }
  }
  return violations;
}

// ---------- manifest file discovery ----------

const SKIP = new Set(["bin", "obj", "node_modules", ".git"]);

function walk(dir, pick, acc = []) {
  let entries;
  try {
    entries = readdirSync(dir);
  } catch {
    return acc;
  }
  for (const name of entries) {
    if (SKIP.has(name)) continue;
    const full = join(dir, name);
    let s;
    try {
      s = statSync(full);
    } catch {
      continue;
    }
    if (s.isDirectory()) {
      walk(full, pick, acc);
    } else if (pick(name, full)) {
      acc.push(full);
    }
  }
  return acc;
}

/** Every real committed 9A config manifest under backend/ — the shared, module-agnostic sweep target
 * (S1's domain-core.config.json, S2's aiml-router.config.json, S3's identity.config.json, S5's
 * admin-host.config.json + v0-batch.config.json, and every future module's manifest, zero edits to this
 * tool required when one lands). @param {string} repoRoot @returns {string[]} */
export function findConfigManifests(repoRoot) {
  return walk(join(repoRoot, "backend"), (name) => name.endsWith(".config.json")).sort();
}

/** @param {string} path @returns {Array<object>} */
export function loadManifestEntries(path) {
  const json = JSON.parse(readFileSync(path, "utf8"));
  return Array.isArray(json.entries) ? json.entries : [];
}

async function main() {
  const repoRoot = join(__dirname, "..", "..");

  const buildMdPath = join(repoRoot, "BUILD.md");
  const playbookPath = join(repoRoot, "SLICE_PLAYBOOK.md");
  if (!existsSync(buildMdPath) || !existsSync(playbookPath)) {
    console.log("dead-tunable-lint SKIP: BUILD.md or SLICE_PLAYBOOK.md not found (guarded — should never happen post-S0)");
    return;
  }

  const knownSlices = parseLedgerSlices(readFileSync(buildMdPath, "utf8"));
  const doneSlices = parseDoneSlices(readFileSync(playbookPath, "utf8"));
  const manifestPaths = findConfigManifests(repoRoot);

  if (manifestPaths.length === 0) {
    console.log("dead-tunable-lint SKIP: no backend/**/*.config.json manifests exist yet");
    return;
  }

  let allViolations = [];
  for (const path of manifestPaths) {
    const entries = loadManifestEntries(path);
    const violations = checkManifestEntries(entries, { knownSlices, doneSlices }).map((v) => `${path}: ${v}`);
    allViolations = allViolations.concat(violations);
  }

  if (allViolations.length > 0) {
    console.error("dead-tunable-lint BLOCKED:");
    for (const v of allViolations) console.error(`  - ${v}`);
    process.exitCode = 1;
    return;
  }

  console.log(
    `dead-tunable-lint OK: ${manifestPaths.length} manifest file(s) checked, zero dead/invalid-pending ` +
      `tunables (${knownSlices.size} known ledger slices; DONE: ${[...doneSlices].sort().join(", ") || "none"})`
  );
}

const isMain = process.argv[1] && import.meta.url === `file://${process.argv[1]}`;
if (isMain) {
  main();
}

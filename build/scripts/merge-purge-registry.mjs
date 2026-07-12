#!/usr/bin/env node
// build/scripts/merge-purge-registry.mjs — SLICE_S5_CONTRACT.md §6 drift-gate support.
//
// backend/domain-core/purge-registry.json is ONE committed, repo-wide union of every host's 13A
// registrations. No single .NET binary may assemble that union directly: Svac.PublicApi (core+identity)
// and Svac.AdminHost (admin) are two DIFFERENT deploy units, and the admin trust-boundary rule (SLICE_
// S5_CONTRACT.md §0 law c) forbids Svac.PublicApi from ever referencing Svac.AdminHost* — so this
// merge happens OUTSIDE either process, in the deterministic-space script that already owns the
// drift-gate shape (build/scripts/emit-purge-registry.sh).
//
// Deterministic, no LLM. Concatenates N JSON-array fragment files IN THE ORDER GIVEN (never re-sorted —
// fragment order is fixed by the caller's argument order, so the merged output is stable across runs
// without churning the committed file's existing row order every time a new fragment is added) and
// boot-refuses (throws) on a (storeKey, purgeClass) pair claimed by more than one fragment — the SAME
// duplicate-ownership invariant Svac.DomainCore.Purge.PurgeRegistry's own constructor enforces inside a
// running process, re-proven here at the file level.
//
// Usage: node build/scripts/merge-purge-registry.mjs <fragment.json> [...fragment.json] <output.json>

import { readFileSync, writeFileSync } from "node:fs";

/** @param {Array<Array<{storeKey:string, purgeClass:string, verb:string, reason:string}>>} fragments */
export function mergeRegistries(fragments) {
  const seen = new Map(); // "storeKey/purgeClass" -> which fragment index claimed it first
  const merged = [];
  fragments.forEach((entries, fragmentIndex) => {
    for (const entry of entries) {
      const cellKey = `${entry.storeKey}/${entry.purgeClass}`;
      if (seen.has(cellKey)) {
        throw new Error(
          `merge-purge-registry: (${entry.storeKey}, ${entry.purgeClass}) is registered by more than one fragment ` +
            `(fragment #${seen.get(cellKey)} and #${fragmentIndex}) — a store/class cell must have exactly one registrant.`
        );
      }
      seen.set(cellKey, fragmentIndex);
      merged.push(entry);
    }
  });
  return merged;
}

function main() {
  const argv = process.argv.slice(2);
  if (argv.length < 2) {
    console.error("usage: node merge-purge-registry.mjs <fragment.json> [...fragment.json] <output.json>");
    process.exitCode = 1;
    return;
  }
  const outputPath = argv[argv.length - 1];
  const fragmentPaths = argv.slice(0, -1);
  const fragments = fragmentPaths.map((p) => JSON.parse(readFileSync(p, "utf8")));
  const merged = mergeRegistries(fragments);
  writeFileSync(outputPath, JSON.stringify(merged, null, 2) + "\n");
  console.log(`merge-purge-registry: wrote ${outputPath} (${merged.length} entries from ${fragments.length} fragment(s))`);
}

const isMain = process.argv[1] && import.meta.url === `file://${process.argv[1]}`;
if (isMain) {
  main();
}

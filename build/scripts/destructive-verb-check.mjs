#!/usr/bin/env node
// build/scripts/destructive-verb-check.mjs — SLICE_S0_CONTRACT.md §2(c), EF-migration CI gate.
//
// A migration file containing DROP TABLE / DROP COLUMN / TRUNCATE fails the gate unless it also
// carries an explicit "-- destructive: <reason>" marker comment. Silent column drops are how
// region/lawful-basis and consent columns die in refactors (P3's scar) — this is the tripwire.
//
// Deterministic text scan, no LLM. Called by build/scripts/ef-gate.sh (step c) once EF migrations
// exist (S1+); has its own golden-vector tests now so the gate arrives pre-tested.
//
// Usage: node build/scripts/destructive-verb-check.mjs <migration-file-or-dir> [...]

import { readFileSync, statSync, readdirSync } from "node:fs";
import { join } from "node:path";

// Raw-SQL verbs (migrationBuilder.Sql("DROP TABLE ...")) AND the idiomatic EF Core fluent migration API
// (migrationBuilder.DropColumn/.DropTable/.DropSchema(...)) — SECURITY_REVIEW_S0.md PII/residency F2.
// .cs migration files overwhelmingly use the fluent API, not raw SQL; missing it let an unmarked drop of
// region/lawful_basis/consent columns (exactly P3's scar) sail through the tripwire.
const DESTRUCTIVE_VERB_RE = /\b(DROP\s+TABLE|DROP\s+COLUMN|TRUNCATE|DropColumn|DropTable|DropSchema)\b/i;
const MARKER_RE = /--\s*destructive:\s*\S.+/i;

const VERB_DISPLAY_NAMES = {
  DROPTABLE: "DROP TABLE",
  DROPCOLUMN: "DROP COLUMN",
  DROPSCHEMA: "DROP SCHEMA",
  TRUNCATE: "TRUNCATE",
};

function normalizeVerb(raw) {
  const compact = raw.toUpperCase().replace(/\s+/g, "");
  return VERB_DISPLAY_NAMES[compact] ?? raw.toUpperCase();
}

/** @param {string} content @returns {{destructive: boolean, hasMarker: boolean, matchedVerbs: string[]}} */
export function analyzeMigration(content) {
  const matchedVerbs = [];
  const re = new RegExp(DESTRUCTIVE_VERB_RE.source, "gi");
  let m;
  while ((m = re.exec(content)) !== null) matchedVerbs.push(normalizeVerb(m[1]));
  return {
    destructive: matchedVerbs.length > 0,
    hasMarker: MARKER_RE.test(content),
    matchedVerbs,
  };
}

/** @param {Array<{path:string, content:string}>} files @returns {string[]} violations */
export function checkFiles(files) {
  const violations = [];
  for (const { path, content } of files) {
    const { destructive, hasMarker, matchedVerbs } = analyzeMigration(content);
    if (destructive && !hasMarker) {
      violations.push(
        `${path}: contains destructive verb(s) [${[...new Set(matchedVerbs)].join(", ")}] with no "-- destructive: <reason>" marker`
      );
    }
  }
  return violations;
}

function collectFiles(target) {
  const stat = statSync(target);
  if (stat.isDirectory()) {
    return readdirSync(target)
      .filter((f) => f.endsWith(".sql") || f.endsWith(".cs"))
      .map((f) => join(target, f));
  }
  return [target];
}

async function main() {
  const args = process.argv.slice(2);
  if (args.length === 0) {
    console.log("destructive-verb-check SKIP: no migration path given (guarded — no EF migrations exist until S1)");
    return;
  }
  const files = args.flatMap(collectFiles).map((path) => ({ path, content: readFileSync(path, "utf8") }));
  const violations = checkFiles(files);
  if (violations.length > 0) {
    console.error("destructive-verb-check BLOCKED:");
    for (const v of violations) console.error(`  - ${v}`);
    process.exitCode = 1;
    return;
  }
  console.log(`destructive-verb-check OK: ${files.length} file(s) scanned, zero unmarked destructive verbs`);
}

const isMain = process.argv[1] && import.meta.url === `file://${process.argv[1]}`;
if (isMain) {
  main();
}

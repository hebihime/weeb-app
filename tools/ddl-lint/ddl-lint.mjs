#!/usr/bin/env node
// tools/ddl-lint/ddl-lint.mjs — SLICE_S0_CONTRACT.md §2, L21 residency lint.
//
// Operates on generated SQL DDL (e.g. `dotnet ef migrations script` output, or raw .sql migration
// fixtures). A CREATE TABLE whose name matches a glob in pii-patterns.json must declare every column in
// required_columns, unless the table name is in the allowlist with a stated reason.
//
// Deterministic, no LLM: pure regex/string parsing against golden-vector fixtures.
//
// Usage: node tools/ddl-lint/ddl-lint.mjs <file.sql> [<file.sql> ...]
//        cat migration.sql | node tools/ddl-lint/ddl-lint.mjs -

import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const __dirname = dirname(fileURLToPath(import.meta.url));

/** Convert a glob like "*consent*" to a case-insensitive RegExp. */
function globToRegExp(glob) {
  const escaped = glob.replace(/[.+^${}()|[\]\\]/g, "\\$&").replace(/\*/g, ".*");
  return new RegExp(`^${escaped}$`, "i");
}

/**
 * Extract CREATE TABLE statements from SQL text: { tableName, columnNames }[].
 * Handles quoted ("Foo") and unquoted identifiers, single-line and multi-line column blocks.
 */
export function parseCreateTables(sql) {
  const tables = [];
  const stmtRe = /CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?"?([A-Za-z_][A-Za-z0-9_]*)"?\s*\(([\s\S]*?)\)\s*;/gi;
  let m;
  while ((m = stmtRe.exec(sql)) !== null) {
    const tableName = m[1];
    const body = m[2];
    const columnNames = [];
    // Split top-level commas (no nested parens expected in simple column defs; CONSTRAINT lines skipped).
    const lines = splitTopLevel(body);
    for (const line of lines) {
      const trimmed = line.trim();
      if (/^(CONSTRAINT|PRIMARY\s+KEY|FOREIGN\s+KEY|UNIQUE|CHECK)\b/i.test(trimmed)) continue;
      const colMatch = trimmed.match(/^"?([A-Za-z_][A-Za-z0-9_]*)"?\s+/);
      if (colMatch) columnNames.push(colMatch[1]);
    }
    tables.push({ tableName, columnNames });
  }
  return tables;
}

function splitTopLevel(body) {
  const parts = [];
  let depth = 0;
  let current = "";
  for (const ch of body) {
    if (ch === "(") depth++;
    if (ch === ")") depth--;
    if (ch === "," && depth === 0) {
      parts.push(current);
      current = "";
    } else {
      current += ch;
    }
  }
  if (current.trim()) parts.push(current);
  return parts;
}

/**
 * @param {ReturnType<typeof parseCreateTables>} tables
 * @param {{patterns: string[], required_columns: string[], allowlist: Array<{table: string, reason: string}>}} config
 * @returns {string[]} violations (empty = pass)
 */
export function checkResidencyColumns(tables, config) {
  const violations = [];
  const regexes = config.patterns.map(globToRegExp);
  const allowSet = new Map((config.allowlist ?? []).map((a) => [a.table.toLowerCase(), a.reason]));

  for (const { tableName, columnNames } of tables) {
    const matches = regexes.some((re) => re.test(tableName));
    if (!matches) continue;

    if (allowSet.has(tableName.toLowerCase())) {
      const reason = allowSet.get(tableName.toLowerCase());
      if (!reason || !reason.trim()) {
        violations.push(`table "${tableName}" is allowlisted with no stated reason — reason is required`);
      }
      continue;
    }

    const lowerCols = columnNames.map((c) => c.toLowerCase());
    for (const required of config.required_columns) {
      if (!lowerCols.includes(required.toLowerCase())) {
        violations.push(
          `table "${tableName}" matches a PII pattern but is missing required column "${required}" (allowlist it with a reason if this is intentional)`
        );
      }
    }
  }
  return violations;
}

export function loadConfig(path = join(__dirname, "pii-patterns.json")) {
  return JSON.parse(readFileSync(path, "utf8"));
}

async function main() {
  const args = process.argv.slice(2);
  if (args.length === 0) {
    console.error("usage: ddl-lint.mjs <file.sql|-> [...]");
    process.exitCode = 2;
    return;
  }

  const config = loadConfig();
  let allViolations = [];

  for (const arg of args) {
    const sql = arg === "-" ? readFileSync(0, "utf8") : readFileSync(arg, "utf8");
    const tables = parseCreateTables(sql);
    const violations = checkResidencyColumns(tables, config).map((v) => `${arg}: ${v}`);
    allViolations = allViolations.concat(violations);
  }

  if (allViolations.length > 0) {
    console.error("ddl-lint BLOCKED:");
    for (const v of allViolations) console.error(`  - ${v}`);
    process.exitCode = 1;
    return;
  }
  console.log(`ddl-lint OK: ${args.length} file(s) checked, zero residency violations`);
}

const isMain = process.argv[1] && import.meta.url === `file://${process.argv[1]}`;
if (isMain) {
  main();
}

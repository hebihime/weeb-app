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
 * Extract CREATE TABLE statements from SQL text: { tableName, qualifiedName, columnNames }[].
 * Handles quoted ("Foo") and unquoted identifiers, single-line and multi-line column blocks, and an
 * OPTIONAL schema qualifier (schema.table or "schema"."table") — the shape `dotnet ef migrations
 * script` emits for a modular monolith with schema-per-module (CLAUDE.md architecture; SECURITY_
 * REVIEW_S0.md PII/residency F1 / minor F4; SLICE_S3_CONTRACT.md item 1, the second module-owned
 * schema). `tableName` is the bare identifier (unchanged, so every existing bare-name pattern like
 * "*consent*"/"*events_*" keeps matching exactly as before — zero behavior change for schema `core`,
 * whose EF-emitted DDL already carries its own "core." qualifier today). `qualifiedName` is
 * `schema.table` when a schema qualifier is present, else identical to `tableName` — this is what makes
 * a SCHEMA-ANCHORED pattern like "identity.*" possible: a glob that only ever matches tables that live
 * in schema `identity`, never a same-named bare table anywhere else, distinct from the older
 * substring-anywhere patterns ("*identity*") which key on English vocabulary rather than schema.
 */
export function parseCreateTables(sql) {
  const tables = [];
  const stmtRe = /CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?(?:"?([A-Za-z_][A-Za-z0-9_]*)"?\.)?"?([A-Za-z_][A-Za-z0-9_]*)"?\s*\(([\s\S]*?)\)\s*;/gi;
  let m;
  while ((m = stmtRe.exec(sql)) !== null) {
    const schema = m[1];
    const tableName = m[2];
    const qualifiedName = schema ? `${schema}.${tableName}` : tableName;
    const body = m[3];
    const columnNames = [];
    // Split top-level commas (no nested parens expected in simple column defs; CONSTRAINT lines skipped).
    const lines = splitTopLevel(body);
    for (const line of lines) {
      const trimmed = line.trim();
      if (/^(CONSTRAINT|PRIMARY\s+KEY|FOREIGN\s+KEY|UNIQUE|CHECK)\b/i.test(trimmed)) continue;
      const colMatch = trimmed.match(/^"?([A-Za-z_][A-Za-z0-9_]*)"?\s+/);
      if (colMatch) columnNames.push(colMatch[1]);
    }
    tables.push({ tableName, qualifiedName, columnNames });
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

  for (const { tableName, qualifiedName, columnNames } of tables) {
    // qualifiedName is undefined for pre-existing test fixtures that construct { tableName, columnNames }
    // directly (never through parseCreateTables) — falls back to tableName, a no-op for those callers.
    const qn = (qualifiedName ?? tableName).toLowerCase();
    const matches = regexes.some((re) => re.test(tableName) || re.test(qn));
    if (!matches) continue;

    // Matches either the bare name or the schema-qualified name — a table can be allowlisted either way.
    const allowKey = allowSet.has(tableName.toLowerCase()) ? tableName.toLowerCase() : qn;
    if (allowSet.has(allowKey)) {
      const reason = allowSet.get(allowKey);
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

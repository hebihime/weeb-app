// tools/ddl-lint/ddl-lint.pii-residency.adversary.test.mjs
// LENS: PII/residency + special-category. Adversary suite — these tests assert the L21 residency gate
// (SLICE_S0_CONTRACT.md §2) does what the contract claims. They FAIL against the current parser,
// demonstrating a residency-column bypass. Run: node --test tools/ddl-lint/ddl-lint.pii-residency.adversary.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import { parseCreateTables, checkResidencyColumns, loadConfig } from "./ddl-lint.mjs";

const CONFIG = loadConfig();

// BREAK 1: schema-qualified table names slip past parseCreateTables entirely.
// The backend is a modular monolith (1A) whose EF Core mapping puts each module's tables in its own
// Postgres schema (profiles.*, consent.*, integrity.*). `dotnet ef migrations script` therefore emits
// `CREATE TABLE profiles.user_consent (...)` / `CREATE TABLE "profiles"."UserIdentity" (...)`.
// The regex in parseCreateTables anchors on a SINGLE identifier and never matches the schema.table
// form, so a PII/special-category table with NO region/lawful_basis columns produces zero tables and
// zero violations. The residency gate is silent exactly where L21 must bite.
test("[BREAK] schema-qualified PII table missing region/lawful_basis is caught", () => {
  const sql = 'CREATE TABLE profiles.user_consent (id uuid NOT NULL, body text);';
  const tables = parseCreateTables(sql);
  assert.equal(tables.length, 1, "schema-qualified CREATE TABLE must be parsed");
  assert.equal(tables[0].tableName, "user_consent");
  const violations = checkResidencyColumns(tables, CONFIG);
  assert.ok(
    violations.length >= 1,
    "profiles.user_consent has no region/lawful_basis — L21 must flag it, but the parser dropped the table"
  );
});

test("[BREAK] quoted schema-qualified special-category identity table is caught", () => {
  const sql = 'CREATE TABLE "profiles"."UserIdentity" ("Id" uuid NOT NULL, "doc_number" text);';
  const tables = parseCreateTables(sql);
  assert.equal(tables.length, 1, 'quoted "schema"."table" must be parsed');
  const violations = checkResidencyColumns(tables, CONFIG);
  assert.ok(violations.length >= 1, "UserIdentity has no region/lawful_basis — L21 must flag it");
});

test("[BREAK] schema-qualified IF NOT EXISTS location table is caught", () => {
  const sql = 'CREATE TABLE IF NOT EXISTS geo.location_pings (id uuid NOT NULL);';
  const tables = parseCreateTables(sql);
  assert.equal(tables.length, 1);
  const violations = checkResidencyColumns(tables, CONFIG);
  assert.ok(violations.length >= 1, "geo.location_pings has no region/lawful_basis — L21 must flag it");
});

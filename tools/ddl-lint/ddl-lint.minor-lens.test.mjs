// tools/ddl-lint/ddl-lint.minor-lens.test.mjs
// LENS: minor-protection (L21 region-first PII residency). Adversarial suite against SLICE_S0_CONTRACT.md §2.
//
// The residency lint exists so consent / verification / location tables (all minor-data-bearing) can
// never land without region + lawful_basis. These tests feed it the DDL EF Core actually emits and the
// tables that actually hold age data, and prove the gate goes dark. They assert CORRECT behavior; they
// FAIL against the current linter.

import { test } from "node:test";
import assert from "node:assert/strict";
import { parseCreateTables, checkResidencyColumns, loadConfig } from "./ddl-lint.mjs";

test("L21: schema-qualified CREATE TABLE must still be residency-checked (BREAK: parser ignores schema-qualified names)", () => {
  // `dotnet ef migrations script` for a module in schema `verification` emits exactly this shape.
  // The backend is a modular monolith with schema-per-module (CLAUDE.md), so this is the NORMAL DDL.
  const efScript = `CREATE TABLE verification."ConsentLedger" (
    "Id" uuid NOT NULL,
    "SubjectId" uuid NOT NULL,
    "GrantedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_ConsentLedger" PRIMARY KEY ("Id")
);`;
  const config = loadConfig();
  const tables = parseCreateTables(efScript);
  assert.ok(
    tables.length === 1,
    "parseCreateTables must recognize a schema-qualified table; it currently matches zero, so the whole residency gate is silently a no-op on real EF DDL"
  );
  const violations = checkResidencyColumns(tables, config);
  assert.ok(
    violations.length >= 1,
    "a consent table with no region/lawful_basis must fail residency even when schema-qualified"
  );
});

test("L21: age-attestation tables must be residency-checked (BREAK: age/minor patterns absent from pii-patterns.json)", () => {
  // Age attestation is THE central minor-protection datum. A birthdate/age table with no region +
  // lawful_basis is precisely the residency hole L21 closes, but no seeded pattern matches it.
  const sql = `CREATE TABLE age_attestations (
    id uuid PRIMARY KEY,
    user_id uuid,
    birthdate date,
    attested_at timestamptz
);`;
  const config = loadConfig();
  const tables = parseCreateTables(sql);
  const violations = checkResidencyColumns(tables, config);
  assert.ok(
    violations.length >= 1,
    "an age-attestation table lacking region/lawful_basis must fail residency; the pattern set omits age/birthdate/minor tables entirely"
  );
});

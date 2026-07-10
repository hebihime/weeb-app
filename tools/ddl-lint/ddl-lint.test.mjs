// tools/ddl-lint/ddl-lint.test.mjs — golden-vector tests. Run: node --test tools/ddl-lint/ddl-lint.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import { parseCreateTables, checkResidencyColumns, loadConfig } from "./ddl-lint.mjs";

const CONFIG = {
  patterns: ["*consent*", "*profile*", "*identity*", "*location*", "*verification*"],
  required_columns: ["region", "lawful_basis"],
  allowlist: [],
};

test("parseCreateTables: single simple table, quoted identifiers (Npgsql EF style)", () => {
  const sql = `
    CREATE TABLE "ConsentLedger" (
        "Id" uuid NOT NULL,
        "region" text NOT NULL,
        "lawful_basis" text NOT NULL,
        CONSTRAINT "PK_ConsentLedger" PRIMARY KEY ("Id")
    );
  `;
  const tables = parseCreateTables(sql);
  assert.equal(tables.length, 1);
  assert.equal(tables[0].tableName, "ConsentLedger");
  assert.deepEqual(tables[0].columnNames, ["Id", "region", "lawful_basis"]);
});

test("parseCreateTables: multiple statements in one file", () => {
  const sql = `
    CREATE TABLE "Widgets" ( "Id" uuid NOT NULL );
    CREATE TABLE "Profiles" ( "Id" uuid NOT NULL, "region" text NOT NULL );
  `;
  const tables = parseCreateTables(sql);
  assert.equal(tables.length, 2);
  assert.deepEqual(tables.map((t) => t.tableName), ["Widgets", "Profiles"]);
});

test("parseCreateTables: unquoted identifiers also parse", () => {
  const sql = `CREATE TABLE identity_records ( id uuid NOT NULL, region text NOT NULL, lawful_basis text NOT NULL );`;
  const tables = parseCreateTables(sql);
  assert.equal(tables[0].tableName, "identity_records");
  assert.deepEqual(tables[0].columnNames, ["id", "region", "lawful_basis"]);
});

test("checkResidencyColumns: golden PASS — consent table with both required columns", () => {
  const tables = [{ tableName: "ConsentLedger", columnNames: ["Id", "region", "lawful_basis"] }];
  assert.deepEqual(checkResidencyColumns(tables, CONFIG), []);
});

test("checkResidencyColumns: golden FAIL — profile table missing lawful_basis", () => {
  const tables = [{ tableName: "UserProfile", columnNames: ["Id", "region", "display_name"] }];
  const violations = checkResidencyColumns(tables, CONFIG);
  assert.equal(violations.length, 1);
  assert.match(violations[0], /missing required column "lawful_basis"/);
});

test("checkResidencyColumns: golden FAIL — verification table missing both columns", () => {
  const tables = [{ tableName: "VerificationAttempt", columnNames: ["Id", "status"] }];
  const violations = checkResidencyColumns(tables, CONFIG);
  assert.equal(violations.length, 2);
});

test("checkResidencyColumns: non-PII table is untouched even with no region/lawful_basis", () => {
  const tables = [{ tableName: "Widgets", columnNames: ["Id", "Sku"] }];
  assert.deepEqual(checkResidencyColumns(tables, CONFIG), []);
});

test("checkResidencyColumns: allowlisted table with reason is skipped", () => {
  const tables = [{ tableName: "LegacyProfileCache", columnNames: ["Id"] }];
  const config = { ...CONFIG, allowlist: [{ table: "LegacyProfileCache", reason: "ephemeral cache, no PII persisted, ADR-042" }] };
  assert.deepEqual(checkResidencyColumns(tables, config), []);
});

test("checkResidencyColumns: allowlist entry with empty reason still fails (reason mandatory)", () => {
  const tables = [{ tableName: "LegacyProfileCache", columnNames: ["Id"] }];
  const config = { ...CONFIG, allowlist: [{ table: "LegacyProfileCache", reason: "" }] };
  const violations = checkResidencyColumns(tables, config);
  assert.equal(violations.length, 1);
  assert.match(violations[0], /reason is required/);
});

test("checkResidencyColumns: pattern matching is case-insensitive on table name", () => {
  const tables = [{ tableName: "CONSENT_LOG", columnNames: [] }];
  const violations = checkResidencyColumns(tables, CONFIG);
  assert.equal(violations.length, 2);
});

test("loadConfig: seeded pii-patterns.json loads and matches the contract's patterns (five original + three minor-protection, SECURITY_REVIEW_S0.md minor F5, +two S1 event/ledger patterns, SLICE_S1_CONTRACT.md §2)", () => {
  const config = loadConfig();
  assert.deepEqual(config.patterns, [
    "*consent*", "*profile*", "*identity*", "*location*", "*verification*", "*age_attestation*", "*birthdate*", "*minor*",
    "*events_*", "*ledger*",
  ]);
  assert.deepEqual(config.required_columns, ["region", "lawful_basis"]);
  assert.deepEqual(config.allowlist, [
    {
      table: "ledger_balances",
      reason: "SLICE_S1_CONTRACT.md §2: a rebuildable summation PROJECTION over events_ledger + ledger_entries (both of which DO carry region/lawful_basis on every row); the balance row itself is a derived aggregate, not an independently-personal-data-bearing row, so it is registered here with a reason rather than silently exempt.",
    },
  ]);
});

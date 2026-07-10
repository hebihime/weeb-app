// build/scripts/destructive-verb-check.test.mjs — golden-vector tests.
// Run: node --test build/scripts/destructive-verb-check.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import { analyzeMigration, checkFiles } from "./destructive-verb-check.mjs";

test("analyzeMigration: golden — DROP TABLE with no marker is destructive and unmarked", () => {
  const result = analyzeMigration('migrationBuilder.Sql("DROP TABLE \\"Widgets\\";");');
  assert.equal(result.destructive, true);
  assert.equal(result.hasMarker, false);
  assert.deepEqual(result.matchedVerbs, ["DROP TABLE"]);
});

test("analyzeMigration: golden — DROP COLUMN with a marker comment is destructive but marked", () => {
  const content = `
    -- destructive: legacy_email column unused since S3, verified zero reads in prod telemetry
    migrationBuilder.DropColumn(name: "legacy_email", table: "Users");
  `;
  const result = analyzeMigration(content);
  assert.equal(result.hasMarker, true);
});

test("analyzeMigration: TRUNCATE detected", () => {
  const result = analyzeMigration("TRUNCATE consent_ledger;");
  assert.deepEqual(result.matchedVerbs, ["TRUNCATE"]);
});

test("analyzeMigration: golden — additive migration (CREATE TABLE, AddColumn) is not destructive", () => {
  const result = analyzeMigration('migrationBuilder.CreateTable(name: "Profiles", ...); migrationBuilder.AddColumn(name: "bio", table: "Profiles");');
  assert.equal(result.destructive, false);
});

test("checkFiles: golden FAIL — unmarked DROP TABLE blocks the gate", () => {
  const files = [{ path: "20260710_DropWidgets.cs", content: 'migrationBuilder.Sql("DROP TABLE \\"Widgets\\";");' }];
  const violations = checkFiles(files);
  assert.equal(violations.length, 1);
  assert.match(violations[0], /DROP TABLE/);
  assert.match(violations[0], /no "-- destructive: <reason>" marker/);
});

test("checkFiles: golden PASS — marked destructive migration passes", () => {
  const files = [
    {
      path: "20260710_DropLegacyEmail.cs",
      content: '// -- destructive: legacy_email dead since S3, zero prod reads (ADR-019)\nmigrationBuilder.DropColumn(name: "legacy_email", table: "Users");',
    },
  ];
  assert.deepEqual(checkFiles(files), []);
});

test("checkFiles: golden PASS — purely additive migration passes with no marker needed", () => {
  const files = [{ path: "20260710_AddBio.cs", content: 'migrationBuilder.AddColumn(name: "bio", table: "Profiles");' }];
  assert.deepEqual(checkFiles(files), []);
});

test("checkFiles: multiple destructive verbs in one file report a deduped, combined violation", () => {
  const files = [
    { path: "20260710_Multi.cs", content: 'migrationBuilder.Sql("DROP TABLE \\"A\\"; DROP TABLE \\"B\\"; TRUNCATE \\"C\\";");' },
  ];
  const violations = checkFiles(files);
  assert.equal(violations.length, 1);
  assert.match(violations[0], /DROP TABLE, TRUNCATE/);
});

test("checkFiles: marker present but unrelated to a later file in the batch — each file judged independently", () => {
  const files = [
    { path: "clean.cs", content: 'migrationBuilder.AddColumn(name: "x", table: "Y");' },
    { path: "dirty.cs", content: 'migrationBuilder.Sql("DROP TABLE \\"Z\\";");' },
  ];
  const violations = checkFiles(files);
  assert.equal(violations.length, 1);
  assert.match(violations[0], /dirty\.cs/);
});

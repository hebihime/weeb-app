// build/scripts/destructive-verb-check.pii-residency.adversary.test.mjs
// LENS: PII/residency + special-category. Adversary suite for the §2(c) destructive-verb tripwire, whose
// stated purpose is: "silent column drops are how region/lawful-basis and consent columns die in
// refactors (P3's scar)". These tests FAIL against the current scanner, showing the tripwire misses the
// IDIOMATIC EF Core migration API and lets an unmarked drop of region/lawful_basis through.
// Run: node --test build/scripts/destructive-verb-check.pii-residency.adversary.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import { analyzeMigration, checkFiles } from "./destructive-verb-check.mjs";

// BREAK 2: the scanner only matches raw SQL verbs (DROP TABLE / DROP COLUMN / TRUNCATE). Real EF Core
// migrations are C# and drop schema via the fluent API: migrationBuilder.DropColumn(...) /
// .DropTable(...). ef-gate.sh scans *.cs migration files, and the existing golden tests only cover
// migrationBuilder.Sql("DROP TABLE ...") — never the idiomatic DropColumn/DropTable. So the exact P3
// scar (region/lawful_basis silently dropped in a refactor) sails through unmarked.
test("[BREAK] EF Core DropColumn(region) is flagged as destructive", () => {
  const cs = 'migrationBuilder.DropColumn(name: "region", table: "user_profiles");';
  const result = analyzeMigration(cs);
  assert.equal(result.destructive, true, "DropColumn must count as a destructive verb");
});

test("[BREAK] unmarked EF DropColumn of lawful_basis blocks the gate", () => {
  const cs = 'migrationBuilder.DropColumn(name: "lawful_basis", table: "consent_ledger");';
  const violations = checkFiles([{ path: "20260801_DropLawfulBasis.cs", content: cs }]);
  assert.equal(violations.length, 1, "an unmarked DropColumn of a residency column must block the gate");
});

test("[BREAK] unmarked EF DropTable of a consent store blocks the gate", () => {
  const cs = 'migrationBuilder.DropTable(name: "consent_ledger");';
  const result = analyzeMigration(cs);
  assert.equal(result.destructive, true, "DropTable must count as a destructive verb");
  const violations = checkFiles([{ path: "20260801_DropConsent.cs", content: cs }]);
  assert.equal(violations.length, 1, "an unmarked DropTable of a consent store must block the gate");
});

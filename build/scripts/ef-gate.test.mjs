// build/scripts/ef-gate.test.mjs — fixture tests for the EF-migration CI gate's guarded-activation
// and clause-ordering logic (SLICE_S0_CONTRACT.md §2, §8: "ef-gate fixture tests" is named
// explicitly as part of S0's quality story even though the gate itself no-ops until a DbContext
// exists).
//
// What these fixtures do NOT cover: clause (a) `has-pending-model-changes` and clause (b) the
// idempotent-reapply-against-a-throwaway-Postgres-container check. Both require a real dotnet-ef
// toolchain + Docker + an actual EF Core project — that is genuinely S1's first live exercise of
// this gate (BUILD.md local-facts line: "no DbContext yet — S1 is the first consumer of
// ef-gate.sh"), not golden-vector material. What IS deterministic and testable today, with zero
// docker/dotnet-ef dependency, is the gate's guard/skip/ordering behavior — exactly the seam that
// makes B2/B3 (BUILD.md §6) structurally unreachable: S1 cannot land a DbContext without also
// tripping the destructive-verb check and the EF-Core-project prerequisite, in that order, before
// the gate would ever try to touch a container. These fixtures pin that ordering now so a future
// edit to ef-gate.sh cannot silently reorder clause (c) after clause (a) (which would let an
// unmarked DROP TABLE slip through if a project happens to have no EF Core reference).
//
// Run: node --test build/scripts/ef-gate.test.mjs

import { test } from "node:test";
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

const SCRIPT = fileURLToPath(new URL("./ef-gate.sh", import.meta.url));

function withTempBackend(fn) {
  const dir = mkdtempSync(join(tmpdir(), "ef-gate-fixture-"));
  try {
    return fn(dir);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}

function runGate(backendDir) {
  try {
    const stdout = execFileSync("bash", [SCRIPT, backendDir], { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] });
    return { status: 0, stdout, stderr: "" };
  } catch (err) {
    return { status: err.status ?? 1, stdout: err.stdout?.toString() ?? "", stderr: err.stderr?.toString() ?? "" };
  }
}

const DBCONTEXT_CS = `namespace Svac.Fixture;\npublic class FixtureDbContext : DbContext\n{\n}\n`;
const UNMARKED_DROP_CS = `migrationBuilder.Sql("DROP TABLE \\"Widgets\\";");`;
const MARKED_DROP_CS = `// -- destructive: legacy_widgets dead since S3, verified zero prod reads\nmigrationBuilder.Sql("DROP TABLE \\"Widgets\\";");`;

test("golden SKIP — backend dir does not exist at all (repo-root-glue-only S0 state)", () => {
  withTempBackend((dir) => {
    const missing = join(dir, "does-not-exist");
    const result = runGate(missing);
    assert.equal(result.status, 0);
    assert.match(result.stdout, /ef-gate SKIP: .* does not exist/);
  });
});

test("golden SKIP — backend dir exists but declares zero DbContext (the S0 baseline)", () => {
  withTempBackend((dir) => {
    mkdirSync(join(dir, "SomeProject"), { recursive: true });
    writeFileSync(join(dir, "SomeProject", "Program.cs"), "public class Program {}\n");
    const result = runGate(dir);
    assert.equal(result.status, 0);
    assert.match(result.stdout, /ef-gate SKIP: no DbContext found under .* \(guarded — S1 is the first consumer\)/);
  });
});

test("clause ordering — destructive-verb check (c) fails BEFORE the EF-Core-project prerequisite (a) is even checked", () => {
  withTempBackend((dir) => {
    const proj = join(dir, "Svac.Domain");
    const migrations = join(proj, "Migrations");
    mkdirSync(migrations, { recursive: true });
    writeFileSync(join(proj, "FixtureDbContext.cs"), DBCONTEXT_CS);
    writeFileSync(join(migrations, "20260710_DropWidgets.cs"), UNMARKED_DROP_CS);
    // Deliberately NO .csproj anywhere — if clause (a)'s prerequisite ran first it would fail with
    // a DIFFERENT message ("no .csproj references Microsoft.EntityFrameworkCore"). It must not:
    // the unmarked destructive verb is the more severe finding and must block first.
    const result = runGate(dir);
    assert.equal(result.status, 1);
    assert.match(result.stderr, /ef-gate BLOCKED: destructive-verb check failed/);
    assert.doesNotMatch(result.stderr, /no \.csproj references/);
  });
});

test("clause ordering — a MARKED destructive migration clears (c), then (a)'s EF-Core-project prerequisite fires next", () => {
  withTempBackend((dir) => {
    const proj = join(dir, "Svac.Domain");
    const migrations = join(proj, "Migrations");
    mkdirSync(migrations, { recursive: true });
    writeFileSync(join(proj, "FixtureDbContext.cs"), DBCONTEXT_CS);
    writeFileSync(join(migrations, "20260710_DropWidgets.cs"), MARKED_DROP_CS);
    // Still no .csproj referencing Microsoft.EntityFrameworkCore.
    const result = runGate(dir);
    assert.equal(result.status, 1);
    assert.doesNotMatch(result.stderr, /destructive-verb check failed/);
    assert.match(result.stderr, /ef-gate BLOCKED: DbContext found but no \.csproj references Microsoft\.EntityFrameworkCore/);
  });
});

test("golden — a DbContext with no Migrations/ directory yet skips clause (c) cleanly and still enforces (a)'s prerequisite", () => {
  withTempBackend((dir) => {
    const proj = join(dir, "Svac.Domain");
    mkdirSync(proj, { recursive: true });
    writeFileSync(join(proj, "FixtureDbContext.cs"), DBCONTEXT_CS);
    const result = runGate(dir);
    assert.equal(result.status, 1);
    assert.match(result.stdout, /ef-gate: no Migrations\/ directories yet, skipping destructive-verb check/);
    assert.match(result.stderr, /ef-gate BLOCKED: DbContext found but no \.csproj references Microsoft\.EntityFrameworkCore/);
  });
});

test("golden — DbContext discovery lists every matching file, not just the first", () => {
  withTempBackend((dir) => {
    const projA = join(dir, "Svac.Domain");
    const projB = join(dir, "Svac.Reporting");
    mkdirSync(projA, { recursive: true });
    mkdirSync(projB, { recursive: true });
    writeFileSync(join(projA, "FixtureDbContext.cs"), DBCONTEXT_CS);
    writeFileSync(join(projB, "ReportingDbContext.cs"), `public class ReportingDbContext : DbContext {}\n`);
    const result = runGate(dir);
    assert.match(result.stdout, /ef-gate: found DbContext\(s\):/);
    assert.match(result.stdout, /FixtureDbContext\.cs/);
    assert.match(result.stdout, /ReportingDbContext\.cs/);
  });
});

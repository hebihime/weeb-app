// infra/admin-host-container-app.test.mjs — unit assertions on the admin host's Container App module
// (SLICE_S5_CONTRACT.md §0 law d / §1a). Same rationale as infra/edge-guard.test.mjs: no `az`/`bicep`
// CLI is available in every environment that runs this gate, so this test asserts structurally against
// the .bicep source itself; CI additionally runs `az bicep build`/`az bicep lint` on this file
// (infra.yml's bicep-build-lint job globs infra/**/*.bicep at maxdepth 2 automatically — no workflow
// edit needed for a new module file to be picked up).
//
// Run: node --test infra/admin-host-container-app.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const __dirname = dirname(fileURLToPath(import.meta.url));
const bicepSource = readFileSync(join(__dirname, "modules", "admin-host-container-app.bicep"), "utf8");

test("ingress is internal-only (external: false) — never reachable from the public internet", () => {
  assert.match(bicepSource, /external:\s*false/);
});

test("ingress carries an ipSecurityRestrictions allowlist block (the 'allowlisted' half of §0 law d)", () => {
  assert.match(bicepSource, /ipSecurityRestrictions:/);
});

test("no path-based routing config exists — never path-mounted under a consumer domain", () => {
  assert.doesNotMatch(bicepSource, /pathsRules|routes:\s*\[/);
});

test("SVAC_DEVSEAMS_ENABLED is hardcoded false in the prod container env — never a template variable that could default true", () => {
  const block = bicepSource.match(/name:\s*'SVAC_DEVSEAMS_ENABLED'[\s\S]{0,80}/)?.[0];
  assert.ok(block, "SVAC_DEVSEAMS_ENABLED env var not found");
  assert.match(block, /value:\s*'false'/);
});

test("ASPNETCORE_ENVIRONMENT is Production, never Development, in this module", () => {
  const block = bicepSource.match(/name:\s*'ASPNETCORE_ENVIRONMENT'[\s\S]{0,80}/)?.[0];
  assert.ok(block, "ASPNETCORE_ENVIRONMENT env var not found");
  assert.match(block, /value:\s*'Production'/);
});

test("the Postgres connection string is a @secure() param with no default — fails closed if unset, never a literal", () => {
  assert.match(bicepSource, /param postgresConnectionString string\s*\n/);
  assert.doesNotMatch(bicepSource, /param postgresConnectionString string\s*=/);
  const paramIdx = bicepSource.indexOf("param postgresConnectionString string");
  const precedingLines = bicepSource.slice(0, paramIdx).split("\n").slice(-3).join("\n");
  assert.match(precedingLines, /@secure\(\)/);
  assert.doesNotMatch(bicepSource, /ConnectionStrings__Core[\s\S]{0,40}value:\s*'/);
});

test("the container image param has no default — fails closed before OQ-3's registry exists", () => {
  assert.match(bicepSource, /param containerImage string\s*\n/);
  assert.doesNotMatch(bicepSource, /param containerImage string\s*=/);
});

test("allowedIngressCidrs defaults to an empty array — fail-closed, no traffic allowed until OQ-3 populates it", () => {
  assert.match(bicepSource, /param allowedIngressCidrs array = \[\]/);
});

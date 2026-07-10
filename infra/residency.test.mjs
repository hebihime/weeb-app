// infra/residency.test.mjs — L21 residency pair-allowlist assertion (SLICE_S0_CONTRACT.md §9: "an IaC
// test fails any data-bearing resource declaring its own location literal" + "Geo-redundant backup
// pairing constrained to an in-jurisdiction pair-allowlist ... asserted in the test, not left to
// memory"). Structural checks against main.bicep + the module files, deterministic, no LLM.
//
// Run: node --test infra/residency.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync, readdirSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const __dirname = dirname(fileURLToPath(import.meta.url));
const mainSource = readFileSync(join(__dirname, "main.bicep"), "utf8");

const DATA_BEARING_MODULES = ["postgres-flexible.bicep", "blob-cdn.bicep", "signalr.bicep", "keyvault.bicep"];

test("main.bicep declares the allowlisted westeurope<->northeurope pair", () => {
  assert.match(mainSource, /westeurope:\s*'northeurope'/);
  assert.match(mainSource, /northeurope:\s*'westeurope'/);
});

test("main.bicep has a residency guard that fails deployment on an invalid pair", () => {
  assert.match(mainSource, /pairIsValid/);
  assert.match(mainSource, /if\s*\(!pairIsValid\)/);
});

test("every data-bearing module receives `location` as a param, never a hardcoded region literal", () => {
  const knownAzureRegions = ["westeurope", "northeurope", "eastus", "westus", "spaincentral"];
  for (const file of DATA_BEARING_MODULES) {
    const source = readFileSync(join(__dirname, "modules", file), "utf8");
    assert.match(source, /param location string/, `${file} must declare a "location" param`);
    for (const region of knownAzureRegions) {
      assert.ok(
        !source.includes(`'${region}'`),
        `${file} hardcodes region literal '${region}' — location must come from the param, not a literal`
      );
    }
  }
});

test("main.bicep passes `location` (not a literal) into every data-bearing module call", () => {
  for (const file of DATA_BEARING_MODULES) {
    const moduleName = file.replace(".bicep", "");
    // crude but sufficient: find the module block for this file and assert it wires `location: location`
    const idx = mainSource.indexOf(`modules/${file}`);
    assert.ok(idx >= 0, `main.bicep does not reference modules/${file}`);
    const block = mainSource.slice(idx, mainSource.indexOf("}", mainSource.indexOf("{", idx)) + 1);
    assert.match(block, /location:\s*location/, `main.bicep's ${moduleName} module call does not wire location: location`);
  }
});

test("every module file under infra/modules/ is referenced from main.bicep (no orphaned module)", () => {
  const moduleFiles = readdirSync(join(__dirname, "modules")).filter((f) => f.endsWith(".bicep"));
  for (const file of moduleFiles) {
    assert.ok(mainSource.includes(`modules/${file}`), `modules/${file} is never referenced from main.bicep`);
  }
});

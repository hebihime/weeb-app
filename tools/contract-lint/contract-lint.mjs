#!/usr/bin/env node
// tools/contract-lint/contract-lint.mjs — SLICE_S0_CONTRACT.md §1. Build-failing rule set v0 over
// contracts/openapi.v0.json. Guarded activation: self-skips (exit 0) when the file does not exist yet
// (S1 is the first real consumer). Deterministic OpenAPI-document walk, no LLM.
//
// Rules (each backed by a ratified ruling — see contracts/README.md):
//   1. Server-authoritative trust (L20): no REQUEST schema contains a trust-field-shaped property name.
//   2. Absence law (token law 3): no consumer-facing RESPONSE schema contains a gate-leak-shaped name.
//   3. One deny shape (10A / DR-7.3): every 429 response on a consumer path $refs LimitReached.
//   4. Silent-rejection unobservability (R5/12A-r, T7-A): no sender-visible pending/void response schema
//      on deny/void-class operations; unauthorized/excluded consumer reads use the same 404 shape as a
//      nonexistent resource — no 403 on consumer read paths.
//
// Usage: node tools/contract-lint/contract-lint.mjs [path/to/openapi.json]
//        defaults to contracts/openapi.v0.json at repo root.

import { readFileSync, existsSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const __dirname = dirname(fileURLToPath(import.meta.url));
const DEFAULT_PATH = join(__dirname, "..", "..", "contracts", "openapi.v0.json");

const TRUST_FIELD_RE = /^(verification|reputation|premium|moderation_state|age_estimate|trust|tier)/i;
const GATE_LEAK_RE = /(_locked|_disabled|_gated)$|^gate_state$|^locked_reason$|^upgrade_required$/i;

// Allowlist file requires a ruling citation per entry (§1.2 — sole known exception: create-crew
// Premium CTA, S27). Empty at S0 since no schemas exist yet.
const ABSENCE_ALLOWLIST = new Set([
  // "CreateCrewResponse.premium_cta_shown", // S27, DR-2.2 — example shape, not yet real
]);

function collectSchemas(doc) {
  return doc.components?.schemas ?? {};
}

function propPaths(schema, prefix = "") {
  const out = [];
  if (!schema || typeof schema !== "object") return out;
  if (schema.properties) {
    for (const [name, sub] of Object.entries(schema.properties)) {
      const path = prefix ? `${prefix}.${name}` : name;
      out.push({ name, path });
      out.push(...propPaths(sub, path));
    }
  }
  if (schema.items) out.push(...propPaths(schema.items, prefix));
  for (const combinator of ["allOf", "oneOf", "anyOf"]) {
    if (Array.isArray(schema[combinator])) {
      for (const sub of schema[combinator]) out.push(...propPaths(sub, prefix));
    }
  }
  return out;
}

/** Rule 1: server-authoritative trust fields absent from request schemas. */
export function checkServerAuthoritativeTrust(doc) {
  const violations = [];
  for (const [path, methods] of Object.entries(doc.paths ?? {})) {
    for (const [method, op] of Object.entries(methods)) {
      const bodySchema = op.requestBody?.content?.["application/json"]?.schema;
      if (!bodySchema) continue;
      for (const { name, path: propPath } of propPaths(bodySchema)) {
        if (TRUST_FIELD_RE.test(name)) {
          violations.push(`${method.toUpperCase()} ${path}: request field "${propPath}" looks trust-authoritative (L20) — server sets this, client never sends it`);
        }
      }
    }
  }
  return violations;
}

/** Rule 2: absence law — no gate-leak field in consumer-facing response schemas. */
export function checkAbsenceLaw(doc, allowlist = ABSENCE_ALLOWLIST) {
  const violations = [];
  for (const [path, methods] of Object.entries(doc.paths ?? {})) {
    for (const [method, op] of Object.entries(methods)) {
      for (const [status, response] of Object.entries(op.responses ?? {})) {
        const schema = response.content?.["application/json"]?.schema;
        if (!schema) continue;
        for (const { name, path: propPath } of propPaths(schema)) {
          if (GATE_LEAK_RE.test(name)) {
            const key = `${op.operationId ?? `${method} ${path}`}.${propPath}`;
            if (allowlist.has(key)) continue;
            violations.push(`${method.toUpperCase()} ${path} [${status}]: response field "${propPath}" leaks gate state (token law 3) — the field must be ABSENT below the gate, not present-and-false`);
          }
        }
      }
    }
  }
  return violations;
}

/** Rule 3: one deny shape — every 429 on a consumer path $refs the shared LimitReached component. */
export function checkOneDenyShape(doc) {
  const violations = [];
  for (const [path, methods] of Object.entries(doc.paths ?? {})) {
    if (path.startsWith("/admin") || path.startsWith("/partner")) continue; // consumer paths only
    for (const [method, op] of Object.entries(methods)) {
      const resp429 = op.responses?.["429"];
      if (!resp429) continue;
      const schema = resp429.content?.["application/json"]?.schema;
      const ref = schema?.$ref;
      if (ref !== "#/components/schemas/LimitReached") {
        violations.push(`${method.toUpperCase()} ${path} [429]: must $ref #/components/schemas/LimitReached (10A / DR-7.3 one deny shape), found ${ref ? `"${ref}"` : "an inline schema"}`);
      }
    }
  }
  return violations;
}

/** Rule 4: silent-rejection unobservability. */
export function checkSilentRejection(doc) {
  const violations = [];
  const PENDING_VOID_RE = /^(pending|void|voided)$/i;
  for (const [path, methods] of Object.entries(doc.paths ?? {})) {
    if (path.startsWith("/admin") || path.startsWith("/partner")) continue;
    for (const [method, op] of Object.entries(methods)) {
      if (op["x-deny-void-class"]) {
        for (const [status, response] of Object.entries(op.responses ?? {})) {
          const schema = response.content?.["application/json"]?.schema;
          for (const { name, path: propPath } of propPaths(schema)) {
            if (PENDING_VOID_RE.test(name)) {
              violations.push(`${method.toUpperCase()} ${path} [${status}]: deny/void-class op declares sender-visible field "${propPath}" (R5/12A-r/T7-A) — the sender must never observe a void`);
            }
          }
        }
      }
      if (op["x-consumer-read"] && op.responses?.["403"]) {
        violations.push(`${method.toUpperCase()} ${path}: consumer read path declares 403 — unauthorized/excluded reads must use the same 404 shape as nonexistent (no distinguishing status)`);
      }
    }
  }
  return violations;
}

export function lintDocument(doc) {
  return [
    ...checkServerAuthoritativeTrust(doc),
    ...checkAbsenceLaw(doc),
    ...checkOneDenyShape(doc),
    ...checkSilentRejection(doc),
  ];
}

async function main() {
  const target = process.argv[2] ?? DEFAULT_PATH;
  if (!existsSync(target)) {
    console.log(`contract-lint SKIP: ${target} does not exist yet (guarded activation — S1 is the first consumer)`);
    return;
  }
  const doc = JSON.parse(readFileSync(target, "utf8"));
  const violations = lintDocument(doc);
  if (violations.length > 0) {
    console.error(`contract-lint BLOCKED (${target}):`);
    for (const v of violations) console.error(`  - ${v}`);
    process.exitCode = 1;
    return;
  }
  console.log(`contract-lint OK: ${target} passes all v0 rules`);
}

const isMain = process.argv[1] && import.meta.url === `file://${process.argv[1]}`;
if (isMain) {
  main();
}

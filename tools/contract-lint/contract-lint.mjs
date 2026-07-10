#!/usr/bin/env node
// tools/contract-lint/contract-lint.mjs — SLICE_S0_CONTRACT.md §1. Build-failing rule set v0 over
// contracts/openapi.v0.json. Guarded activation: self-skips (exit 0) when the file does not exist yet
// (S1 is the first real consumer). Deterministic OpenAPI-document walk, no LLM.
//
// Rules (each backed by a ratified ruling — see contracts/README.md):
//   1. Server-authoritative trust (L20): no REQUEST schema (body OR parameter, any json/multipart media
//      type) contains a trust-field-shaped property name.
//   2. Absence law (token law 3): no consumer-facing RESPONSE schema contains a gate-leak-shaped name.
//   3. One deny shape (10A / DR-7.3): every 429 response on a consumer path $refs LimitReached.
//   4. Silent-rejection unobservability (R5/12A-r, T7-A): no sender-visible pending/void response schema
//      on any mutation-class operation; unauthorized/excluded consumer reads use the same 404 shape as a
//      nonexistent resource — no 403 on any consumer GET/HEAD. Both checks are UNCONDITIONAL (SECURITY_
//      REVIEW_S0.md trust-boundary F2/silent-rejection F3): a security default must not depend on an
//      author remembering to opt in with a marker.
//
// $ref resolution (SECURITY_REVIEW_S0.md trust-boundary F1/silent-rejection F1/minor F1): every rule
// walks schemas through propPaths(), which resolves local `#/components/...` $refs (with a cycle guard)
// so a trust field, gate leak, or void tell hidden behind a component reference is not invisible to the
// lint just because the author used idiomatic $ref composition instead of an inline schema.
//
// Usage: node tools/contract-lint/contract-lint.mjs [path/to/openapi.json]
//        defaults to contracts/openapi.v0.json at repo root.

import { readFileSync, existsSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const __dirname = dirname(fileURLToPath(import.meta.url));
const DEFAULT_PATH = join(__dirname, "..", "..", "contracts", "openapi.v0.json");

// MinorProt-F5 (SECURITY_REVIEW_S1.md) — mirrors backend/tests/Svac.Tests.Architecture/TrustDtoArchTest.cs's
// TrustFieldPattern exactly: the canonical forgeable-18+ attest field names (age_verified, age_attested,
// is_adult, adult_verified, birthdate_verified, minor_flag) were previously invisible to this gate.
const TRUST_FIELD_RE = /^(verification|reputation|premium|moderation_state|age_estimate|age_?verified|age_?attested|is_?adult|adult_?verified|birthdate_?verified|minor_?flag|trust|tier)/i;
const GATE_LEAK_RE = /(_locked|_disabled|_gated)$|^gate_state$|^locked_reason$|^upgrade_required$/i;

// Mutation-class HTTP methods for rule 4's void-tell check (SECURITY_REVIEW_S0.md IDOR F2 / silent-
// rejection F3): a deny/void result is only meaningful on an operation that attempts a state change.
const MUTATION_METHODS = new Set(["post", "put", "patch", "delete"]);

// Allowlist file requires a ruling citation per entry (§1.2 — sole known exception: create-crew
// Premium CTA, S27). Empty at S0 since no schemas exist yet.
const ABSENCE_ALLOWLIST = new Set([
  // "CreateCrewResponse.premium_cta_shown", // S27, DR-2.2 — example shape, not yet real
]);

/** Resolve a local "#/a/b/c" JSON-pointer $ref against the full document. Non-local refs -> undefined. */
function resolveRef(doc, ref) {
  if (typeof ref !== "string" || !ref.startsWith("#/")) return undefined;
  const parts = ref.slice(2).split("/").map((p) => p.replace(/~1/g, "/").replace(/~0/g, "~"));
  let node = doc;
  for (const part of parts) {
    if (node == null || typeof node !== "object") return undefined;
    node = node[part];
  }
  return node;
}

/**
 * Walk a schema's property paths, resolving local $refs (cycle-guarded) so trust fields / gate leaks /
 * void tells declared in components.schemas and referenced from a path operation are not invisible.
 */
function propPaths(schema, doc, prefix = "", seen = new Set()) {
  const out = [];
  if (!schema || typeof schema !== "object") return out;
  if (schema.$ref) {
    if (seen.has(schema.$ref)) return out; // cycle guard — already expanding this ref on this branch
    const resolved = resolveRef(doc, schema.$ref);
    if (resolved) {
      out.push(...propPaths(resolved, doc, prefix, new Set(seen).add(schema.$ref)));
    }
    return out;
  }
  if (schema.properties) {
    for (const [name, sub] of Object.entries(schema.properties)) {
      const path = prefix ? `${prefix}.${name}` : name;
      out.push({ name, path });
      out.push(...propPaths(sub, doc, path, seen));
    }
  }
  if (schema.items) out.push(...propPaths(schema.items, doc, prefix, seen));
  for (const combinator of ["allOf", "oneOf", "anyOf"]) {
    if (Array.isArray(schema[combinator])) {
      for (const sub of schema[combinator]) out.push(...propPaths(sub, doc, prefix, seen));
    }
  }
  return out;
}

/**
 * Media types worth walking for a request/response body (minor F3, SECURITY_REVIEW_S0.md): every
 * *json* media type (application/json, application/json; charset=utf-8, ...) AND multipart/* bodies
 * (the S18 verification photo upload), not just the exact literal "application/json".
 */
function relevantSchemas(content) {
  if (!content || typeof content !== "object") return [];
  return Object.entries(content)
    .filter(([mediaType]) => /json/i.test(mediaType) || /^multipart\//i.test(mediaType))
    .map(([, mediaObj]) => mediaObj?.schema)
    .filter(Boolean);
}

/** Rule 1: server-authoritative trust fields absent from request schemas (body AND parameters). */
export function checkServerAuthoritativeTrust(doc) {
  const violations = [];
  for (const [path, methods] of Object.entries(doc.paths ?? {})) {
    for (const [method, op] of Object.entries(methods)) {
      for (const bodySchema of relevantSchemas(op.requestBody?.content)) {
        for (const { name, path: propPath } of propPaths(bodySchema, doc)) {
          if (TRUST_FIELD_RE.test(name)) {
            violations.push(`${method.toUpperCase()} ${path}: request field "${propPath}" looks trust-authoritative (L20) — server sets this, client never sends it`);
          }
        }
      }
      for (const param of op.parameters ?? []) {
        if (param?.name && TRUST_FIELD_RE.test(param.name)) {
          violations.push(`${method.toUpperCase()} ${path}: ${param.in ?? "parameter"} "${param.name}" looks trust-authoritative (L20) — server sets this, client never sends it`);
        }
        for (const { name, path: propPath } of propPaths(param?.schema, doc)) {
          if (TRUST_FIELD_RE.test(name)) {
            violations.push(`${method.toUpperCase()} ${path}: ${param.in ?? "parameter"} "${param.name}" field "${propPath}" looks trust-authoritative (L20) — server sets this, client never sends it`);
          }
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
        for (const schema of relevantSchemas(response.content)) {
          for (const { name, path: propPath } of propPaths(schema, doc)) {
            if (GATE_LEAK_RE.test(name)) {
              const key = `${op.operationId ?? `${method} ${path}`}.${propPath}`;
              if (allowlist.has(key)) continue;
              violations.push(`${method.toUpperCase()} ${path} [${status}]: response field "${propPath}" leaks gate state (token law 3) — the field must be ABSENT below the gate, not present-and-false`);
            }
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

/**
 * Rule 4: silent-rejection unobservability. UNCONDITIONAL (SECURITY_REVIEW_S0.md IDOR F2 / trust-
 * boundary F2 / silent-rejection F3): neither check depends on an `x-deny-void-class` / `x-consumer-read`
 * marker any more. Every mutation-class op on a non-/admin non-/partner path is checked for a sender-
 * visible void tell; every GET/HEAD on a non-/admin non-/partner path is checked for a 403 existence
 * oracle. Mirrors checkOneDenyShape's own unconditional-on-every-op posture.
 */
export function checkSilentRejection(doc) {
  const violations = [];
  // Word/segment-boundary match (not exact-anchored) so realistic snake_case void tells — void_reason,
  // pending_since, is_voided, voided_at, rejected, suppressed — are caught, not just the bare words
  // "pending"/"void"/"voided" (silent-rejection F2, SECURITY_REVIEW_S0.md).
  const PENDING_VOID_RE = /(^|_)(pending|void|voided|rejected|suppressed)(_|$)/i;
  for (const [path, methods] of Object.entries(doc.paths ?? {})) {
    if (path.startsWith("/admin") || path.startsWith("/partner")) continue;
    for (const [method, op] of Object.entries(methods)) {
      if (MUTATION_METHODS.has(method.toLowerCase())) {
        for (const [status, response] of Object.entries(op.responses ?? {})) {
          for (const schema of relevantSchemas(response.content)) {
            for (const { name, path: propPath } of propPaths(schema, doc)) {
              if (PENDING_VOID_RE.test(name)) {
                violations.push(`${method.toUpperCase()} ${path} [${status}]: mutation op declares sender-visible field "${propPath}" (R5/12A-r/T7-A) — the sender must never observe a void`);
              }
            }
          }
        }
      }
      if ((method.toLowerCase() === "get" || method.toLowerCase() === "head") && op.responses?.["403"]) {
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

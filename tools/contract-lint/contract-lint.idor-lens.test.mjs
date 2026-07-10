// tools/contract-lint/contract-lint.idor-lens.test.mjs — ADVERSARIAL LENS: auth/IDOR observability.
//
// These tests are written to FAIL against the current tools/contract-lint/contract-lint.mjs. They prove
// that Rule 4 (SLICE_S0_CONTRACT.md §1.4: "unauthorized/excluded consumer reads declare the same 404
// shape as nonexistent — NO 403 on consumer read paths") only fires when an operation VOLUNTARILY tags
// itself `x-consumer-read`. The invariant is a security default, but the gate is opt-in: any consumer
// GET that declares a 403 (an existence oracle for IDOR enumeration) slips through unless the author
// remembered a marker. Rule 3 (one deny shape) already proves the right pattern — it applies to every
// non-/admin non-/partner path automatically. Rule 4 should too.
//
// Run: node --test tools/contract-lint/contract-lint.idor-lens.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import { lintDocument, checkSilentRejection } from "./contract-lint.mjs";

function consumerReadWith403(extra = {}) {
  return {
    openapi: "3.1.0",
    paths: {
      "/profiles/{id}": {
        get: {
          operationId: "getProfile",
          ...extra,
          responses: {
            "200": {
              content: { "application/json": { schema: { type: "object", properties: { id: { type: "string" } } } } },
            },
            // 403 distinguishes "exists but you can't see it" from "doesn't exist" -> enumeration oracle.
            "403": {
              description: "blocked or excluded viewer",
              content: { "application/json": { schema: { type: "object", properties: { reason: { type: "string" } } } } },
            },
            "404": { description: "not found" },
          },
        },
      },
    },
  };
}

// --- Control: with the opt-in marker present, the existing lint DOES catch the 403 -------------------
test("[control] tagged x-consumer-read op with a 403 is flagged today", () => {
  const doc = consumerReadWith403({ "x-consumer-read": true });
  const violations = checkSilentRejection(doc);
  assert.ok(
    violations.some((v) => v.includes("403")),
    "sanity: a tagged consumer-read op with 403 should already be flagged"
  );
});

// --- BREAK 1: same 403 oracle, marker omitted -> lint stays silent -----------------------------------
// A consumer GET on /profiles/{id} that returns 403 for a blocked/excluded viewer leaks the resource's
// existence. The invariant forbids it outright. Because the author did not set x-consumer-read, the gate
// does not fire. The default posture of a security gate must be closed, not opt-in.
test("BREAK: consumer GET returning 403 must be flagged even without an x-consumer-read marker", () => {
  const doc = consumerReadWith403(); // no marker
  const violations = lintDocument(doc);
  assert.ok(
    violations.some((v) => /403/.test(v)),
    "consumer GET /profiles/{id} declares a 403 existence oracle but the lint reported nothing — the no-403-on-consumer-read rule is opt-in via x-consumer-read; it must apply to every non-/admin non-/partner read path the way Rule 4's own contract text states"
  );
});

// --- BREAK 2: the oracle also survives on a collection read path -------------------------------------
test("BREAK: any non-admin/non-partner GET with a 403 is an unguarded enumeration oracle", () => {
  const doc = {
    openapi: "3.1.0",
    paths: {
      "/crews/{id}/members": {
        get: {
          operationId: "listCrewMembers",
          responses: {
            "200": { description: "ok" },
            "403": { description: "not a member" }, // reveals the crew exists
            "404": { description: "not found" },
          },
        },
      },
    },
  };
  const violations = lintDocument(doc);
  assert.ok(
    violations.some((v) => /403/.test(v)),
    "GET /crews/{id}/members returns 403 (crew-exists oracle) and the lint is silent — a distinguishing 403 vs 404 on any consumer read path lets an attacker enumerate valid IDs"
  );
});

// tools/contract-lint/contract-lint.trust-boundary.test.mjs
// LENS: trust-boundary (DTO trust absence, money-doors fail closed, never-pay-to-rank).
// These tests assert the SECURE behavior the S0 contract §1 promises. They FAIL against the
// current contract-lint.mjs, demonstrating trust-boundary holes.
//
// Run: node --test tools/contract-lint/contract-lint.trust-boundary.test.mjs

import { test } from "node:test";
import assert from "node:assert/strict";
import {
  checkServerAuthoritativeTrust,
  checkAbsenceLaw,
  checkSilentRejection,
} from "./contract-lint.mjs";

// ---------------------------------------------------------------------------
// BREAK 1: propPaths never resolves $ref. Real OpenAPI docs put request/response
// bodies in components.schemas and reference them with {"$ref": ...}. A client can
// then smuggle server-authoritative trust fields (L20) through a $ref'd request body
// and the lint sees nothing.
// ---------------------------------------------------------------------------
test("BREAK: trust field in a $ref'd REQUEST body escapes rule 1", () => {
  const doc = {
    paths: {
      "/profiles/me": {
        put: {
          requestBody: {
            content: {
              "application/json": {
                schema: { $ref: "#/components/schemas/UpdateProfileRequest" },
              },
            },
          },
          responses: {},
        },
      },
    },
    components: {
      schemas: {
        UpdateProfileRequest: {
          properties: {
            bio: { type: "string" },
            reputation_score: { type: "integer" }, // client sets its own reputation
            premium_tier: { type: "string" }, // never-pay-to-rank: client injects tier
            verification_state: { type: "string" }, // client claims verified
          },
        },
      },
    },
  };
  const violations = checkServerAuthoritativeTrust(doc);
  // SECURE expectation: the three trust-shaped fields are flagged. Current code returns [].
  assert.ok(
    violations.length >= 1,
    `expected trust fields behind $ref to be flagged, got none (${violations.length})`
  );
});

// ---------------------------------------------------------------------------
// BREAK 2: same $ref blindness defeats the absence law. A gate-state leak hidden in a
// referenced response component ships to consumers below the gate.
// ---------------------------------------------------------------------------
test("BREAK: gate-leak field in a $ref'd RESPONSE body escapes rule 2", () => {
  const doc = {
    paths: {
      "/deck": {
        get: {
          responses: {
            "200": {
              content: {
                "application/json": {
                  schema: { $ref: "#/components/schemas/DeckResponse" },
                },
              },
            },
          },
        },
      },
    },
    components: {
      schemas: {
        DeckResponse: {
          properties: {
            cards: { type: "array" },
            upgrade_required: { type: "boolean" }, // gate state leaked to consumer
          },
        },
      },
    },
  };
  const violations = checkAbsenceLaw(doc);
  // SECURE expectation: upgrade_required is flagged. Current code returns [].
  assert.ok(
    violations.length >= 1,
    `expected gate-leak field behind $ref to be flagged, got none (${violations.length})`
  );
});

// ---------------------------------------------------------------------------
// BREAK 3: the "no 403 on consumer read paths" invariant (existence-enumeration
// boundary, R5/12A-r/T7-A) is enforced only when the author opts in with
// x-consumer-read. A consumer GET that omits the marker and returns 403 sails through,
// leaking resource existence. A fail-closed invariant must not depend on an opt-in tag.
// ---------------------------------------------------------------------------
test("BREAK: consumer GET returning 403 without x-consumer-read marker escapes rule 4", () => {
  const doc = {
    paths: {
      "/profiles/42": {
        // no x-consumer-read marker — S1 author forgot it
        get: { responses: { "403": {}, "200": {} } },
      },
    },
  };
  const violations = checkSilentRejection(doc);
  // SECURE expectation: a consumer read path with 403 is flagged regardless of marker.
  assert.ok(
    violations.length >= 1,
    `expected untagged consumer 403 to be flagged, got none (${violations.length})`
  );
});

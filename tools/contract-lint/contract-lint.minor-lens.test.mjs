// tools/contract-lint/contract-lint.minor-lens.test.mjs
// LENS: minor-protection (L1-L4, 18+ invariants). Adversarial suite against SLICE_S0_CONTRACT.md §1.
//
// The contract's rule 1 (L20 server-authoritative trust) exists so a CLIENT can never assert its own
// age_estimate / verification / trust / tier — that assertion is the entire minor age-gate defeat.
// These tests write the contract the way S1+ will actually write it (idiomatic OpenAPI) and prove the
// gate is bypassable. They assert the CORRECT behavior; they FAIL against the current linter.

import { test } from "node:test";
import assert from "node:assert/strict";
import {
  checkServerAuthoritativeTrust,
  checkAbsenceLaw,
} from "./contract-lint.mjs";

test("L20: age_estimate in a $ref'd request body must be caught (BREAK: $ref bodies are never walked)", () => {
  const doc = {
    components: {
      schemas: {
        ProfileUpdate: {
          type: "object",
          properties: {
            display_name: { type: "string" },
            age_estimate: { type: "integer" },       // client asserting its own age band
            verification_status: { type: "string" },  // client asserting it is verified
          },
        },
      },
    },
    paths: {
      "/profile": {
        post: {
          requestBody: {
            content: {
              "application/json": { schema: { $ref: "#/components/schemas/ProfileUpdate" } },
            },
          },
          responses: {},
        },
      },
    },
  };
  const violations = checkServerAuthoritativeTrust(doc);
  assert.ok(
    violations.length >= 1,
    "a $ref'd request body carrying age_estimate/verification_status must be flagged; the linter only walks inline schemas so the L20 gate is dead on the idiomatic contract"
  );
});

test("L20: age_estimate as a query/path parameter must be caught (BREAK: parameters are never inspected)", () => {
  const doc = {
    paths: {
      "/deck": {
        get: {
          parameters: [
            { name: "age_estimate", in: "query", schema: { type: "integer" } }, // client-asserted age band
            { name: "trust_tier", in: "query", schema: { type: "string" } },
          ],
          responses: {},
        },
      },
    },
  };
  const violations = checkServerAuthoritativeTrust(doc);
  assert.ok(
    violations.length >= 1,
    "a client sending age_estimate as a query parameter is the same L20 violation as a body field; the linter only looks at requestBody"
  );
});

test("L20: age_estimate in a multipart verification upload must be caught (BREAK: only application/json is walked)", () => {
  // S18 verification photo upload is multipart/form-data. A client slipping age_estimate into that form
  // defeats estimation-first age gating (T2r) and the linter never looks at non-JSON content.
  const doc = {
    paths: {
      "/verification/photo": {
        post: {
          requestBody: {
            content: {
              "multipart/form-data": {
                schema: {
                  type: "object",
                  properties: {
                    photo: { type: "string", format: "binary" },
                    age_estimate: { type: "integer" },
                  },
                },
              },
            },
          },
          responses: {},
        },
      },
    },
  };
  const violations = checkServerAuthoritativeTrust(doc);
  assert.ok(
    violations.length >= 1,
    "age_estimate in a multipart verification upload must be flagged; the linter hardcodes content['application/json']"
  );
});

test("token-law-3: *_locked gate leak in a $ref'd response must be caught (BREAK: $ref responses never walked)", () => {
  // A gate-state leak tells a below-gate user (incl. a minor below the 18+ DM-media gate) the surface
  // exists. Below a gate the field must be ABSENT, never present-and-false.
  const doc = {
    components: {
      schemas: {
        CardResponse: {
          type: "object",
          properties: {
            id: { type: "string" },
            dm_media_locked: { type: "boolean" }, // leaks the 18+ media gate's existence
          },
        },
      },
    },
    paths: {
      "/cards": {
        get: {
          responses: {
            "200": {
              content: {
                "application/json": { schema: { $ref: "#/components/schemas/CardResponse" } },
              },
            },
          },
        },
      },
    },
  };
  const violations = checkAbsenceLaw(doc);
  assert.ok(
    violations.length >= 1,
    "a $ref'd response leaking *_locked must be flagged; the absence-law walk also only sees inline schemas"
  );
});

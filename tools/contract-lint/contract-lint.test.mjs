// tools/contract-lint/contract-lint.test.mjs — golden-vector tests.
// Run: node --test tools/contract-lint/contract-lint.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import {
  checkServerAuthoritativeTrust,
  checkAbsenceLaw,
  checkOneDenyShape,
  checkSilentRejection,
  lintDocument,
} from "./contract-lint.mjs";

test("checkServerAuthoritativeTrust: golden FAIL — request body carries reputation_score", () => {
  const doc = {
    paths: {
      "/profiles/me": {
        put: {
          requestBody: {
            content: {
              "application/json": {
                schema: { properties: { reputation_score: { type: "integer" }, bio: { type: "string" } } },
              },
            },
          },
          responses: {},
        },
      },
    },
  };
  const violations = checkServerAuthoritativeTrust(doc);
  assert.equal(violations.length, 1);
  assert.match(violations[0], /reputation_score/);
});

test("checkServerAuthoritativeTrust: golden PASS — clean request body", () => {
  const doc = {
    paths: {
      "/profiles/me": {
        put: { requestBody: { content: { "application/json": { schema: { properties: { bio: { type: "string" } } } } } }, responses: {} },
      },
    },
  };
  assert.deepEqual(checkServerAuthoritativeTrust(doc), []);
});

test("checkServerAuthoritativeTrust: nested property inside allOf caught", () => {
  const doc = {
    paths: {
      "/x": {
        post: {
          requestBody: {
            content: {
              "application/json": {
                schema: { allOf: [{ properties: { tier_override: { type: "string" } } }] },
              },
            },
          },
          responses: {},
        },
      },
    },
  };
  const violations = checkServerAuthoritativeTrust(doc);
  assert.equal(violations.length, 1);
  assert.match(violations[0], /tier_override/);
});

test("checkAbsenceLaw: golden FAIL — response leaks feature_locked", () => {
  const doc = {
    paths: {
      "/deck": {
        get: {
          responses: {
            "200": { content: { "application/json": { schema: { properties: { feature_locked: { type: "boolean" } } } } } },
          },
        },
      },
    },
  };
  const violations = checkAbsenceLaw(doc);
  assert.equal(violations.length, 1);
  assert.match(violations[0], /feature_locked/);
});

test("checkAbsenceLaw: golden PASS — field absent entirely", () => {
  const doc = {
    paths: { "/deck": { get: { responses: { "200": { content: { "application/json": { schema: { properties: { cards: { type: "array" } } } } } } } } } },
  };
  assert.deepEqual(checkAbsenceLaw(doc), []);
});

test("checkAbsenceLaw: allowlisted operationId+field is exempted with citation", () => {
  const doc = {
    paths: {
      "/crews": {
        post: {
          operationId: "createCrew",
          responses: { "200": { content: { "application/json": { schema: { properties: { premium_cta_shown: { type: "boolean" } } } } } } },
        },
      },
    },
  };
  const allowlist = new Set(["createCrew.premium_cta_shown"]);
  assert.deepEqual(checkAbsenceLaw(doc, allowlist), []);
});

test("checkOneDenyShape: golden FAIL — inline 429 schema instead of shared ref", () => {
  const doc = {
    paths: {
      "/gifts/send": {
        post: { responses: { "429": { content: { "application/json": { schema: { properties: { message: { type: "string" } } } } } } } },
      },
    },
  };
  const violations = checkOneDenyShape(doc);
  assert.equal(violations.length, 1);
  assert.match(violations[0], /LimitReached/);
});

test("checkOneDenyShape: golden PASS — correct shared ref", () => {
  const doc = {
    paths: {
      "/gifts/send": {
        post: { responses: { "429": { content: { "application/json": { schema: { $ref: "#/components/schemas/LimitReached" } } } } } },
      },
    },
  };
  assert.deepEqual(checkOneDenyShape(doc), []);
});

test("checkOneDenyShape: admin paths are exempt (not a consumer path)", () => {
  const doc = {
    paths: {
      "/admin/reports": {
        post: { responses: { "429": { content: { "application/json": { schema: { properties: {} } } } } } },
      },
    },
  };
  assert.deepEqual(checkOneDenyShape(doc), []);
});

test("checkSilentRejection: golden FAIL — deny-class op exposes a pending field", () => {
  const doc = {
    paths: {
      "/romantic-intent": {
        post: {
          "x-deny-void-class": true,
          responses: { "200": { content: { "application/json": { schema: { properties: { pending: { type: "boolean" } } } } } } },
        },
      },
    },
  };
  const violations = checkSilentRejection(doc);
  assert.equal(violations.length, 1);
  assert.match(violations[0], /pending/);
});

test("checkSilentRejection: golden FAIL — consumer read declares 403", () => {
  const doc = {
    paths: {
      "/profiles/42": {
        get: { "x-consumer-read": true, responses: { "403": {}, "200": {} } },
      },
    },
  };
  const violations = checkSilentRejection(doc);
  assert.equal(violations.length, 1);
  assert.match(violations[0], /same 404 shape/);
});

test("checkSilentRejection: golden PASS — deny-class op with no visible state, read with only 404", () => {
  const doc = {
    paths: {
      "/romantic-intent": { post: { "x-deny-void-class": true, responses: { "200": { content: { "application/json": { schema: { properties: { ok: { type: "boolean" } } } } } } } } },
      "/profiles/42": { get: { "x-consumer-read": true, responses: { "404": {}, "200": {} } } },
    },
  };
  assert.deepEqual(checkSilentRejection(doc), []);
});

test("lintDocument: golden vector combining all four rules on one doc", () => {
  const getResponses = {
    "200": { content: { "application/json": { schema: { properties: { feature_locked: { type: "boolean" } } } } } },
  };
  const postRequestBody = {
    content: { "application/json": { schema: { properties: { trust_override: { type: "string" } } } } },
  };
  const postResponses = {
    "429": { content: { "application/json": { schema: { properties: {} } } } },
  };
  const doc = {
    paths: {
      "/deck": {
        get: { responses: getResponses },
        post: { requestBody: postRequestBody, responses: postResponses },
      },
    },
  };
  const violations = lintDocument(doc);
  // feature_locked leak (absence law) + trust_override request field (trust) + non-ref 429 (deny shape) = 3
  assert.equal(violations.length, 3);
});

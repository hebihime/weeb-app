// tools/contract-lint/contract-lint.silent-rejection.test.mjs
// ADVERSARIAL LENS: silent-rejection leaks (deny/void/exclusion/tier-floor unobservable to the sender;
// no 403-vs-404 tell). These tests encode SLICE_S0_CONTRACT.md §1 rules 2 & 4 as the contract PROMISES
// they are, not as the current implementation happens to behave. They FAIL against contract-lint.mjs as
// shipped at a95edb5 — each failure is a demonstrated hole through which a silent-rejection leak reaches
// the locked OpenAPI contract undetected. Run: node --test contract-lint.silent-rejection.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import { checkAbsenceLaw, checkSilentRejection } from "./contract-lint.mjs";

// ---------------------------------------------------------------------------
// BREAK 1 — $ref responses are never dereferenced. In real OpenAPI, path
// operations $ref component schemas; the lint walks only inline `properties`.
// A void tell (rule 4) or gate-state leak (rule 2) defined in components/schemas
// and referenced from the path sails straight through.
// ---------------------------------------------------------------------------
test("SILENT-REJECTION: void tell in a $ref'd component response is caught (rule 4)", () => {
  const doc = {
    paths: {
      "/romantic-intent": {
        post: {
          "x-deny-void-class": true,
          responses: { "200": { content: { "application/json": { schema: { $ref: "#/components/schemas/IntentResult" } } } } },
        },
      },
    },
    components: { schemas: { IntentResult: { properties: { pending: { type: "boolean" }, voided: { type: "boolean" } } } } },
  };
  // The sender must never observe a void; pending/voided in the response body is the leak.
  assert.ok(checkSilentRejection(doc).length >= 1, "void tell hidden behind a $ref went undetected");
});

test("SILENT-REJECTION: gate-state leak in a $ref'd component response is caught (rule 2)", () => {
  const doc = {
    paths: {
      "/deck": { get: { responses: { "200": { content: { "application/json": { schema: { $ref: "#/components/schemas/DeckCard" } } } } } } },
    },
    components: { schemas: { DeckCard: { properties: { name: { type: "string" }, match_locked: { type: "boolean" }, upgrade_required: { type: "boolean" } } } } },
  };
  assert.ok(checkAbsenceLaw(doc).length >= 1, "gate-state leak hidden behind a $ref went undetected");
});

// ---------------------------------------------------------------------------
// BREAK 2 — PENDING_VOID_RE = /^(pending|void|voided)$/i is exact-anchored.
// Every realistic void/pending tell name escapes: void_reason, pending_since,
// is_voided, voided_at. A correctly-marked deny/void-class op still leaks.
// ---------------------------------------------------------------------------
test("SILENT-REJECTION: real-world void tell field names are caught (rule 4)", () => {
  const doc = {
    paths: {
      "/romantic-intent": {
        post: {
          "x-deny-void-class": true,
          responses: {
            "200": {
              content: {
                "application/json": {
                  schema: {
                    properties: {
                      void_reason: { type: "string" },
                      pending_since: { type: "string" },
                      is_voided: { type: "boolean" },
                      voided_at: { type: "string" },
                    },
                  },
                },
              },
            },
          },
        },
      },
    },
  };
  // Four distinct sender-visible void tells; none matches the exact-anchored regex.
  assert.ok(checkSilentRejection(doc).length >= 1, "void tells named void_reason/pending_since/is_voided/voided_at all escaped the anchored regex");
});

// ---------------------------------------------------------------------------
// BREAK 3 — silent-rejection enforcement is opt-in. Both checks only fire when
// the author tags the op with x-deny-void-class / x-consumer-read. The default
// (untagged) op gets ZERO enforcement, so the leak is reachable by omission —
// the opposite of the "structurally unreachable" claim in §0 / §1.4.
// ---------------------------------------------------------------------------
test("SILENT-REJECTION: an untagged consumer GET declaring 403 is caught (rule 4)", () => {
  // No x-consumer-read marker. A 403 on a read path is the classic exclusion tell:
  // it distinguishes 'exists but you may not see it' from 'does not exist' (404).
  const doc = { paths: { "/profiles/42": { get: { responses: { "403": {}, "200": {} } } } } };
  assert.ok(checkSilentRejection(doc).length >= 1, "unmarked consumer read declared 403 (exclusion tell) and passed");
});

test("SILENT-REJECTION: an untagged deny/void response exposing a void is caught (rule 4)", () => {
  // No x-deny-void-class marker — but the shape is unmistakably a deny/void result the sender sees.
  const doc = {
    paths: {
      "/gifts/send": {
        post: { responses: { "200": { content: { "application/json": { schema: { properties: { voided: { type: "boolean" }, void_reason: { type: "string" } } } } } } } },
      },
    },
  };
  assert.ok(checkSilentRejection(doc).length >= 1, "unmarked op exposed voided/void_reason to sender and passed");
});

// tools/contract-lint/contract-lint.deferred.test.mjs — DEFERRED findings from SECURITY_REVIEW_S0.md.
//
// Both tests below assert the CORRECT (secure) behavior and FAIL against the current contract-lint.mjs
// — same as every other lens test in this repo — but are explicitly skip-annotated because the real fix
// is expensive/blocked (see each test's skip reason and SECURITY_REVIEW_S0.md for the full analysis).
// The proof of the gap stays in code so the suite is honest about what S0 does NOT yet enforce, while
// staying green until the fix lands.
//
// Run: node --test tools/contract-lint/contract-lint.deferred.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import { lintDocument, checkSilentRejection } from "./contract-lint.mjs";

// ---------------------------------------------------------------------------------------------
// PII/residency F3 — no v0 rule forbids PII/special-category fields (identity docs, precise location,
// verification media, consent detail) in /partner response schemas. "never-partner-visible" is a real
// invariant the product needs but nothing in the v0 rule set (checkServerAuthoritativeTrust /
// checkAbsenceLaw / checkOneDenyShape / checkSilentRejection) checks for it — those rules catch trust-
// field injection, gate-state leaks, deny-shape drift, and silent rejection, none of which is "PII on a
// partner-visible response." Expensive: this is a NEW rule on the §1-locked v0 rule set, which means a
// contract version bump plus a PII field-name taxonomy design — not a cheap fix.
// ---------------------------------------------------------------------------------------------
test(
  "[DEFERRED] /partner response schemas must not carry PII/special-category fields (PII/residency F3)",
  {
    skip:
      "deferred: SECURITY_REVIEW_S0.md PII/residency F3 (MEDIUM) — adding a never-partner-visible rule " +
      "to the §1-locked v0 rule set is a contract version bump + PII field-name design, not a cheap fix.",
  },
  () => {
    const doc = {
      paths: {
        "/partner/matches": {
          get: {
            responses: {
              "200": {
                content: {
                  "application/json": {
                    schema: {
                      properties: {
                        display_name: { type: "string" },
                        identity_document_number: { type: "string" }, // special-category identity doc
                        precise_location: { type: "object" }, // precise geolocation
                        verification_media_url: { type: "string" }, // verification photo/video
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
    const violations = lintDocument(doc);
    assert.ok(
      violations.length >= 1,
      "identity_document_number / precise_location / verification_media_url leak on a /partner response " +
        "and nothing in the v0 rule set catches it — 'never-partner-visible' is an unenforced invariant"
    );
  }
);

// ---------------------------------------------------------------------------------------------
// trust-boundary F3 — consumer-path detection by /admin /partner string PREFIX contradicts the
// ratified 3-host layout (CLAUDE.md: public API / admin API / partner API are separate ASP.NET hosts,
// not path prefixes on one host). An admin-only route that happens not to start with "/admin" (e.g.
// "/reports", served only from the admin host) is wrongly treated as a consumer path by every check
// that gates on this prefix (checkOneDenyShape, checkSilentRejection) — its legitimate 403 gets flagged
// as an IDOR enumeration oracle even though no consumer can ever reach it. Expensive/blocked: the
// correct fix needs the TRUE host->path mapping, which no contract defines yet at S0 (that mapping is
// S1's to write once real routes exist) — fixing the heuristic now would just swap one guess for
// another.
// ---------------------------------------------------------------------------------------------
test(
  "[DEFERRED] consumer-path detection by /admin /partner prefix must reflect the true host->path map, not string-prefix guessing (trust-boundary F3)",
  {
    skip:
      "deferred: SECURITY_REVIEW_S0.md trust-boundary F3 (MEDIUM) — correct fix needs the true host->path " +
      "mapping, which no contract defines at S0; blocked pending the S1 host map.",
  },
  () => {
    // "/reports" is an admin-host-only route under the ratified 3-host layout but does not start with
    // "/admin", so the prefix heuristic treats it as a consumer path and flags its legitimate 403 as an
    // enumeration oracle.
    const doc = {
      paths: {
        "/reports": {
          get: {
            responses: {
              "403": {}, // legitimately admin-only; not a consumer enumeration oracle
              "200": {},
            },
          },
        },
      },
    };
    const violations = checkSilentRejection(doc);
    assert.equal(
      violations.length,
      0,
      "/reports is an admin-host-only route under the ratified 3-host layout, but the /admin /partner " +
        "PREFIX heuristic doesn't recognize it as such and flags its 403 as a consumer-read enumeration " +
        "oracle — the heuristic needs the real host->path map, not string-prefix guessing"
    );
  }
);

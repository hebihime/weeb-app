# SECURITY_REVIEW_S0.md — slice S0 (repo-ci-iac) remediation record

**Reviewed commit (base):** `0e66379` ("S0 test-author: ef-gate fixture tests"). **Scope:** every gate,
lint, and IaC file S0 ships — `infra/`, `.github/workflows/`, `build/scripts/`, `tools/contract-lint/`,
`tools/ddl-lint/`. **Method:** each finding below is backed by an adversarial "lens" test that encodes
the CORRECT (secure) behavior and was written to fail against the code as shipped — a demonstrated
break, not a hypothetical. Every `fixNow` finding is now remediated and its lens test is green. Every
`defer` finding keeps its lens test in the tree, `Skip`-annotated with the reason, so the gap stays
documented and provable without failing the gate.

**Final gate result:** see [§3](#3-gate-result-actual). 126 tests: **119 pass, 0 fail, 7 skipped**
(skips == the 7 deferred findings below, one test each). Zero fails.

---

## 1. Remediated (`fixNow`) — 15 findings, all green

### IDOR F1 (HIGH) — encoded-traversal / `/internal` WAF bypass

- **Where:** `infra/edge-guard.bicep` (rules `RejectEncodedTraversal`, `RejectInternalReachThrough`),
  `backend/e2e/edge-guard.mjs` (`ADVERSARIAL_PATHS`).
- **Break:** both rules matched literal percent-substrings with a `Lowercase`-only transform. A
  double-encoded path (`%252e%252e`) or a percent-encoded `/internal` (`%69nternal`) never contained the
  literal matchValue, so the WAF passed it through for the origin to decode and act on.
- **Fix:** added `UrlDecode` to both rules' transforms. `RejectEncodedTraversal` gained the post-decode
  literal forms (`../`, `..\`, `/..`, `\..`) so a single-encoded payload — which UrlDecode fully resolves
  to plain dots, no longer containing `%2e` — still matches, plus a `%25` tripwire as a decode-independent
  backstop. `RejectInternalReachThrough` gained the pre-decode literal `%69nternal` as a second layer.
  `ADVERSARIAL_PATHS` gained two double-encoded traversal probes and two encoded-`/internal` probes.
- **Proof:** `infra/edge-guard.idor-lens.test.mjs` — all 4 tests green (was: 3 of 4 failing).
  `infra/edge-guard.test.mjs` (golden vectors) still green — no regression on the existing rule shape.

### IDOR F2 / trust-boundary F2 / silent-rejection F3 (HIGH) — opt-in no-403-on-consumer-read gate

- **Where:** `tools/contract-lint/contract-lint.mjs`, `checkSilentRejection`.
- **Break:** the "no 403 on a consumer read path" rule and the "no sender-visible void tell" rule only
  fired when the author tagged the operation `x-consumer-read` / `x-deny-void-class`. An author who
  forgot the marker shipped an IDOR-enumeration 403 or a silent-void leak with zero lint pressure.
- **Fix:** dropped both marker guards. Every non-`/admin` non-`/partner` GET/HEAD is now checked for a
  403 unconditionally; every non-`/admin` non-`/partner` mutation op (POST/PUT/PATCH/DELETE) is checked
  for a sender-visible void tell unconditionally — mirroring `checkOneDenyShape`'s own always-on posture.
- **Proof:** `tools/contract-lint/contract-lint.idor-lens.test.mjs` (3/3 green),
  `tools/contract-lint/contract-lint.trust-boundary.test.mjs` BREAK 3 (green),
  `tools/contract-lint/contract-lint.silent-rejection.test.mjs` BREAK 3 tests (green). Existing golden
  vectors in `contract-lint.test.mjs` unaffected (markers still accepted, just no longer required).

### PII/residency F1 / minor F4 (HIGH→CRITICAL) — schema-qualified table names invisible to L21

- **Where:** `tools/ddl-lint/ddl-lint.mjs`, `parseCreateTables`.
- **Break:** the `CREATE TABLE` regex anchored on a single identifier and never matched
  `schema.table` or `"schema"."table"`. A modular monolith with schema-per-module (CLAUDE.md
  architecture) emits exactly that shape from `dotnet ef migrations script`; every real PII table
  (`profiles.user_consent`, `"profiles"."UserIdentity"`, `geo.location_pings`) parsed to zero tables and
  the residency gate was a silent no-op on the DDL S1 will actually generate.
- **Fix:** the statement regex now accepts an optional, non-capturing `schema.` / `"schema".` prefix
  before the table identifier; the schema qualifier is discarded and only the bare table name is
  returned, so `pii-patterns.json` glob matching is untouched.
- **Proof:** `tools/ddl-lint/ddl-lint.pii-residency.adversary.test.mjs` (3/3 green),
  `tools/ddl-lint/ddl-lint.minor-lens.test.mjs` schema-qualified test (green). All 9 existing golden
  vectors in `ddl-lint.test.mjs` unaffected (unqualified names still parse identically).

### PII/residency F2 (HIGH) — destructive-verb tripwire misses idiomatic EF Core API

- **Where:** `build/scripts/destructive-verb-check.mjs`, `DESTRUCTIVE_VERB_RE`.
- **Break:** the tripwire only matched raw SQL (`DROP TABLE`, `DROP COLUMN`, `TRUNCATE`), never the
  fluent API (`migrationBuilder.DropColumn(...)`, `.DropTable(...)`) that real `.cs` EF Core migrations
  use almost exclusively. An unmarked `DropColumn(region)` / `DropColumn(lawful_basis)` / `DropTable` of
  a consent store — precisely P3's scar — passed the gate clean.
- **Fix:** added `DropColumn|DropTable|DropSchema` to the verb regex (still `\b`-bounded, so it cannot
  match `CreateTable`/`AddColumn`), with a display-name normalizer so violation messages read
  `DROP COLUMN` / `DROP TABLE` / `DROP SCHEMA` regardless of which syntax triggered them.
- **Proof:** `build/scripts/destructive-verb-check.pii-residency.adversary.test.mjs` (3/3 green). All 7
  existing golden vectors in `destructive-verb-check.test.mjs` unaffected.

### trust-boundary F1 / silent-rejection F1 / minor F1 (CRITICAL) — `$ref` never resolved

- **Where:** `tools/contract-lint/contract-lint.mjs`, `propPaths` (was: dead `collectSchemas`).
- **Break:** every rule walked only inline `properties`; a request/response body expressed as
  `{"$ref": "#/components/schemas/X"}` — the idiomatic OpenAPI shape S1 will actually write — was
  invisible. Trust-field injection, gate-state leaks, and void tells hidden behind a component `$ref`
  all sailed through rules 1, 2, and 4.
- **Fix:** `propPaths(schema, doc, prefix, seen)` now resolves local `#/components/...` JSON-pointer
  refs recursively, with a `seen`-set cycle guard so a self-referential or mutually-referential schema
  pair cannot infinite-loop the linter. Every call site (`checkServerAuthoritativeTrust`,
  `checkAbsenceLaw`, `checkSilentRejection`) passes `doc` through so refs resolve against the full
  document.
- **Proof:** `tools/contract-lint/contract-lint.trust-boundary.test.mjs` BREAK 1/2 (green),
  `tools/contract-lint/contract-lint.silent-rejection.test.mjs` BREAK 1 (both $ref tests, green),
  `tools/contract-lint/contract-lint.minor-lens.test.mjs` $ref tests (green).

### silent-rejection F2 (HIGH) — exact-anchored void-tell regex

- **Where:** `tools/contract-lint/contract-lint.mjs`, `PENDING_VOID_RE`.
- **Break:** `/^(pending|void|voided)$/i` only matched a field named EXACTLY `pending`/`void`/`voided`.
  Every realistic name — `void_reason`, `pending_since`, `is_voided`, `voided_at`, `rejected`,
  `suppressed` — escaped it.
- **Fix:** broadened to a segment-boundary match, `/(^|_)(pending|void|voided|rejected|suppressed)(_|$)/i`
  — matches the word at the start/end of the field name or immediately after/before an underscore, so
  snake_case compounds are caught without false-positiving on unrelated substrings.
- **Proof:** `tools/contract-lint/contract-lint.silent-rejection.test.mjs` BREAK 2 (green).

### minor F2 (HIGH) — client-asserted trust fields via query/path/header parameters

- **Where:** `tools/contract-lint/contract-lint.mjs`, `checkServerAuthoritativeTrust`.
- **Break:** rule 1 only read `op.requestBody`; a client asserting `age_estimate`/`trust_tier` as a
  query parameter (`GET /deck?age_estimate=25&trust_tier=gold`) was never inspected.
- **Fix:** `checkServerAuthoritativeTrust` now also walks `op.parameters`, flagging both the parameter
  name itself and any nested property of a structured (object) parameter schema against
  `TRUST_FIELD_RE`.
- **Proof:** `tools/contract-lint/contract-lint.minor-lens.test.mjs` query-parameter test (green).

### minor F3 (HIGH) — hardcoded `application/json` content lookup

- **Where:** `tools/contract-lint/contract-lint.mjs`, request-body and response-content lookups.
- **Break:** both lookups hardcoded the literal key `"application/json"`. `multipart/form-data` (the
  S18 verification-photo upload) and charset-suffixed `application/json; charset=utf-8` bodies bypassed
  every rule that walks request/response content.
- **Fix:** new `relevantSchemas(content)` helper walks every media-type key matching `/json/i` (covers
  the charset-suffixed form) or `/^multipart\//i`, replacing every hardcoded content-type lookup in
  rules 1, 2, and 4.
- **Proof:** `tools/contract-lint/contract-lint.minor-lens.test.mjs` multipart-upload test (green).

### purge-completeness F1 / F2 / F3 (HIGH / HIGH / MEDIUM) — uncited derivative stores

- **Where:** `infra/README.md`, "13A non-stores" section.
- **Break:** `postgres-flexible.bicep`'s geo-redundant backup (cross-region 7-day derivative copy of
  every future purge-subject row), `blob-cdn.bicep`'s CDN edge cache (per-edge derivative of blob with
  no purge-on-delete wiring), and `log-analytics.bicep`'s 30-day retention (derivative operator-log
  stream) were all declared in code with load-bearing lifetimes and zero citation in the 13A record —
  the exact "unexamined gap" §6 exists to prevent.
- **Fix:** added three cited bullets to the 13A section, each naming the source file, the load-bearing
  setting, why it's a derivative whose lifetime doesn't inherit from a purge on the primary store, and
  who owns closing the gap (S1 purge-registry review) and by when (before prod deploy).
- **Proof:** `infra/purge-completeness.lens.test.mjs` findings 1–3 (3/3 green).

### concurrency F1a (HIGH) — prod deploy job cancellable by an unrelated push

- **Where:** `.github/workflows/infra.yml`, `deploy` job.
- **Break:** the `deploy` job inherited the workflow-level `group: infra-${{ github.ref }}` +
  `cancel-in-progress: true`. A push to `master` touching `infra/**` while a manual prod dispatch was
  mid-`az deployment group create` cancelled the GitHub run while ARM kept mutating prod server-side —
  the pipeline lied about what was actually happening in the account.
- **Fix:** the `deploy` job now declares its own job-level `concurrency` block:
  `cancel-in-progress: false`, so a deploy is queued behind an in-flight one, never torn out from under it.
- **Proof:** `build/scripts/concurrency-lens.test.mjs` F1a (green).

### concurrency F1b (HIGH) — deploys to the same environment race across refs

- **Where:** `.github/workflows/infra.yml`, `deploy` job (same block as F1a).
- **Break:** the group key was `github.ref`, not the target environment; `deploy(prod)` dispatched from
  two different refs landed in two different concurrency groups and ran concurrently against
  `svac-prod-rg` — last-writer-wins.
- **Fix:** the job's own group is `infra-deploy-${{ github.event.inputs.environment }}` — keyed on the
  target environment, so two dispatches at the same environment always serialize regardless of ref.
- **Proof:** `build/scripts/concurrency-lens.test.mjs` F1b (green). Same code change as F1a.

### minor F5 (MEDIUM) — age/birthdate/minor tables absent from the PII pattern set

- **Where:** `tools/ddl-lint/pii-patterns.json`.
- **Break:** the seeded glob set (`*consent*`, `*profile*`, `*identity*`, `*location*`,
  `*verification*`) never matched `age_attestations`, `minor_purge_queue`, or a bare `birthdate` table —
  the central minor-protection datum could ship with no `region`/`lawful_basis` and the residency gate
  would never notice.
- **Fix:** added `*age_attestation*`, `*birthdate*`, `*minor*` to the pattern set. The corresponding
  golden vector in `ddl-lint.test.mjs` (`loadConfig`) was updated to assert the new 8-pattern list —
  an intentional, cited widening, not a silent test edit.
- **Proof:** `tools/ddl-lint/ddl-lint.minor-lens.test.mjs` age-attestation test (green).

### concurrency F5 (LOW/MEDIUM) — multi-commit push only scans the tip commit

- **Where:** `.github/workflows/lints.yml`, `secret-scan` job.
- **Break:** on `push`, `BASE` fell back to `HEAD~1`, so only the diff of the single tip commit reached
  the scanner. A secret added in commit 1 of a 3-commit push and never touched again survived commits
  2–3 unscanned and landed on `master`.
- **Fix:** `BASE` is now `github.event.pull_request.base.sha` on PRs, else `github.event.before` (the
  pre-push HEAD) on push — spanning the FULL pushed range. Falls back to the repo root commit (scan
  everything) if `before` is empty, the all-zero SHA (new-branch push), or unresolvable, rather than
  silently narrowing the range.
- **Proof:** `build/scripts/concurrency-lens.test.mjs` F5 (green) — the test now (a) asserts the
  workflow source actually references `github.event.before`, then (b) proves the resulting range catches
  a secret added in the first commit of a simulated 3-commit push.

---

## 2. Deferred — 7 findings, proof kept in code, `skip`-annotated

Each deferred test still asserts the CORRECT (secure) behavior and still fails if the skip is removed —
the gap is provable, not hidden. Skip reason cites this document; severity/rationale below matches the
input triage.

| Finding | Severity | File : test | Why deferred |
|---|---|---|---|
| PII/residency F3 — no v0 rule forbids PII/special-category fields on `/partner` responses | MEDIUM | `tools/contract-lint/contract-lint.deferred.test.mjs` : `[DEFERRED] /partner response schemas must not carry PII/special-category fields` | New rule on the §1-locked v0 rule set = contract version bump + PII field-name taxonomy design, not a cheap fix |
| trust-boundary F3 — `/admin` `/partner` prefix detection contradicts the ratified 3-host layout | MEDIUM | `tools/contract-lint/contract-lint.deferred.test.mjs` : `[DEFERRED] consumer-path detection by /admin /partner prefix must reflect the true host->path map...` | Correct fix needs the true host→path map, which no contract defines yet at S0; blocked pending the S1 host map |
| purge-completeness F4 — CI-artifact 13A claims (30-day retention, no DB dumps/fixtures) enforced by nothing | MEDIUM | `infra/purge-completeness.lens.test.mjs` : "the recorded CI-artifact policy has a structural enforcement point, not prose" | Needs a new lint script grepping every workflow + wiring into `lints.yml`, not a cheap fix |
| purge-completeness F5 — `DATA_BEARING_MODULES` omits `redis.bicep` / `log-analytics.bicep` | MEDIUM | `infra/purge-completeness.lens.test.mjs` : "residency suite's data-bearing set includes the derivative stores redis and log-analytics" | Enumeration traces to `SLICE_S0_CONTRACT.md:102` — the fix is a versioned contract change, not a test edit |
| concurrency F2 — `release.yml` platform=`both` group overlaps platform=ios/android | MEDIUM | `build/scripts/concurrency-lens.test.mjs` : F2 | Latent until S7 lands `ios/`/`android`/fastlane; not in the protective trust/residency/minor triad |
| concurrency F3 — `compose-smoke.sh` has no compose-project isolation or lock | MEDIUM | `build/scripts/concurrency-lens.test.mjs` : F3 | Not in the protective triad; two parallel sessions in one clone is the exposure, acceptable at S0 |
| concurrency F4 — `ef-gate.sh` throwaway container name keyed on `$$` (PID) | LOW | `build/scripts/concurrency-lens.test.mjs` : F4 | Harmless on GitHub-hosted runners (own PID namespace); only real on a shared self-hosted daemon |

---

## 3. Gate result (actual)

Ran every gate-lane test file in the repo (`node --test` across all `*.test.mjs`) plus every guarded
standalone script in its S0 no-op state, plus the committed pre-commit hook, plus a YAML-syntax check on
the two edited workflow files (the `az bicep`/`actionlint` legs need tooling not installed in this
environment — see note below).

```
node --test $(find . -name "*.test.mjs" -not -path "*/node_modules/*")

ℹ tests 126
ℹ suites 0
ℹ pass 119
ℹ fail 0
ℹ cancelled 0
ℹ skipped 7
ℹ todo 0
```

7 skipped == the 7 deferred findings in §2, one test each, by design. 0 failed.

Standalone guarded scripts (all correctly self-skip — no real contract/DbContext/migrations exist yet
at S0, exactly per contract):

```
node tools/contract-lint/contract-lint.mjs   -> SKIP (contracts/openapi.v0.json doesn't exist yet), exit 0
node tools/i18n-lint/i18n-lint.mjs           -> OK (no client dirs yet, all sub-checks guarded), exit 0
node build/scripts/brand-gate.mjs            -> OK: weeb, friki verified, exit 0
node build/scripts/destructive-verb-check.mjs -> SKIP (no migration path given), exit 0
bash build/scripts/ef-gate.sh                -> SKIP (no DbContext under backend/ yet), exit 0
bash .githooks/pre-commit                    -> secret-scan OK / pre-commit OK, exit 0
```

YAML syntax check on both edited workflow files (`.github/workflows/lints.yml`,
`.github/workflows/infra.yml`) via `yaml.safe_load`: both OK.

**Not run in this environment (no tool installed here; both run in CI on every push per `infra.yml` /
`lints.yml`):**
- `az bicep build` / `az bicep lint` over every `infra/*.bicep` file — `az` CLI is not installed in this
  sandbox. The bicep edits in this remediation (`edge-guard.bicep`) only added array entries and
  `transforms` values inside existing match-condition objects — no new resource types, no new params, no
  structural change — so compile risk is low, but CI's `bicep-build-lint` job is the authoritative check
  and must be green before this lands on `master`.
- `raven-actions/actionlint` on the two edited workflow files — not installed locally; the manual YAML
  parse above catches structural/indentation breaks (the class of bug most likely from a hand-edit) but
  not actionlint's GitHub-Actions-expression-specific checks (e.g. context field typos).

**CI wiring change (required for "now-green" to mean something):** before this remediation, none of the
adversarial lens/adversary test files were wired into `.github/workflows/lints.yml` or `infra.yml` — they
existed on disk but nothing ran them in CI, so a regression on any fixed finding would have shipped
silently. Every lens/adversary file is now added to its corresponding CI job (`contract-lint`, `ddl-lint`,
`destructive-verb-check`, `edge-guard-assertions`) plus two new dedicated jobs
(`concurrency-lens`, `purge-completeness-lens`) so the full adversarial suite runs on every push/PR, not
just locally on request.

## 4. Files touched

**Fixed (code/config changed):**
- `infra/edge-guard.bicep`, `backend/e2e/edge-guard.mjs` — IDOR F1
- `tools/contract-lint/contract-lint.mjs` — IDOR F2, trust-boundary F1/F2, silent-rejection F1/F2/F3, minor F1/F2/F3
- `tools/ddl-lint/ddl-lint.mjs` — PII/residency F1, minor F4
- `tools/ddl-lint/pii-patterns.json` (+ golden vector in `ddl-lint.test.mjs`) — minor F5
- `build/scripts/destructive-verb-check.mjs` — PII/residency F2
- `infra/README.md` — purge-completeness F1/F2/F3
- `.github/workflows/infra.yml` — concurrency F1a/F1b
- `.github/workflows/lints.yml` — concurrency F5, plus CI wiring for every lens/adversary test file

**New test files (lens/adversary suites, previously present but unwired, or newly written for
previously-uncovered deferred findings):**
- `infra/edge-guard.idor-lens.test.mjs`
- `infra/purge-completeness.lens.test.mjs` (findings 4–5 now `Skip`-annotated)
- `tools/contract-lint/contract-lint.idor-lens.test.mjs`
- `tools/contract-lint/contract-lint.minor-lens.test.mjs`
- `tools/contract-lint/contract-lint.silent-rejection.test.mjs`
- `tools/contract-lint/contract-lint.trust-boundary.test.mjs`
- `tools/contract-lint/contract-lint.deferred.test.mjs` (new — PII/residency F3, trust-boundary F3)
- `tools/ddl-lint/ddl-lint.minor-lens.test.mjs`
- `tools/ddl-lint/ddl-lint.pii-residency.adversary.test.mjs`
- `build/scripts/destructive-verb-check.pii-residency.adversary.test.mjs`
- `build/scripts/concurrency-lens.test.mjs` (F2/F3/F4 now `Skip`-annotated; F1a/F1b/F5 green)

**Removed:** `tools/lens-tests/pii-residency.lens.test.mjs` — an untracked, syntactically-truncated
leftover file (cut off mid-import statement) that duplicated content already properly covered by
`tools/ddl-lint/ddl-lint.pii-residency.adversary.test.mjs` and
`build/scripts/destructive-verb-check.pii-residency.adversary.test.mjs`. It was referenced nowhere
(no CI job, no doc) and would have crashed any `node --test` glob run over the tree with a syntax error.

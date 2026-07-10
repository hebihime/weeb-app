# SLICE S0 CONTRACT — repo-ci-iac (LOCKED)

**Gate:** G0 · **Actor:** the system ("a commit builds, tests, and ships ×4 listings + web") · **Kills:** B18 · **Ledger outcome:** green pipeline on empty solution (BUILD.md §7, S0 row).

Synthesized from three architect proposals (P1 simplicity, P2 extraction-grade seams, P3 privacy/i18n/residency). Conflict resolutions and ruling contradictions are logged in §12. This document is the locked contract; changes to it are versioned contract changes per CLAUDE.md.

---

## 0. Scope ruling (the boundary question, decided)

**S0 owns exactly the ledger's units: `.github/`, `infra/`, repo root glue, `.githooks/` — plus the backend toolchain skeleton (`backend/Svac.sln` empty + `backend/Directory.Build.props`), because the ledger outcome "green pipeline on EMPTY SOLUTION" requires a solution to exist.** Zero projects, zero business logic. Everything else inside `backend/` is S1's.

**S0 does NOT create `ios/`, `android/`, or `web/` directories.** P2 proposed build-system shells for all four unit roots; that point loses: the ledger assigns `ios/`+`android/` to S7 (BUILD.md:270) and `web/` to S9 (BUILD.md:272), the web stack is deliberately unratified until S9 Phase 0, and pre-creating flavor files inside another lane's unit violates the parallel-session boundary CLAUDE.md mandates.

**How B18 still dies at S0 (not merely "arms"):** the canonical brand manifest (`brands/*.json`), the ×2-brand CI matrix, and the brand-drift gate all exist and are path-guard-armed **before any client directory does**. The first commit that creates `ios/`, `android/`, or `web/` cannot be green without its flavor axis matching the manifest — the flavorless-first-commit failure path is structurally unreachable. That is the kill, per the CLAUDE.md meta-loop: the constraint precedes the code it constrains.

**No design artifact applies:** S0 has zero user-visible surface. Noted per SLICE_PLAYBOOK, not skipped silently. DESIGN.md is consumed only as the source of ratified brand values.

## 1. Module API surface + OpenAPI delta

**None.** Zero endpoints, zero runtime modules. S0 pins the contract coordinate so lanes B/C/D never renegotiate it:

- **Canonical location: `contracts/openapi.v0.json` at repo root** (CLAUDE.md: root holds contracts; P3's placement, P2's ownership rule). Owned by backend (lane A) via CODEOWNERS; consumed read-only by all three clients (B17).
- Backend CI publishes it as artifact **`openapi-contract-v0`** when the file exists (guarded activation, same pattern as the committed pre-commit hook). Client codegen steps are committed in the client workflows pointing at that artifact name, skip-with-note until it exists. S1 fills the seam without touching CI.
- **`tools/contract-lint/`** (deterministic script, golden-vector tested, no LLM) runs whenever `contracts/openapi.v0.json` exists. Build-failing rule set v0 (P3, all backed by ratified rulings):
  1. **Server-authoritative trust (L20):** no request schema contains `verification*`, `reputation*`, `premium*`, `moderation_state`, `age_estimate`, `trust*`, `tier*`.
  2. **Absence law (token law 3):** no consumer-facing response schema contains `*_locked`, `*_disabled`, `*_gated`, `gate_state`, `locked_reason`, `upgrade_required`. Below a gate the field is ABSENT. Allowlist file requires a ruling citation per entry (sole known exception: create-crew Premium CTA, S27).
  3. **One deny shape (10A / DR-7.3):** every 429/limit response on consumer paths `$ref`s the single shared `LimitReached` component; distinct per-cause deny schemas fail.
  4. **Silent-rejection unobservability (R5/12A-r, T7-A):** deny/void-class operations declare no sender-visible pending/void schemas; unauthorized/excluded consumer reads declare the same 404 shape as nonexistent — no 403 on consumer read paths.
- `contracts/README.md` states these invariants in plain words with ruling citations.

## 2. Schema DDL

**None.** Compose Postgres boots schemaless. S0 ships the schema **gates**:

- **EF-migration CI gate (7A, ratified)** — `build/scripts/ef-gate.sh`, shipped WITH fixture tests, guarded on a DbContext existing:
  - (a) `dotnet ef migrations has-pending-model-changes` per context — model drift without a migration fails the PR (P1);
  - (b) idempotency: apply the full chain to a fresh throwaway Postgres+PostGIS container, apply again, assert no-op (P2);
  - (c) destructive-verb grep (`DROP TABLE`, `DROP COLUMN`, `TRUNCATE`) fails unless the migration carries an explicit `-- destructive: <reason>` marker (P3 — silent column drops are how region/lawful-basis and consent columns die in refactors).
- **DDL residency lint (L21)** — `tools/ddl-lint/` + `pii-patterns.json` seeded now (`*consent*`, `*profile*`, `*identity*`, `*location*`, `*verification*`): any matching table lacking `region` + `lawful_basis` columns fails unless allowlisted with a stated reason. Committed at S0 with golden vectors against fixture DDL; first real consumer is S1. B2 logic applied to residency: the gate must predate the first feature store.

## 3. 4A policy entries

**None — zero mutation endpoints exist.** The `policy-matrix` arch-suite CI slot is committed now, guarded on `backend/tests/Svac.Tests.Architecture` (which S1 lands), in both the pre-commit hook (block 2, already committed) and backend CI. Guarantee: S1 cannot land its first endpoint ungated. B1 stays S1's kill.

## 4. 9A config entries

**None at runtime — and a locked rule (P1+P2 over P3, see §12):** brand identity is **build-time only, forever**. It lives in `brands/*.json` and each toolchain's idiomatic flavor mechanism; it never migrates into the 9A registry. A runtime brand swap being structurally impossible is 7A's intent. 9A is runtime-tunables only.

Root single-source files S0 creates (build-time facts, not registry entries):

| File | Content | Notes |
|---|---|---|
| `brands/weeb.json`, `brands/friki.json` | `{brand_key, display_name, bundle_id_ios, application_id_android, web_domain, brand_primary, brand_celebration, wordmark_asset_ref, string_pack_id, store_metadata_path, store_listing_refs}` | THE canonical flavor source (7A drift mitigation, eng review risk table). Hexes locked to DESIGN.md:109-110 ratified values — Weeb: primary `#F7568F` (Bubblegum), celebration `#FF9838` (Mikan); Friki: primary `#FF7A3D` (Tangerine), celebration `#F7568F`. Bundle IDs blank pending OQ-1. |
| `i18n/locales.json` | `["en","es","pt","zh-Hans"]`, default `"en"` | T1.1-A consciously-confirmed set; changing it is a ruling. Fallback only — device-region localization is client-side per DR-7.7. S1 may mirror into 9A read-only if a runtime consumer appears; that is S1's call, not an open question. |
| `infra/params.{dev,staging,prod}.bicepparam` | 7A env map incl. single `location` parameter | See §8 residency seam. Region value pends OQ-2. |

**Drift enforcement:** `build/scripts/brand-gate.mjs` (gate-test lane, <2s, golden-vector tested) asserts every platform flavor file (xcconfig / gradle productFlavors / web env / fastlane metadata) matches the canonical JSON value-for-value and that all four listing identities are distinct and complete. **Lint-verify, not generate** (P2's call, ratified by the vanilla rule — generation fights three toolchains for zero gain). CI additionally fails any flavor build that reads a brand literal from outside `brands/` (grep rule).

## 5. 10A quota keys

**None.** No actor-facing consumable action. CI concurrency caps are workflow YAML platform config, explicitly not 10A.

## 6. 13A store registrations (and 3A)

**None — S0 creates zero data stores, emits zero events.** B2-safe by construction: nothing predates the 13A CI gate S1 lands. Two explicit non-stores recorded in `infra/README.md` so the S1 purge-registry review has a citation instead of an unexamined gap:
- CI artifacts/logs: GitHub-retained, no user data; workflow policy forbids uploading DB dumps or user-shaped fixtures as artifacts; artifact retention 30 days; OIDC only, zero long-lived cloud credentials in GitHub.
- docker-compose dev volumes: dev-only, destroyed by `down -v`, exercised by the fresh-boot clause.

The 13A gate's CI job slot is reserved in backend CI now; S1 lands the gate itself before its first feature store.

## 7. Notification taxonomy rows

**None; closure rule satisfied vacuously and asserted, not assumed.** No user-visible state change exists (the actor is the system). Pipeline/deploy failures are operator-facing GitHub-native notifications, outside the nine-category consumer taxonomy (S4). Stated so the S4 closure lint starts from a clean baseline.

## 8. Evals

**N/A with reason:** S0 contains zero latent-space surface (BUILD.md §0 enumerates all four; none is here). Every S0 artifact is deterministic-space; tests are the whole quality story: brand-gate golden vectors, ef-gate fixture tests, contract-lint golden vectors, ddl-lint golden vectors, i18n-lint golden vectors, compose health asserts, actionlint, hook secret-scan cases.

## 9. Concrete deliverables (full inventory)

### Workflows — per-unit files, not one monolith (P2 over P1, see §12)

`.github/workflows/`: `backend.yml`, `ios.yml`, `android.yml`, `web.yml`, `infra.yml`, `lints.yml`, `release.yml`. Each path-filtered to its unit so parallel sessions never edit the same workflow file, and required checks map 1:1 to units. Every leg directory-guarded (the committed pre-commit hook's own activation pattern) so the pipeline is **green on the empty repo today** and each later slice lights its own legs with zero workflow edits.

| Workflow | Jobs |
|---|---|
| `backend.yml` | restore/build/test on `backend/Svac.sln` → arch-suite slot (guarded on `backend/tests/Svac.Tests.Architecture`) → EF gate (guarded on DbContext) → 13A-gate slot (S1) → `openapi-contract-v0` artifact publish (guarded) → container image (guarded on a host existing) |
| `ios.yml` | matrix `brand ∈ {weeb, friki}`; `xcodebuild test` per flavor; string-catalog lint slot; brand-smoke Maestro slot ×2 (activates S7, 14A); codegen step pinned to `openapi-contract-v0`, skip-with-note |
| `android.yml` | matrix `brand ∈ {weeb, friki}`; `./gradlew test<Flavor>DebugUnitTest`; same slots as iOS |
| `web.yml` | build both brand artifacts; `npm run --if-present gate` (survives any S9 stack choice); prod-build h1/LCP static assertion slot (§8 clause 3, activates S9) |
| `infra.yml` | `az bicep build` + linter over `infra/**` (credential-free, runs day one) → `what-if` against dev (guarded on Azure OIDC configured) → deploy only via manually-dispatched, environment-gated job |
| `lints.yml` | brand-drift gate · contract-lint · ddl-lint · i18n-lint · secret scan (same patterns as pre-commit block 1, so a bypassed local hook is still caught — P3) · actionlint on the workflows themselves · compose-smoke: `up -d --wait` → health asserts → `down -v` → fresh `up` → zero-error/zero-restart log sweep |
| `release.yml` | per-brand fastlane lanes → TestFlight internal ×2 + Play internal ×2, **only** inside a protected `release` GitHub environment. Secret posture (synthesis of P1/P2, see §12): normal CI never attempts uploads (green on a fork with zero secrets); inside the `release` environment a missing signing/store secret **hard-fails** — never silently skips (L18 fail-closed at the money/store door). Fastlane is the sole store-upload surface (buy, per 7A ruling text); swap = one lane file. |

CI test parallelism capped at 4 (§8 clause 6), recorded in workflow env, re-measured at S1.

### `infra/` Bicep (2A verbatim)

- Modules, one per Azure service (any 2A exit ramp = one module swap): `container-apps-env`, `postgres-flexible` (PostGIS enabled), `redis`, `signalr`, `keyvault`, `blob-cdn`, `log-analytics`; `main.bicep` + `params.{dev,staging,prod}.bicepparam`.
- **`edge-guard.bicep` (L17, S0's half per BUILD.md:374):** ingress normalization-and-reject rules — `%2e`/`%2f`/`%5c`, dot segments, `/internal` reach-through → 404 **before** any rewrite — with unit assertions on the rendered rules. Companion `backend/e2e/edge-guard.mjs` adversarial reachability script committed now, self-skips until a public host answers; mandatory at S9 (S9's half).
- **Residency (L21, P3):** single `location` parameter per environment; ALL data-bearing resources (PostgreSQL, Blob, SignalR, Key Vault) resolve from it; an IaC test fails any data-bearing resource declaring its own location literal. Geo-redundant backup pairing constrained to an in-jurisdiction pair-allowlist (for `westeurope` → `northeurope`, both EU), asserted in the test, not left to memory.
- **Key Vault posture (P3):** soft-delete + purge protection ON (crypto-shredding of special-category field keys is a deliberate 13A purge verb later, never an accident); app identities crypto-user RBAC only, no key export; key name **`field-enc-special-category-v1`** reserved now so S10's envelope encryption has a home. No default values on any secret-typed Bicep parameter — unset secret fails deployment (IaC twin of fail-closed).

### Root glue

- `docker-compose.yml`: postgis, redis, azurite, mailpit, self-hosted SignalR (dev; Azure SignalR exit ramp already a TODOS item) — the §8 clause-2 stack, all with healthchecks; hosts join at S1. Fresh-boot clause applies from S0.
- `brands/weeb.json` + `brands/friki.json` (§4), `i18n/locales.json`, `.editorconfig`, root `.gitignore` (secrets-safe: `.env*`, signing keys, `*.jks`, `*.p8`, `xcuserdata`), **CODEOWNERS** mapping each unit root + `contracts/` to its lane (the parallel-session boundary made mechanical — P2), README build/hook instructions (`git config core.hooksPath .githooks`).
- `backend/Svac.sln` (zero projects) + `backend/Directory.Build.props` (TFM, nullable, analyzers, `Svac.*` root namespace).

### Deterministic tools (each with golden-vector tests, gate lane)

`tools/contract-lint/` (§1) · `tools/ddl-lint/` (§2) · `tools/i18n-lint/` (below) · `build/scripts/brand-gate.mjs` (§4) · `build/scripts/ef-gate.sh` (§2).

**i18n-lint (P3; keyed strings ×4 from commit one, machine-checked not remembered):** formats stay platform-idiomatic (`.xcstrings`, `values-<locale>/strings.xml`, `.resx`, web per S9); the shared lint activates per unit directory as it appears and enforces: (1) key parity across all four locales of `i18n/locales.json` — a missing `zh-Hans` string fails the build, no TODO state exists; (2) hardcoded user-facing string tripwire per platform (SwiftUI/Compose/Razor literal regexes, allowlist requires justification); (3) brand string-pack overrides must exist in the base catalog and in both brands — a Friki string can never silently fall through to Weeb voice; (4) fastlane store metadata dirs (4 listings × 4 locales) are in scope — an EN-only listing fails that brand's leg. Advisory now, hard at S1: no pre-localized `*_display` date/number strings in response schemas (DR-7.7 — canonical values server-side, clients localize).

### `.githooks/pre-commit`

Already committed at a95edb5. S0 verifies the executable bit, wires the README, and honors the hook's own header contract: measured runtime recorded when S1 lands.

## 10. Dependency classification (every not-yet-built system S0 touches)

| Dependency | Class | Handling |
|---|---|---|
| Backend projects + arch tests (S1) | **swap-safe** (guarded activation) | All backend legs + hook block 2 green vacuously until S1; the gates (EF, 13A slot, 4A slot, arch slot, ddl-lint) are wired at S0 so S1 cannot land its first store/endpoint ungated |
| OpenAPI contract v0 (S1) | **seam-now** | `contracts/` coordinate + `openapi-contract-v0` artifact name + contract-lint pinned; client codegen committed, skipping. Never stub a fake contract |
| `ios/`, `android/` (S7) | **swap-safe** | Matrix, brand gate, i18n lint, Maestro slots, release lanes already waiting; flavorless first commit is red by construction |
| `web/` + stack decision (S9 Phase 0) | **swap-safe / seam-now** | `--if-present` gate + toolchain-neutral leg; S9 specializes the gate script for the ratified stack, zero workflow rework |
| Azure subscription / Founders Hub (2A) | **must-build-first for the deploy stage only** | Bicep compiles/lints credential-free; what-if activates on OIDC; deploy environment-gated. Founders Hub application is a named G0 founder task (OQ-3) |
| Apple Developer + Play Console ×2 listings, D-U-N-S, signing (fastlane match) | **seam-now, external lead time flagged** | Build/test/lint lanes green without them; upload lives only in the `release` environment where absence hard-fails. NOT a B18 blocker — B18 dies via flavors-gated-from-first-commit, not store upload. Start account creation now (OQ-3) |
| Self-hosted SignalR dev container | **swap-safe** | Compose service; Azure SignalR exit ramp = one Bicep module + one compose entry |
| Maestro harness (S7, 14A) | **seam-now** | Brand-smoke job slots ×4 reserved, skip-with-note |
| Font Awesome Pro registry token | **seam-now** | Secret slot named once here so S7/S9 don't invent divergent ones |
| Config registry 9A (S1) | **seam-now** | Root single-source files are build-time truth; no duplicate runtime truth ever created (§4 rule) |
| MUST-BUILD-FIRST (blocking S0 itself) | **none** | S0 is the ledger root; that emptiness is the proof it can start today |

## 11. Outcome moved + where it renders

**Green pipeline on empty solution**, measured at S0 sign-off:
1. All seven workflows green on the S0 commit, **twice consecutively** (§8 clause 6 discipline applied to CI itself).
2. `bicep build` clean over all three env param sets; edge-guard and residency assertions pass.
3. Compose fresh-boot: `down -v` → `up`, zero errors, zero restarts.
4. All lint tools green with golden-vector suites passing; pre-commit hook live.
5. Branch protection requiring the per-unit checks on `master` (Julien-executed, §13).

B18 dead: no client codebase's first commit can be green without its flavor axis matching `brands/*.json`. Rendering surface until the Metrics & Ops desk exists (S5): the Actions status matrix + required-checks config; at S5, pipeline-green-rate becomes a desk tile. BUILD.md §1 local-facts line gets actual paths/ports/runtimes recorded when this lands, as it instructs.

## 12. Synthesis decisions log (conflicts resolved, contradictions noted)

1. **Client unit shells (P2) — REJECTED.** Contradicts the ledger's unit-ownership columns (`ios/`/`android/` are S7's units, `web/` is S9's; BUILD.md:270,272) and the parallel-session boundary rule. B18 is killed structurally by manifest + gate + matrix preceding any client dir (§0). P2's valid kernel — the contract coordinate, codegen seams, CODEOWNERS, OIDC — is grafted.
2. **`backend/Svac.sln` skeleton (P1) — ADOPTED; P2's placeholder xunit project — REJECTED.** The ledger outcome says "empty solution" verbatim; an empty sln exercises restore/build/test, and a throwaway project would be deleted at S1.
3. **Workflow layout: per-unit files (P2) over single ci.yml (P1).** One monolithic workflow is a merge-conflict magnet across parallel lanes and muddies required-checks-per-unit; per-unit files make CODEOWNERS meaningful for CI itself.
4. **Store-upload secret posture: P1 ∧ P2 merged.** They looked contradictory (fail-hard vs skip-with-note) but compose: normal CI never runs upload jobs (green with zero secrets, P2); the `release` environment hard-fails on absent secrets (L18 fail-closed, P1). Both invariants hold simultaneously.
5. **`platform.brands` as a reserved 9A key (P3) — LOSES automatically.** Contradicts 7A's ratified intent: brand is a build flavor; runtime brand swap must be structurally impossible. Brand values are build-time files forever (P1+P2). P3's other reserved values survive as root single-source files, not 9A reservations.
6. **Brand config: lint-verify, not generate (P2)** — vanilla rule; generation fights three toolchains for zero gain.
7. **Contract location: root `contracts/` (P3) with backend ownership (P2)** — CLAUDE.md places contracts at root; CODEOWNERS assigns lane A. P1's `backend/openapi.json` path superseded.
8. **P3's three lint gates (contract-shape, ddl-residency, i18n parity) — GRAFTED whole.** Each is a one-time deterministic script whose absence is only ever discovered as a rewrite; all trace to ratified rulings (L20, L21, DR-7.3, T1.1-A, token law 3). They are guarded-activation, so S0 stays green on the empty repo.
9. **L17: edge-guard module + e2e skeleton (P1/P3) over rewrite-grep lint (P2).** "No rewrites exist yet" is vacuous; reject-before-rewrite rules with rendered-rule assertions are the real seam. The e2e script self-skips until S9.
10. **EF gate = union of all three** (pending-model-changes + idempotent-reapply + destructive-verb marker); no conflict, pure graft.
11. **Azure OIDC federation, zero long-lived cloud creds in GitHub (P2/P3) — ADOPTED** as the CI identity posture.

## 13. Julien-executed actions (settings list, not questions)

- Branch protection on `master` requiring the per-unit workflow checks.
- GitHub environments `dev`/`staging`/`prod`/`release`; Azure OIDC federation.
- ASC API key + Play service-account secrets into the `release` environment when accounts exist; Font Awesome Pro token slot.

## 14. OPEN QUESTIONS (genuinely Julien's — permanent values and external authorizations only)

- **OQ-1 — The four permanent store identities + web domain confirmation.** Bundle/application IDs are unchangeable after first store upload. Proposal on the table (P2): `app.weeb.ios` / `app.friki.ios`, `app.weeb.android` / `app.friki.android`, domains `weeb.app` / `friki.app`, plus the four store listing names. Seeds `brands/*.json`; needed before release lanes are exercised, not before S0 is green.
- **OQ-2 — Primary Azure region (permanent-leaning, one value since 1A means one Postgres).** (a) `westeurope` — recommended: EU jurisdiction for the strictest user base, in-EU backup pair `northeurope`, acceptable Iberia + worldwide latency; (b) `spaincentral` — best Friki latency, but verify Container Apps/SignalR SKU availability and its backup pair first; (c) a US region — favors Weeb's US con circuit but buys the EU-data-transfer problem before the lawful-basis map (L-1) exists. Recommendation: (a); revisit only with counsel at L-1.
- **OQ-3 — Green light to start external applications now:** Apple Developer Program (D-U-N-S), Play Console, both brands' store listings, Azure Founders Hub. Real external lead time; CI is skip-with-note until they exist. Authorization to act, not a design fork.

No other forks: fastlane is ratified in 7A's text, lint-vs-generate is decided by the vanilla rule, the web stack is S9 Phase 0's decision, and the scope boundary (§0) is resolved by the ledger itself.

## 15. RATIFICATION — 2026-07-10

Ratified as written (delegated: "review the contract it writes, ratify, continue"). None of the OQs
blocks S0 green; build proceeds with these interim postures, each reversible until Julien answers:

- **OQ-1 (store identities):** OPEN for Julien. `brands/*.json` ships with bundle/application IDs blank
  exactly as §4 specifies; brand-gate treats blank-pending-OQ-1 as valid until the release lanes are
  first exercised, then hard-fails on blank.
- **OQ-2 (Azure region):** build with the recommendation, `westeurope` (+ `northeurope` backup pair), as
  the value in all three bicepparam files, marked `// pending OQ-2 ratification`. Reversible: deploys are
  environment-gated and OIDC is not configured; nothing provisions until Julien confirms.
- **OQ-3 (external applications):** Julien's action, listed in §13's lane. CI stays skip-with-note.

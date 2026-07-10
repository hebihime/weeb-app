# SLICE S1 CONTRACT — domain-substrate (LOCKED)

**Gate:** G0 · **Actor:** the system ("pillars stand; OpenAPI v0 emits") · **Kills:** B1 B2 B3 B12 B17 · **Ledger outcome:** arch suite enforces every pillar; contract v0 published (BUILD.md §7, S1 row).

Synthesized from three architect proposals (P1 simplicity, P2 extraction-grade seams, P3 privacy/i18n/residency). Conflict resolutions and ruling contradictions are logged in §12. This document is the locked contract; changes to it are versioned contract changes per CLAUDE.md.

---

## 0. Scope ruling (the boundary questions, decided)

**S1 owns `backend/domain-core/**`, `backend/tests/Svac.Tests.Architecture/**`, the first emission of `contracts/openapi.v0.json`, and ONE minimal public API host at `backend/public-host/Svac.PublicApi`** (path follows the ledger's `backend/admin-host` naming convention). The host is in scope not because the ledger names it but because three of S1's five kills are impossible without it: B17 needs the contract actually emitted and lint-gated; B1 needs a live 4A middleware refusing a real HTTP mutation; S0 §9's compose clause needs `/health` 200. The host contains zero business logic: health, one bootstrap GET, the 4A middleware, OpenAPI emit.

**P3's three-host proposal loses automatically:** `backend/admin-host` is S5's unit and the partner host is S29's per the ledger; pre-creating them violates unit ownership and the parallel-session boundary. The valid kernel is grafted as **`Svac.DomainCore.Hosting`** (P2): one middleware package (request context, 4A chokepoint, error shape) that all three hosts must mount — enforced by arch test — so the chokepoint is built once and the later hosts cannot ship without it.

**S1 does NOT:** build any feature module (`backend/modules/*` are S2+), seed the 9A v0 batch (S5's ledger row carries "v0 batch seeded" — S1 ships the mechanism and manifest format so S5 seeds data, not code), register any consumer-facing quota key, ship any consumer mutation endpoint, touch clients, or use Redis (Postgres does everything S1 needs; Redis stays an idle compose service until a slice proves a hot path).

**Simplicity doctrine (P1, ratified for the whole slice):** pillars are namespaces, not projects; streams are tables generated from one shape, not six designs; registries are code the arch test enumerates, not CRUD. Wherever the eng review permits either a mechanism or a product, S1 ships the mechanism and the named later slice ships the product.

**Design artifacts:** no user-visible surface exists — noted per SLICE_PLAYBOOK, not skipped silently. DESIGN.md is consumed as contract constraints only: token law 4 / DR-7.3 (one limit surface → the single `LimitReached` component), token law 3 (absence law → `DenyAsAbsence` policy mode), token law 2 (one generic error → one `Problem` shape + shared message keys), DR-7.7 (canonical values, clients localize).

**Privacy thesis (P3, adopted as a design principle of this slice):** S1 is the only moment the privacy invariants can be made structural. Region/lawful-basis, silent-deny semantics, absence-as-server-truth, and crypto-shredding land in the type system and CI gates here, or every later slice re-litigates them per endpoint.

## 1. Module API surface + OpenAPI delta

### 1a. Assemblies (1A minimum; no per-pillar pre-split — see §12.2)

- **`Svac.DomainCore.Contracts`** (public): the ONLY assembly future modules may reference (arch test: cross-boundary references hit `*.Contracts` only). Interfaces, DTOs, policy-table types, typed ids.
- **`Svac.DomainCore`** (internal, `InternalsVisibleTo` its tests): EF `CoreDbContext` (schema `core`), pillar implementations, purge worker, projection runner.
- **`Svac.DomainCore.Deterministic`** (pure): con-day window math, quota reset boundaries, ledger summation, opaque-id codec. Zero IO references (arch-tested), golden vectors, no wall-clock reads inside functions, no LLM ever. This is where CLAUDE.md's deterministic-space rule lives for the whole backend.
- **`Svac.DomainCore.Hosting`**: middleware package every host mounts (request-context construction, 4A enforcement, Problem/LimitReached serialization). Modules never read `HttpContext` (arch test).
- **`Svac.PublicApi`** (host), **`Svac.Tests.Architecture`**, **`Svac.Tests.DomainCore`**, plus `backend/e2e/substrate.e2e.mjs`.

### 1b. Public contract surface (signatures load-bearing)

```
// Typed opaque ids (P3): prefixed ULIDs — usr_, stf_, prt_, sys_, evt_, led_ …
// ActorRef carries kind in the prefix; no raw Guid/uuid crosses a module boundary or the API.

// 3A — one substrate, six typed streams
enum StreamType { Ledger, Reputation, Consent, Behavioral, Audit, HeatmapProvenance }
IEventStore {
  Append(StreamType, streamId, eventType, payload, RequestContext, ExpectedVersion)  // joins the ambient EF tx: the substrate IS the outbox (§9); same-tx or it does not exist
  Reverse(eventId, reason, ctx)          // appends a reversal_of entry, never mutates
  Tombstone(eventId, purgeClass, ctx)    // the ONLY sanctioned update path (payload → NULL, flag set)
  ReadStream(StreamType, streamId, fromSeq)
  Replay(StreamType, IProjection, fromWatermark)
}
IProjection { Handles(eventType): bool; Apply(event); }   // foreign-event skip is STRUCTURAL:
// Replay advances the per-consumer watermark even when Handles()==false (§8 clause 7 harness ships with it)

// 4A — the mutation chokepoint
IPolicyEngine { Authorize(ActorRef, action, TargetRef): PolicyDecision }
// PolicyDecision is a CLOSED UNION (P3): Allow | DenyAsAbsence | DenySilentAs404 | DenyAsLimit(quota_key) | DenyStandard(reason_key)
// The deny MODE is a column of the declarative table, not per-endpoint judgment. DenyStandard is legal
// only for staff/partner actor kinds; an arch test fails any policy entry mapping a consumer actor to
// DenyStandard on a read path (contract-lint invariant 4 made source-level).
// PolicyTable: checked-in typed C# table {action, actorKinds, axes(premium/reputation/mode/verification),
// denyMode, requires_reason}; CI generates the action×axis matrix suite FROM this same table.

// 9A — typed config
IConfigRegistry { Get<T>(key): T; Set(key, value, reason, ctx) }
// Get: typed, bounds-validated. Set: staff/system only via 4A, appends an Audit-stream event in the SAME tx.
// Seed loading: additive manifest file per module (union-merge friendly) — S5 seeds the v0 batch as data.

// 10A — one quota verb, one deny shape
IQuotaService { Consume(ActorRef, quotaKey, QuotaContext): Consumed | LimitReached }
// Cap pipeline: base cap (9A) × ICapModifier[] — premium / reputation-tier / mode modifiers are identity
// functions at S1 (seams; real impls S23/S16/S19). The deny branch maps 1:1 to the LimitReached OpenAPI
// component: there is no second deny type to render (token law 4 structural).
// Window math in Deterministic: windowKey(quotaKey, reset{daily|weekly, con-local|user-local}, tz, con_day_cutoff)

// Ledger (quest-ready day one, questsystem §Day-One verbatim)
ILedger { Append(LedgerEntry), Reverse(entryId, actor, reason), BalanceOf(userId): {points, xp, svac} }
// Typed appends on the Ledger stream + one balance projection. xp = points 1:1 enforced at append AND by CHECK;
// svac accrues only on enumerated event types (empty enumeration at S1); sink_purchase = negative svac only;
// points/xp never negative; balances derive by summation — no mutable balance column exists anywhere.

// 13A — compile-time purge registry
[PurgeRegistration(store, perClassVerbs...)]  // verbs: Delete | Tombstone | CryptoShred | NotApplicable(reason)
IPurgePipeline { Run(purgeClass, SubjectRef): PurgeReport }   // ONE pipeline iterating the registry; the only
// executor of purge verbs; emits an Audit-stream event + a purge_run row per run.
// Registry compiled + emitted as backend/domain-core/purge-registry.json so the CI gate diffs EF surface vs registrations.

// Cross-cutting
RequestContext { ActorRef, ActorKind: anonymous|user|staff|partner|system, Region: RegionCode,
                 RegionSource: declared|signup|edge_inferred|system, LawfulBasisVariant, Locale }
// Built by Hosting middleware before any module code runs; flows via IRequestContextAccessor.
// Region is NEVER client-settable (L20). System-actor writes inherit the SUBJECT's region
// (a purge run on a German user's data is EU-scoped work); pure-system rows use region='ZZ', lawful_basis='n/a'.

IFieldEncryptor { Protect/Unprotect(purpose, plaintext); Shred(purpose, subjectScope) }
// Purpose-bound over .NET Data Protection + Key Vault envelope (key field-enc-special-category-v1, reserved
// at S0). Purposes are a CLOSED enum seeded now: special_category, birthdate, verification_audit,
// identity_exclusion_filters. Shred is a first-class verb: crypto-shredding is a deliberate 13A verb, never
// an accident. Ships with an EF value converter; first real consumer S10.
IFieldKeyVault  // vendor seam: Key Vault prod, local dev keyring under DevSeams; prod without Vault THROWS at startup.

IBehavioralStream { Emit(eventName, payload, ctx) }  // the one door onto the behavioral stream; every slice's
// "metric wired at build time" goes through it, so "verified received" is one integration-test pattern forever.

IPaymentService   // stub contract only (B12). DevSeams seed impl; NEVER DI-registered in prod — prod resolution
// of an unconfigured IPaymentService throws at startup (L18 fail-closed, two slices before money exists).

IRegionResolver   // seam-now: account-declared (S10) > signup capture (S3) > edge-inferred fallback, with
// RegionSource provenance recorded so upgrading resolution never rewrites history. DevSeams deterministic impl.
```

**RegionCode granularity — decided, not open:** ISO-3166-1 country + optional subdivision, always-when-known. The T2r jurisdiction table needs `US-TX`-class rows by ratified spec; country-only is insufficient and the column is a one-way door (present from row one vs a backfill later).

**Lawful basis:** resolved from a code table keyed `(stream/store, event_type, region)` with the VARIANT selected by 9A config (`privacy.lawful_basis_map_variant`) — BUILD.md §9's "L-1 fallback variants deployable by config" made real. Default: conservative GDPR-grade posture applied globally. Counsel's L-1 map lands as a new variant + audited config flip, no deploy.

**`DevSeams` is an environment/deployment flag, NOT a 9A entry** — a runtime-tunable that swaps fake payment/crypto backends from the ops desk must be structurally impossible (same logic as S0's brand ruling). An arch test proves DevSeams impls are never referenced from prod DI composition.

### 1c. OpenAPI contract v0 (B17 dies)

First real emission to `contracts/openapi.v0.json` (committed; drift-gated: CI regenerates + `git diff --exit-code`); all four S0 contract-lint invariants run un-skipped for the first time. The `openapi-contract-v0` artifact leg activates with zero workflow edits, as S0 designed.

- **Shared components (the real payload of v0, pinned so clients generate once):**
  - **`LimitReached`** `{ quota_key, message_key, resets_at (canonical timestamptz — client localizes per DR-7.7), premium_extends: bool }` — THE single 429 deny component every future limit `$ref`s. No cause taxonomy that could distinguish tier floors from freemium caps (design 5.16: identical rendering); `premium_extends` is a render hint for the one surface's optional CTA, not a cause.
  - **`Problem`** — RFC 9457 shape with `message_key` + correlation id, never localized prose (token law 2).
  - `OpaqueId` (string format), `CursorPage` (pagination pinned early), `HealthStatus`.
  - `securitySchemes.bearer` declared placeholder; S3 fills semantics. No trust-shaped property exists in any request schema (contract-lint rule 1 goes from vacuous to live).
- **Paths:** `GET /health` (liveness, compose healthcheck target) · `GET /v1/client-config` → `{ api_version, locales, default_locale }` sourced from `i18n/locales.json` at boot. **No 9A mirror of locales is created** — one truth stays the file (S0 §4 delegated this call to S1; the mirror + drift-test pattern is recorded for the slice that first has a runtime consumer, S4). **Zero mutation paths**; the 4A refusal proof runs against a test-host-only canary endpoint, never shipped in the contract.
- **i18n law hardened (S0's own escalation note executed):** the `*_display` pre-localized-values lint is promoted from advisory to build-failing now. **`contracts/message-keys.json`** manifest published beside the contract — the canonical registry of server-emitted keys; S1 seeds exactly the substrate set (`limit_reached.generic`, `error.generic`, `error.could_not_send`). i18n-lint extends (guarded activation, S0 pattern): every manifest key must exist ×4 locales in each client catalog once client catalogs exist.

## 2. Schema DDL (schema `core`, owned solely by CoreDbContext; first real consumer of `ef-gate.sh` and `ddl-lint` — record their measured runtimes per the hook header contract)

**Table-per-stream (P2+P3 over P1's single table):** six physical tables of ONE common shape, generated from one definition (deterministic space) — `core.events_ledger`, `events_reputation`, `events_consent`, `events_behavioral`, `events_audit`, `events_heatmap_provenance`. Per-stream retention, purge classes, volume (behavioral is orders of magnitude hotter than consent), and residency posture differ; any stream can be re-stored independently; and the names trip ddl-lint's `*consent*` pattern so the residency gate actually bites.

```sql
CREATE SCHEMA core;

-- ×6, one generated shape:
CREATE TABLE core.events_<stream> (
  event_id      text PRIMARY KEY,           -- evt_ ULID
  stream_id     text NOT NULL,              -- subject scope (opaque ref)
  seq           bigint NOT NULL,            -- per-stream_id; optimistic append (ExpectedVersion)
  event_type    text NOT NULL,
  payload       jsonb,                      -- NULL only when tombstoned
  reversal_of   text REFERENCES core.events_<stream>(event_id),
  tombstone     boolean NOT NULL DEFAULT false,
  actor_ref     text NOT NULL,
  region        text NOT NULL,              -- L21, stamped from RequestContext
  lawful_basis  text NOT NULL,              -- L21, resolved per §1b
  occurred_at   timestamptz NOT NULL,
  recorded_at   timestamptz NOT NULL DEFAULT now(),
  UNIQUE (stream_id, seq)
);
-- Append-only enforced IN-DATABASE, not by convention: constraint trigger permits INSERT always;
-- UPDATE only the (tombstone=false→true, payload→NULL) transition; DELETE never. DELETE grant
-- additionally revoked from the app role.

CREATE TABLE core.projection_checkpoints (
  consumer_id text NOT NULL, stream_type text NOT NULL,
  watermark_seq bigint NOT NULL, updated_at timestamptz NOT NULL,
  PRIMARY KEY (consumer_id, stream_type)
);

CREATE TABLE core.ledger_entries (          -- questsystem §Day-One verbatim + region/lawful_basis (additive)
  id text PRIMARY KEY,                      -- led_ ULID; 1:1 with an events_ledger row
  user_id text NOT NULL, crew_id text,
  event_type text NOT NULL,
  points integer NOT NULL CHECK (points >= 0),
  xp integer NOT NULL CHECK (xp = points),  -- 1:1 law in the database, not just the validator
  svac integer NOT NULL,                    -- negative allowed: sink_purchase only
  quest_id text, evidence_ref text,
  region text NOT NULL, lawful_basis text NOT NULL,
  created_at timestamptz NOT NULL,
  reversal_of text REFERENCES core.ledger_entries(id)
);  -- same append-only trigger; balances rebuild via Replay

CREATE TABLE core.ledger_balances (         -- projection, rebuildable; summation is the truth
  user_id text PRIMARY KEY, points bigint, xp bigint, svac bigint,
  watermark bigint NOT NULL, updated_at timestamptz
);

CREATE TABLE core.config_entries (
  key text PRIMARY KEY, type text NOT NULL, value jsonb NOT NULL,
  scope text NOT NULL CHECK (scope IN ('founder','ops','set')),
  gate text, bounds jsonb, requires_reason boolean NOT NULL DEFAULT false,
  updated_at timestamptz NOT NULL, updated_by text NOT NULL
);  -- non-personal by construction; every Set emits an events_audit row in-tx

CREATE TABLE core.quota_counters (
  actor_ref text NOT NULL, quota_key text NOT NULL, window_key text NOT NULL,
  consumed integer NOT NULL DEFAULT 0, updated_at timestamptz NOT NULL,
  PRIMARY KEY (actor_ref, quota_key, window_key)
);  -- Consume = single atomic UPSERT ... WHERE consumed < cap (idempotent-under-race, §9).
    -- Plain Postgres, NOT Redis: transactional with the guarded action, one moving part fewer.
    -- Promotion path if a hot path is ever proven: one DI/store swap behind IQuotaService.

CREATE TABLE core.purge_runs (
  id text PRIMARY KEY, purge_class text NOT NULL, subject_ref text NOT NULL,
  store_key text NOT NULL, rows_affected integer NOT NULL,
  started_at timestamptz NOT NULL, completed_at timestamptz, evidence jsonb
);

CREATE TABLE core.data_protection_keys ( ... );  -- .NET keyring, encrypted at rest; 13A-registered with CryptoShred
CREATE TABLE core.field_key_refs (
  field_key_id text PRIMARY KEY, vault_key_name text NOT NULL,
  created_at timestamptz NOT NULL, retired_at timestamptz
);  -- seeded with field-enc-special-category-v1; no key material ever in Postgres
```

**ddl-lint extension (P3, one-line + golden vector):** `tools/ddl-lint/pii-patterns.json` gains `*events_*` and `*ledger*` so the lint enforces what this contract promises. `region`+`lawful_basis` go on **all six streams uniformly**: every stream can carry personal data, and a uniform invariant is testable where a selective one erodes. `region='ZZ'` / `lawful_basis='n/a'` system rows are allowlisted with that stated reason. Destructive-verb marker: unused (greenfield chain starts here).

**The 4A gate table and the 13A registry are code, not DDL** (4A ruling text: "the premium matrix as code"; L29 union-merge). No CRUD surface exists for either.

## 3. 4A policy entries (deny-by-default; the table ships non-empty)

Zero consumer mutation endpoints → zero consumer entries, **and the generated matrix suite asserts exactly that** (a consumer-reachable mutation at S1 is a contract violation, not a gap). The substrate's own internal verbs get real rows so the engine is exercised by real consumers, not vacuously:

| action | actor kinds | axes | denyMode | requires_reason |
|---|---|---|---|---|
| `core.config.set` (ops scope) | system (seed loader); staff: SuperAdmin, EconomyOps | role | DenyStandard | true |
| `core.config.set` (founder scope) | system (seed loader); staff: SuperAdmin | role | DenyStandard | true (dangerous-edit interstitial at S5) |
| `core.ledger.append` | system only — never client-reachable; structural: entry prevents an ungated route ever shipping | — | DenyStandard | false (event carries provenance) |
| `core.ledger.reverse` | system (spot-check pipeline); staff: SuperAdmin, EconomyOps | role | DenyStandard | true — reversal is the ONLY correction verb; data surgery has no policy entry, hence impossible |
| `core.event.tombstone` | system (purge pipeline ONLY) | — | DenyStandard | true |
| `core.purge.execute` | system (scheduler); staff: SuperAdmin (manual DSR) | role | DenyStandard | true |
| `core.quota.consume` | internal chokepoint | — | DenyAsLimit | n/a |

**Enforcement is fail-closed twice (P1 ∧ P2/P3 merged):** at startup, the host refuses to boot if any non-GET endpoint lacks a `[PolicyAction]` mapping to a table row; at request time, the Hosting middleware refuses closed with the standard shapes. B1's proof: boot-refusal test on an unmapped canary POST + deny tests on a mapped POST with a deny-row actor + the CI-generated action×axis matrix suite compiled from the same table object.

## 4. 9A config entries

Mechanism + manifest loader + **only the entries S1's own pillars consume** (S5's ledger row owns the eng-review §5 v0 batch; it seeds data through S1's manifest format, not code). Every config change — including seeding — appends an Audit-stream event in the same tx: proven by test at S1, rendered by desk at S5.

| key | scope | v0 | consumer |
|---|---|---|---|
| `core.con_day_cutoff` | set | `04:00` | Deterministic window math (eng review §5, already ruled) |
| `privacy.lawful_basis_map_variant` | founder | `conservative_global_v0` | lawful-basis resolution (§1b); counsel's map = new variant + audited config flip |
| `privacy.region_fallback` | ops | `ZZ` | unknown region → strictest treatment, never the permissive one |
| `core.events.replay_batch_size` | ops | `1000` | projection runner |
| `core.purge.sweep_interval_minutes` | ops | `60` | purge pipeline scheduler |

**Rules locked:** brand identity never enters 9A (inherited, S0 §4). **A config key with no registered consumer fails a lint** (P2) — the registry never accretes dead tunables. No i18n mirror at S1 (§1c).

## 5. 10A quota keys

**None live** (P1 over P2's telemetry key and P3's full v0 registration — see §12.5/12.6). No consumer-facing consumable action exists; keys belong to their consuming slices (first real key: S14's `match.swipe.daily`, D26). S1 ships everything later keys resolve against:

- Naming convention `<domain>.<action>.<window>` + additive registration-file format, pinned now so S14/S22/S23 keys are rows, not design work. The declaration (reset semantics: daily/weekly × con-local/user-local) is code beside the key; the cap is 9A config.
- The Deterministic window math with golden vectors: DST spring/fall transitions (incl. a mid-edition transition per the eng-review failure-mode table), con-midnight 04:00 rollover, user-local vs con-local divergence.
- The `Consume` verb, the `ICapModifier` seams (identity at S1), and the ONE `LimitReached` deny shape. Design 5.16's identical-rendering law is structural: there is only one shape to render.
- Con-local resolution takes `(tz, cutoff)` as parameters (`IConDayResolver` seam); S8 supplies real con values. No fake con data ever.

## 6. 13A store registrations (B2: the gate lands before the first feature store — and S1's own stores are the first registrants)

**The CI gate:** arch test enumerates every EF entity type + every declared blob/cache store and fails the build on any store absent from the purge-registry manifest — proven non-vacuous by a red fixture (an unregistered fixture table fails the gate). Fills the CI slot S0 reserved.

**Purge-class taxonomy defined CLOSED at S1** so every later store declares against a fixed set: `account_deletion` (full pipeline, profilemodel §1c full-history semantics), `statutory_erasure` (Art. 17 incl. delivered copies, ER-14), `minor_purge`, `consent_revocation` (window-bounded recompute — explicitly distinct from account_deletion's full-history path), `retention_expiry`, `orphaned_blob` (activates S11). Plus the **`custody_hold` override attribute** (ER-14: holds override erasure during open reports, documented basis — S12/S24 register holds, not new pipelines). **CryptoShred is a VERB a store declares per class** (P1/P2 over P3's class framing), tested: protect → shred → unprotect fails → PurgeReport emitted.

| store | account_deletion | statutory_erasure | minor_purge | consent_revocation | retention_expiry | orphaned_blob |
|---|---|---|---|---|---|---|
| `events_ledger` + `ledger_entries` (+balances) | Tombstone refs (entries survive as tombstones, user refs severed; balances rebuilt by Replay) | Tombstone | Tombstone | NotApplicable (earn events not consent-gated) | n/a | n/a |
| `events_reputation` | Tombstone | Tombstone | Delete | window-bounded recompute (verbs declared; writer arrives S16) | model_version-scoped | n/a |
| `events_consent` | per OQ-1 interim: pseudonymize subject (revocation is an EVENT on this stream, never a purge of it) | per OQ-1 | per OQ-1 | n/a | n/a | n/a |
| `events_behavioral` | Delete rows | Delete | Delete | Delete | retention window (value set when the desk consumes it, S5) | n/a |
| `events_audit` | per OQ-1 interim: Tombstone actor PII in payload, record survives (immutability posture: "reversal entries, never data surgery") | per OQ-1 | per OQ-1 | n/a | statutory | n/a |
| `events_heatmap_provenance` | full-history (profilemodel §1c founder ruling); deletion machinery only — NO read API exists in the contract assembly, enforced structurally | Delete | Delete | n/a | 12mo / R2 (`cell_history_months`) | n/a |
| `quota_counters` | Delete | Delete | Delete | NotApplicable (reason) | Delete expired windows (GC) | n/a |
| `config_entries` | NotApplicable all classes (reason: zero personal data by construction — registered with reason, never silently exempt) | | | | | |
| `data_protection_keys` / `field_key_refs` | CryptoShred (Key Vault purge-protection ON means shred = key destruction by re-wrap denial, tested) | CryptoShred | CryptoShred | n/a | n/a | n/a |
| `purge_runs`, `projection_checkpoints` | registered non-PII with reason; purge_runs itself carries retention_expiry — the registry eats its own dog food | | | | | |

**Pipeline proof (playbook purge-completeness lens):** seed every store → run every class → assert zero residue or asserted tombstone state + a purge_run row + an audit event per store. Committed as a test, not a claim.

## 7. Notification taxonomy rows

**Zero — asserted, not assumed** (S0's closure-baseline pattern). The actor is the system; no consumer-visible state change exists. Config changes are operator-facing (admin desk audit, outside the nine-category consumer taxonomy); ledger accrual has no consumer surface yet — category 7's "balance changed and why" row is already reserved by the taxonomy for S16/S33. S4's closure lint inherits a clean baseline.

## 8. BUILD.md §9 seams made concrete

| Seam | S1 concrete form |
|---|---|
| Schema-per-context (1A) | Contracts vs internal assemblies; arch test: only `*.Contracts` referenced cross-boundary; schema `core` owned solely by CoreDbContext; opaque prefixed-ULID ids — no cross-module FK ever |
| Transactional outbox | `IEventStore.Append` joins the ambient UoW — a domain write and its event are one tx **by API shape**, not discipline; telemetry rides the separate behavioral stream (§9: domain events same-tx, telemetry separate) |
| Region-first PII (L21) | `RequestContext.Region/LawfulBasisVariant` + NOT NULL stamps on all six streams and ledger_entries; RegionSource provenance; pii-patterns extended; config-deployable lawful-basis variants |
| Server-authoritative trust (L20) | contract-lint rule 1 now non-vacuous PLUS an arch test scanning request-DTO type graphs for `verification*/reputation*/premium*/moderation_state/trust*/tier*` (catches internal DTOs OpenAPI lint cannot see) — red-fixture-proven |
| One deny shape (DR-7.3 / token law 4) | single `LimitReached` component; contract-lint rule 3 armed with substance |
| Absence as server truth (token law 3) | `DenyAsAbsence` policy mode; the table enforces the source, contract-lint invariant 2 enforces the wire |
| Silent rejection unobservable | deny mode declared per policy entry; excluded read ≡ nonexistent read: identical status, identical body, same code path (timing-channel mitigation is structural single-path, asserted by test) |
| Buy-vs-build seams | `IPaymentService` stub (B12) + `IFieldKeyVault`: DevSeams-only impls, arch-tested never-in-prod-DI, prod resolution throws (L18) |
| Analytics written AND received | `IBehavioralStream` one door; e2e asserts the sink row EXISTS and the watermark advanced (never emit-only — the hmg scar) |
| Deterministic math in pure libs | `Svac.DomainCore.Deterministic`: golden vectors, IO-free by arch test, no LLM |
| Foreign-event skip (§8 cl.7) | structural in `IProjection`/`Replay` (watermark advances on skip); ledger-balance projection is instance #1 of the hermetic test; skip-test template ready for every future consumer (S4) |
| Concurrency at check-then-act | quota atomic UPSERT-where + event ExpectedVersion unique-violation-re-read, shipped as the reference implementations with race tests |
| 15A router chokepoint | arch rule "no provider SDK outside Svac.AimlRouter" lands now — vacuously green, arms S2 the moment it exists |
| L19 rank-by-attestation / on-platform-REAL | arch-rule slots land now, binding future types by name |
| i18n (DR-7.7, token law 2) | `message_key` discipline + `contracts/message-keys.json` ×4-parity (guarded); `*_display` lint promoted to hard |

## 9. Dependency classification (every not-yet-built system S1 reads)

| Dependency | Class | Handling |
|---|---|---|
| Identity/auth (S3) | seam-now | opaque `ActorRef` + `ActorKind`; test-fixture principals via DevSeams only; bearer scheme declared empty. NEVER stub a fake authenticated user; staff policy rows exist but are unexercisable until S5's Entra |
| Premium (S23), reputation tiers (S16), mode (S19) | seam-now | `ICapModifier` identity defaults + policy axes defaulting free/neutral; tiers never numbers (presentation law respected in the axis type itself) |
| Azure Key Vault (OQ-3 pending, no subscription) | seam-now | `IFieldKeyVault` over local dev keyring (DevSeams); prod wiring = config against the S0-reserved key name; prod-without-Vault throws; nothing blocks S1 green |
| Region resolver truth (S3/S10) | seam-now | `IRegionResolver` deterministic seam + RegionSource provenance so upgrading resolution never rewrites history |
| Lawful-basis map (counsel L-1) | seam-now | config-selected variant, conservative-global default (§1b) |
| Con registry / real con timezones (S8) | seam-now | `IConDayResolver(tz, cutoff)` parameters into Deterministic; con-local resets inert-but-golden-vectored until S8 registers the resolver |
| Metrics & Ops desk (S5) | swap-safe | audit/behavioral events land now; desk renders later; receipt verified by e2e reading the stream |
| Notifications (S4) | swap-safe | S4 subscribes as a 3A consumer; skip-test template ready; audit stream shape frozen here |
| AimlRouter (S2) | not read | 15A arch rule pre-armed only; zero latent surface in S1, stated so no accidental coupling appears |
| Payment vendors (S23 IAP, S30 Stripe) | swap-safe | IPaymentService stub per B12; swap = one DI line |
| Redis (S0 compose) | deliberately unused | quota counters are Postgres (§2); promotion = one store swap behind IQuotaService |
| Azure subscription (OQ-3) | must-build-first for prod deploy only | dev compose is the whole S1 runtime |
| MUST-BUILD-FIRST blocking S1 | **none** | S0 is green; S1 starts today |

## 10. Outcome moved + where it renders

Ledger row: **"arch suite enforces every pillar; contract v0 published."** Measured at sign-off:

1. `Svac.Tests.Architecture` green and **NON-VACUOUS: every enforcement rule proven by a red fixture** (a deliberate violation compiled into a test asset fails the rule) — 1A boundary, 4A unmapped-mutation boot refusal, 13A unregistered-store, 15A provider-SDK, L20 trust-DTO, DevSeams-not-in-prod-DI, Deterministic IO-isolation, append-only trigger, consumer-DenyStandard-on-read. A rule that has never fired is a hope, not a gate.
2. `contracts/openapi.v0.json` committed + contract-lint green un-skipped + drift gate green + `openapi-contract-v0` artifact published + `message-keys.json` beside it → lanes B/C/D unblocked (B17 dead).
3. Kills evidenced by named tests: **B1** (boot-refusal + deny-mode tests + generated matrix), **B2** (gate red on unregistered fixture + registrations + purge-completeness suite), **B3** (`con_day_cutoff` and the S1 keys read through registry machinery by real consumers in a host path), **B12** (prod-throw test on IPaymentService), **B17** (above).
4. Live E2E `backend/e2e/substrate.e2e.mjs` against compose: health 200 → client-config matches `i18n/locales.json` → behavioral event emitted on the request and **read back** with watermark advanced → purge class run leaves zero residue → fresh-boot clause (`down -v`→`up`, migrations under advisory lock, consumers registered after the migration service, L13) → zero-exception log sweep.
5. Rendering until S5: Actions status matrix + committed arch-rule count (S0's interim posture). At S5: desk tiles for config-change audit events, purge-run reports, quota-deny counts — all already flowing on the streams S1 built.

## 11. Evals

**N/A with reason:** S1 contains zero latent-space surface (BUILD.md §0 enumerates all four; none is here — even S2's routing policy is deterministic-space by 15A's own text). Deterministic tests are the entire quality story: golden vectors (con-day/DST, ledger summation incl. negative-svac sinks and reversal chains, config bounds); property-based event-substrate tests (append/reverse/tombstone/replay invariants, seq uniqueness under race — eng review §6); the generated 4A matrix; purge-completeness; crypto-shred; silent-deny single-code-path; red-fixture arch proofs; idempotent-under-race quota; foreign-event skip; fresh-boot + E2E.

## 12. Synthesis decisions log (conflicts resolved, contradictions noted)

1. **Host scope: ONE public host (P1/P2) + `Hosting` package (P2); P3's three hosts LOSES automatically** — `backend/admin-host` is S5's unit and the partner host S29's per the ledger; pre-creating another lane's unit violates the parallel-session boundary. The Hosting package guarantees the later hosts mount the same 4A chokepoint (arch-tested), which was P3's valid kernel.
2. **Assembly layout: P1's namespaces-not-projects over P2's per-pillar assembly pairs.** CLAUDE.md: "Services are extracted only when one earns it — do not pre-split." The 1A module-boundary pattern is satisfied by the Contracts/internal split + arch tests; ~24 projects for one bounded unit is pre-splitting for hypothetical extraction. P2's `Hosting` package and the separate pure `Deterministic` assembly (P2/P3) are grafted — those earn their boundaries now.
3. **Events storage: table-per-stream (P2+P3) over P1's single discriminated table.** Per-stream retention/purge/volume/residency genuinely differ; independent re-storage stays possible; the table names trip ddl-lint's PII patterns. Generated from one shape definition so it costs one design, not six.
4. **9A seeding: mechanism + S1-consumed entries only (P1/P2); P3's full §5 batch at S1 LOSES** — the ledger's S5 row carries "config registry editor + v0 batch seeded" verbatim. S1's manifest format means S5 seeds data, not code. P3's S1-consumed privacy entries survive (they have S1 consumers). P2's dead-tunable lint adopted; P2's telemetry keys and P3's `window_grace_seconds` dropped (no consumer → would fail the very lint adopted).
5. **P2's telemetry ingest endpoint — REJECTED.** The behavioral seam's "verified received" is satisfied server-side (emit on a request path + e2e read-back + watermark assert); a client ingest endpoint's real design (device_ref semantics, event catalogs, quarantine) belongs to the first client slice, and anonymous-device actors would invent an auth story two slices before S3. The hmg emit-without-sink scar is about the sink existing and being verified — it is, at S1, without a public POST.
6. **10A: zero live keys (P1) over P3's full v0 key registration.** Keys with no reachable consumer are dead rows that the consuming slices (S14 et al.) would re-litigate anyway; the machinery, naming convention, reset semantics, and golden vectors are what must predate the first consumer — and they do.
7. **Policy deny-mode closed union (P3) — GRAFTED whole.** Makes token law 3, silent-rejection unobservability, and one-deny-shape structural in the type system rather than per-endpoint judgment. Combined with P1's startup fail-closed boot refusal AND P2/P3's request-time refusal: both hold simultaneously.
8. **region + lawful_basis NOT NULL on ALL six streams (P3) over selective (P2) / region-only (P1).** A uniform invariant is testable where a selective one erodes; retrofitting L21 is the named rewrite. pii-patterns extended (`*events_*`, `*ledger*`).
9. **LimitReached shape merged (P1+P2+P3):** `{quota_key, message_key, resets_at, premium_extends}`. P2's no-cause-taxonomy law holds (nothing distinguishes tier floors from freemium caps); `premium_extends` is the one render hint design 5.16's single surface needs; `message_key` is the i18n law; `resets_at` canonical per DR-7.7.
10. **Purge taxonomy: P3's closed class set + ER-14 custody_hold attribute; CryptoShred as a VERB (P1/P2, 2v1) not a class.** A verb composes with every class per store; a class would force key stores to re-declare the same action six times.
11. **Ids: prefixed typed ULIDs (P3) + strongly-typed wrappers (P2)** — the prefix carries actor kind, making 4A axes and ER-6 absence rules enforceable by type, not lookup. P1's raw uuid superseded.
12. **i18n 9A mirror of locales — NOT created (P1) over P3's mirror-with-drift-test.** S0 §4 delegated the call; no runtime consumer exists at S1 and duplicate truth is the disease 9A exists to cure. The drift-test pattern is recorded for the first runtime consumer (S4).
13. **Region granularity (P3's OQ-2) — DECIDED, not open:** country + optional subdivision always-when-known; P3's own citation (T2r jurisdiction table needs `US-TX`-class rows by ratified spec) settles it, and the column is a one-way door.
14. **xp = points enforced by CHECK (P1/P3) over P2's `xp >= 0`** — the 1:1 law lives in the database, not in prose.
15. **Quota counters: Postgres, Redis unused — unanimous**, recorded with the promotion path.
16. **DevSeams as environment flag, never 9A (P1) — unanimous in effect**, arch-tested per P2.
17. **`*_display` lint advisory→hard (P3)** — executes S0's own escalation note; not a new decision, an obligation honored.
18. **GET /v1/client-config (P1) — ADOPTED**: gives the e2e a real request path carrying a behavioral emit and clients their boot config; P3's health-only surface was one endpoint too austere to prove the stack end to end.

## 13. Julien-executed actions

- Ratify OQ-1 below (the only genuinely-his call in this slice).
- No new environments, secrets, or external accounts required beyond S0's §13 list.

## 14. OPEN QUESTIONS (genuinely Julien's — permanent privacy postures only)

- **OQ-1 — Post-account-deletion retention of the `consent` and `audit` streams.** (a) Retain with subject pseudonymized (irreversible re-key), `lawful_basis=legal_obligation`, bounded by a founder-scope 9A retention ceiling — the defensible-record posture: proves consent existed, preserves staff-action accountability in a dispute. (b) Purge with the account — the maximal-deletion posture, at the cost of being unable to evidence past consent or reconstruct a staff-action trail. P1 and P2 both effectively built (a), and the §4-A5 immutable-audit ruling plus "reversal entries, never data surgery" lean (a) — but this is a permanent privacy posture only Julien ratifies. **Interim posture (S0 ratification pattern): build (a), reversible until the first production deletion runs.** The §6 registration table's "per OQ-1" cells resolve with the answer.

No other forks: host scope, storage shape, seeding split, deny shape, purge taxonomy, and Postgres-vs-Redis are architecture positions resolved above with stated reasoning; every config value S1 touches is founder-ratified (eng review §5) or ops-scope revisable from the desk.

## 15. RATIFICATION (2026-07-10)

Reviewed against the S1 ledger row (BUILD.md §7) and the LOCKED rulings; ratified. Verified: dependency classification balloons nothing (IPaymentService/AimlRouter/KeyVault/region/cap-modifiers all seam-now or not-read, MUST-BUILD-FIRST = none, §9); §12 resolves architect-proposal conflicts, not rulings; deterministic-space respected (§11 evals N/A with a real reason); each of the five kills maps to a named test (§10.3).

**Scope call ratified:** the minimal public host at `backend/public-host/Svac.PublicApi` (§0/§12.1) is IN scope for S1 — B1 (live 4A refusal of a real HTTP mutation) and B17 (OpenAPI v0 emitted + lint-gated) are unprovable without a running host. Confined to zero business logic; the reusable 4A chokepoint lives in `Svac.DomainCore.Hosting` so S5's admin host and S29's partner host mount the same one. S1 does NOT create those other two hosts. The BUILD.md:69 "no public host yet (S9...)" note is about S9 being first consumer of the edge-guard e2e, not host ownership — not in conflict.

**OQ-1 resolved — posture (a), interim-reversible:** post-account-deletion, consent and audit streams RETAIN with the subject pseudonymized (irreversible re-key), `lawful_basis=legal_obligation`, bounded by a founder-scope retention ceiling. Forced for the audit half by the §4-A5 immutable-audit ruling; the defensible-record posture for consent. Built reversibly per the S0 ratification pattern: nothing irreversible happens until the first production deletion runs (none in dev; the deletion pipeline is S3; a production deletion is many slices out). Julien may flip to posture (b) any time before that first production deletion without a rewrite. The §6 "per OQ-1" cells resolve to: `events_consent` account_deletion/statutory_erasure/minor_purge = pseudonymize subject (revocation stays an event on the stream); `events_audit` same-classes = Tombstone actor PII in payload, record survives.

Ratified without a live response from Julien (away from keyboard at ratification time); asked via AskUserQuestion, both recommended defaults are reversible and foreclose no permanent choice. Proceeding into Phases 1-3.

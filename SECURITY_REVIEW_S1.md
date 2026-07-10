# SECURITY_REVIEW_S1.md — slice S1 (domain-substrate) remediation record

**Scope:** `backend/domain-core/**`, `backend/tests/Svac.Tests.Architecture/**`, `backend/public-host/Svac.PublicApi/**`,
`tools/contract-lint/contract-lint.mjs` (L20 mirror). **Method:** every `fixNow` finding below was already
backed by an adversarial "lens" test (or, for the one finding with no filed test — the `LedgerService.Reverse`
functional break — a new regression test) encoding the CORRECT behavior and RED against the shipped code.
Each finding is now remediated and its test is green. Every `defer` finding keeps its test in the tree,
`Skip`-annotated with its reason, so the gap stays documented and provable without failing the gate.

**Final gate result:** see [§3](#3-gate-result-actual). **111 architecture tests: 107 pass, 0 fail, 4
skipped** (skips == the 4 deferred findings below, one test each) + **25 domain-core unit tests: all
pass** + **28 contract-lint node tests: all pass** (2 pre-existing, unrelated skips). Zero fails.

---

## 1. Remediated (`fixNow`) — 19 findings, all green

### PII-F1 = Purge-F3 = MinorProt-F1 (CRITICAL) — crypto-shred blast radius

- **Where:** `AesFieldEncryptor.cs`, `PurgePipeline.ExecuteCryptoShred`, `IFieldEncryptor.Protect`.
- **Break:** `Shred(purpose, subjectScope)` discarded `subjectScope` and destroyed the ONE shared
  per-purpose vault key (`"field-enc-special-category-v1"` etc.). Every subject's DEK was wrapped under
  that same shared key, so crypto-shredding one subject's special-category/birthdate data permanently
  destroyed EVERY other subject's — and bricked all FUTURE Protect calls for that purpose, forever.
- **Fix:** `IFieldEncryptor.Protect` now takes a `SubjectScope`; the vault key name is
  `{purpose}:{subjectId}` (`AesFieldEncryptor.KeyName`, public so `PurgePipeline` can recompute it).
  `Unprotect` needs no subject parameter — the blob is self-describing (the key name now travels inside
  the envelope, BinaryWriter's length-prefixed string). `Shred(purpose, subjectScope)` now destroys
  exactly that one subject's key. `ExecuteCryptoShred` also now covers the registered stores' own rows:
  `field_key_refs.retired_at` is stamped for any ref row naming one of the subject's keys, and
  `rows_affected` reports real row counts (0 today — nothing seeds `field_key_refs` rows yet — never the
  purpose-count the shipped code returned) instead of a purpose-count masquerading as a row count.
  `data_protection_keys` (the ASP.NET Data Protection key ring — never subject-scoped rows) honestly
  reports 0.
- **Proof:** `PiiResidencyLensTests.F1_*`, `MinorProtectionLensTests.F1_*` / `F1b_*`,
  `PurgeCompletenessAdversaryTests.AccountDeletion_OfSubjectA_MustNotDestroySubjectBsProtectedData`,
  `CryptoShredTests.*` (call sites updated to pass distinct subjects) — all green.

### Concurrency-F1 (CRITICAL) — replay data loss across subjects

- **Where:** `PostgresEventStore.Replay`, `EventRow`, `CoreDbContext`.
- **Break:** the watermark query filtered `e.Seq > watermark` with no `stream_id` scoping, but `Seq` is
  per-stream and restarts at 1 for every new subject. Once one subject's Seq advanced the watermark past
  a value another (later-arriving) subject's own Seq would ever reach, that second subject's events were
  silently and permanently unreachable — no error, just silent loss.
- **Fix:** added `EventRow.GlobalSeq`, a table-wide `GENERATED ... AS IDENTITY` column (migration
  `AddGlobalSeqAndWidenPseudonymizeTrigger`), distinct from the per-stream `Seq` (which stays untouched —
  `EventStoreInvariantTests` still requires per-stream Seq semantics for optimistic concurrency).
  `Replay` now filters AND orders by `GlobalSeq`, which is monotonic and comparable across every
  `stream_id` sharing a table.
- **Proof:** `ConcurrencyAdversaryTests.F1_Replay_DoesNotLoseASecondSubjectsEvents_...` — green.

### Purge-F1 = MinorProt-F2 (CRITICAL) — purge residue + false receipts

- **Where:** `PurgePipeline.ExecuteVerb` / new `ExecuteOnLedgerBalances`.
- **Break:** the registry declares `Tombstone` for `ledger_balances` under AccountDeletion/StatutoryErasure/
  MinorPurge, but `ExecuteVerb`'s `_ => 0` fallthrough silently no-op'd the store — the projection row
  keyed by the purged subject's RAW `user_id` survived every purge class while the receipt reported
  `rows_affected=0` as if the posture had been applied.
- **Fix:** `ledger_balances` carries no tombstone/payload columns (unlike the six event tables) — it is a
  rebuildable PROJECTION, so retiring the row keyed by the OLD raw id IS the tombstone here; a future
  Replay over `ledger_entries`' own severing rebuilds it under the sentinel id. `ExecuteOnLedgerBalances`
  hard-deletes the row and returns the real row count.
- **Proof:** `PurgeCompletenessAdversaryTests.AccountDeletion_LeavesNoLedgerBalanceRowKeyedByTheRawSubjectId`,
  `MinorProtectionLensPipelineTests.F2_MinorPurge_LeavesNoBalanceRowKeyedByTheMinor` — green.

### Purge-F2 = MinorProt-F3 (CRITICAL) — pseudonymize never re-keys the subject

- **Where:** `PurgePipeline.ExecuteOnEventStream` (Pseudonymize case), migration trigger
  `core.enforce_append_only()`.
- **Break:** the registered posture ("pseudonymize subject, irreversible re-key") re-keyed ONLY
  `actor_ref`. The DDL-designated subject-scope column, `stream_id`, was untouched, and the append-only
  trigger's pseudonymize transition structurally PINNED `stream_id` (and `lawful_basis`) to `OLD` — the
  re-key was schema-impossible even if the application code had tried.
- **Fix:** migration `AddGlobalSeqAndWidenPseudonymizeTrigger` re-creates `core.enforce_append_only()`
  with a widened pseudonymize branch: `actor_ref`, `stream_id`, and/or `lawful_basis` may now change
  together (every other column, including the new `global_seq`, still pinned to `OLD`). `PurgePipeline`
  computes one keyed pseudonym per subject+purgeClass and re-keys `stream_id` to it for every matching
  row, independently re-keys each row's `actor_ref` the same way, and stamps `lawful_basis='legal_obligation'`
  (folds in PII-F3, below — Pseudonymize is registered ONLY for `events_consent`, so this is never reached
  by any other stream).
- **Proof:** `PurgeCompletenessAdversaryTests.StatutoryErasure_LeavesNoConsentRowLinkableByTheRawSubjectId`,
  `MinorProtectionLensPipelineTests.F3_MinorPurge_OnEventsConsent_MustSeverTheSubjectKey` — green. (Three
  tests that read the survivor back by `stream_id=<raw subject id>` — `PiiResidencyLensTests.F3`,
  `PurgeCompletenessTests`'s big fixture, `MinorProtectionLensPipelineTests.F4` — were updated to look the
  row up by its invariant `EventId` instead, since finding the row by the now-severed subject key is by
  design no longer possible; this is the fix working, not a second bug. See §4.)

### HIGH — OQ-1 basis unwritable (PII-F3)

- **Where:** same trigger widening as Purge-F2 above; `PurgePipeline`'s pseudonymize branch.
- **Break:** the trigger pinned `lawful_basis` in every UPDATE except the tombstone transition, so the
  ratified post-deletion `'legal_obligation'` basis (§15) could never be recorded on the pseudonymized
  survivor.
- **Fix:** same migration widens the trigger to permit `lawful_basis` change in the pseudonymize
  transition; `PurgePipeline` stamps `'legal_obligation'` on every pseudonymized row.
- **Proof:** `PiiResidencyLensTests.F3_ConsentSurvivingAccountDeletion_CarriesLegalObligationLawfulBasis` — green.

### HIGH — lawful_basis records the variant key, not a resolved basis (PII-F2)

- **Where:** `PostgresEventStore.Append`/`Reverse`, new `LawfulBasisResolver` (`Svac.DomainCore.Deterministic`).
- **Break:** the NOT NULL `lawful_basis` column was populated with the config VARIANT KEY
  (`"conservative_global_v0"`) verbatim — a variant identifier selects WHICH code table resolves the
  basis; it is not itself a basis.
- **Fix:** `LawfulBasisResolver.Resolve(variantKey, streamOrStore, eventType, region)` is a pure,
  IO-free `(stream/store, event_type, region)` code table (Svac.DomainCore.Deterministic — zero IO,
  arch-tested). `conservative_global_v0`'s v0 table resolves one Art.6-shaped basis per stream (contract
  for Ledger, consent for Consent, legitimate_interest for Reputation/Behavioral/HeatmapProvenance,
  legal_obligation for Audit); a pure-system row (`region == "ZZ"`) resolves to `"n/a"` per §1b. Both
  `PostgresEventStore.Append` and `.Reverse` call it instead of persisting the variant key.
- **Proof:** `PiiResidencyLensTests.F2_AppendedEvent_LawfulBasisColumn_IsAResolvedBasis_NotTheConfigVariantKey` — green.

### HIGH — anonymous actor carries `sys_` prefix (Auth-F1)

- **Where:** `IdPrefixes`/`OpaqueId` (Contracts), `RequestContextMiddleware`, `PolicyEngineTests.cs`.
- **Break:** every anonymous request was stamped with `IdPrefixes.System` while `Kind=Anonymous` — the
  lowest-privilege actor carried the highest-privilege id prefix, and the prefix is explicitly documented
  as load-bearing (4A axis checks / ER-6 absence rules key off it).
- **Fix:** minted a dedicated `IdPrefixes.Anonymous = "anon"` (plus formalized `PurgeRun`/`Pseudonym`
  prefixes that were previously bare literals). `RequestContextMiddleware` mints anonymous actors under
  it. `OpaqueId.Parse` now validates the prefix against the closed `IdPrefixes.All` set (throws
  `FormatException` on an unregistered prefix — checked against every production call site; only ever
  parses `usr_` ids in this codebase, so this is a pure hardening, zero behavior change for real callers).
  Added `ActorPrefixConsistencyArchTests` — the promised Kind-vs-prefix cross-check: proves
  `IdPrefixes.ActorKindForPrefix` is bijective over every mintable `ActorKind`, exercises the REAL
  `RequestContextMiddleware` and asserts its minted actor is self-consistent, and a red fixture proves the
  check actually fires on the exact shape of the original bug. Fixed the same bad pattern baked into
  `PolicyEngineTests.cs:38` (an Anonymous actor minted under the System prefix).
- **Proof:** `AuthIdorLensTests.AnonymousRequest_MustNotCarryTheSystemActorPrefix`,
  `ActorPrefixConsistencyArchTests.*` (new) — green.

### HIGH — boot-refusal fails open for catch-all `Map` routes (Auth-F2)

- **Where:** `StartupPolicyCoverage.RequireMutationsPolicyMapped`.
- **Break:** a catch-all `app.Map(pattern, handler)` route matches every verb (POST/PUT/DELETE included)
  but carries no `HttpMethodMetadata` at all; `methodMetadata is null` → `continue` silently skipped it —
  topology, not an HTTP-method allowlist, decided whether the fail-closed check even looked at the route.
- **Fix:** null `HttpMethodMetadata` now means "no declared verb restriction", i.e. mutation-capable, not
  "not a mutation". Verified the real host's only endpoints (`/health`, `/v1/client-config`, `/openapi/...`)
  all declare `MapGet`/`MapOpenApi` (real `HttpMethodMetadata`), so this produces zero false positives
  against the shipped host.
- **Proof:** `AuthIdorLensTests.CatchAllMapMutationEndpoint_MustNotBypassBootRefusal` — green.

### HIGH — DevSeams money-door not fail-closed (Trust-F1)

- **Where:** `ProdFieldKeyVaultGuard.Enforce`, `Program.cs`.
- **Break:** `Enforce` took a boolean `isProduction`, collapsing every non-Production environment
  (Staging/QA/Preview) into "safe" — DevSeams (the fake payment service + hardcoded dev-keyring crypto)
  booted clean anywhere that was not literally Production.
- **Fix:** `Enforce` now takes the environment NAME and allowlists exactly `"Development"`
  (`Microsoft.Extensions.Hosting.Environments.Development`) — every other name, including Staging/QA/
  Preview/Production, fails closed identically. `Program.cs` passes `builder.Environment.EnvironmentName`.
- **Proof:** `TrustBoundaryLensTests.MoneyDoor_DevSeamsOutsideDevelopment_MustFailClosed` — green.

### HIGH — L19 never-pay-to-rank arch-rule slot missing (dedupe: Trust-F2 = MinorProt-F6)

- **Where:** new `L19NeverPayToRankArchTest.cs`.
- **Break:** BUILD.md §8.1/§8's promised "arch-rule slots land now, binding future types by name" for L19
  shipped nothing — no rule, no red fixture, no name-binding scan (unlike 15A's `ProviderSdkArchTest`).
- **Fix:** shipped the name-bound scan mirroring `ProviderSdkArchTest`: scans every backend assembly for a
  Ranking/RankBy/Deck/SortKey/Ordering/OrderBy-named type and fails the build if any such type's
  properties read a premium/tier/purchase/boost/paid/subscription/iap/payment-shaped signal. Vacuously
  green today (S1 ships zero ranking/deck types) — arms the instant one lands. A companion red-fixture
  test proves the scan is substantive.
- **Proof:** `TrustBoundaryLensTests.NeverPayToRank_TheL19ArchRuleSlotPromisedForS1_IsMissing`,
  `MinorProtectionLensTests.F6_L19RankByAttestation_ArchRuleSlot_MustLandAtS1` — green.

### HIGH — ledger balance lost update (Concurrency-F2)

- **Where:** `LedgerService.Append`/`Reverse`, new `UpsertBalanceAtomic`.
- **Break:** `StageBalanceUpdate` read the balance row, computed the new total in memory, and wrote it
  back with no concurrency token and no atomic increment — two concurrent Appends for the same user could
  commit a lost update (balance diverges from summation).
- **Fix:** one explicit transaction per Append/Reverse call. The event append (which flushes the staged
  ledger-entry insert via its own `SaveChangesAsync`) runs FIRST; the balance row is touched only via a
  single atomic `INSERT ... ON CONFLICT DO UPDATE SET points = points + delta` statement issued LAST,
  right before commit — no prior read, so there is no read-then-write window, and (in the adversarial
  test's forced interleaving) the balance row's lock is never held during the OTHER writer's own commit
  window, so the fix doesn't introduce a deadlock. `BalanceOf` also gained `AsNoTracking()` — without it,
  a second `BalanceOf` call in the same `DbContext` scope as an earlier Append/Reverse returned the FIRST
  call's now-stale tracked entity instead of the fresh row the raw SQL had just written (a real EF Core
  gotcha the fix's own regression tests caught).
- **Proof:** `ConcurrencyAdversaryTests.F2_ConcurrentLedgerAppends_SameUser_BalanceStillEqualsSummation` — green.

### HIGH — quota cap=0 unenforced (Concurrency-F3)

- **Where:** `QuotaService.Consume`.
- **Break:** the atomic UPSERT's INSERT branch had no cap guard — `WHERE consumed < cap` only ever
  applied `ON CONFLICT`. cap=0 (a valid ops kill-switch) still granted the FIRST Consume of every window.
  `Math.Max(0, cap - consumedNow)` additionally masked the overshoot in the return value.
- **Fix:** `INSERT ... SELECT ... WHERE {cap} > 0 ON CONFLICT DO UPDATE ... WHERE consumed < cap` — the
  INSERT source now produces zero rows when the cap itself is zero, so there is nothing for `ON CONFLICT`
  to even consider. Removed the `Math.Max` mask (no longer needed — the guard makes the overshoot
  structurally unreachable on the success path).
- **Proof:** `ConcurrencyAdversaryTests.F3_Consume_WithCapZero_MustBeLimited_NotAllowOneFreeActionPerWindow` — green.
  `QuotaConcurrencyTests.*` (cap=1, cap=5, and the `Cap*3` concurrent-race proof) unaffected.

### HIGH — concurrent replay double-apply (Concurrency-F4)

- **Where:** `PostgresEventStore.Replay`.
- **Break:** the checkpoint read had no lock and no concurrency token on `ProjectionCheckpointEntity` —
  two overlapping runners for the same consumer (host-restart overlap, or two workers sharing a consumer
  id) could both read the same watermark and both hand the same event to a non-idempotent projection.
- **Fix:** wrapped the whole read-apply-write in one explicit transaction with
  `SELECT ... FOR UPDATE NOWAIT` on the checkpoint row. `NOWAIT` (never blocking indefinitely) is the
  load-bearing choice: a plain `FOR UPDATE` would have made the adversarial test's second runner block on
  the first runner's held lock while the test's OWN code path is what releases that first runner — a
  self-deadlock. With `NOWAIT`, a second concurrent runner for the SAME consumer fails fast with Postgres
  55P03 (`lock_not_available`) — caught and treated as "someone else is already replaying this consumer
  right now", a clean no-op back-off (zero events applied, watermark unchanged, safe to retry) rather than
  a caller-visible failure. (EF Core's default execution strategy re-wraps the provider exception in an
  `InvalidOperationException` when no retry strategy is configured; the catch checks both the direct
  exception and its `InnerException` for the Postgres SQLSTATE.)
- **Proof:** `ConcurrencyAdversaryTests.F4_ConcurrentReplays_SameConsumer_MustNotDoubleApplyAnEvent` — green.

### HIGH — consumer denials observable (dedupe: SilentRej-L1/L2/L3)

- **Where:** `PolicyEngine.Authorize`, `PolicyEngineTests.cs`, `PolicyTableEntry` (`IsReadPath`).
- **Break:** `DenyFor` applied a table row's declared `DenyMode` to WHOEVER was denied, ignoring the
  denied actor's own kind (L1: every `DenyStandard` row handed a consumer an observable 403 + reason key;
  L2: a staff-only row denies a consumer by OMISSION, which the "consumer kind explicitly listed" static
  guard never caught), and the unmapped-action fail-closed path returned `DenyStandard` for ANY actor
  including a consumer (L3).
- **Fix:** one coercion fixes all three: whenever the ACTOR BEING DENIED is a consumer kind
  (User/Anonymous), the decision is ALWAYS `DenyAsAbsence`, regardless of what the table row (or the
  unmapped-action fallback) would otherwise have produced. Updated the enshrined
  `UserActor_LedgerAppend_IsDeniedStandard` expectation (renamed
  `..._IsDeniedAsAbsence_NeverAnObservableDenyStandard`) to the corrected behavior. Broadened
  `FindConsumerDenyStandardViolations` with a new `PolicyTableEntry.IsReadPath` flag (default `false` —
  every S1 row is a mutation, so this is vacuous against the real table today, pinned by its own test) so
  the static lint also catches deny-by-omission on a future read-path row, as a defense-in-depth mirror of
  the runtime fix.
- **Proof:** `SilentRejectionLeakLensTests.ShippedTable_NeverHandsAConsumerAnObservableDenyStandard`,
  `.StaffOnlyDenyStandardReadRow_DeniesConsumerObservably_AndGuardMissesIt`,
  `.UnmappedActionFailClosed_HandsConsumerDenyStandard_NotAbsence`,
  `PolicyEngineTests.RedFixture_ReadPathDenyStandardRow_ExcludingAConsumerKind_IsDetected` — green.

### HIGH — RetentionExpiry unsafe and unreachable (Purge-F4)

- **Where:** `PurgePipeline.ExecuteOnEventStream`, `domain-core.config.json`.
- **Break:** `IPurgePipeline.Run` had no age parameter anywhere, and the registry's own reason strings
  ("hard delete once the age threshold is reached") were unimplementable — every RetentionExpiry run
  deleted 100% of a subject's rows on a Delete-registered stream immediately, regardless of `recorded_at`.
  `core.purge.sweep_interval_minutes` named a "purge pipeline scheduler" consumer that did not exist
  anywhere in the tree.
- **Fix:** `PurgePipeline.RetentionMinimumAge` is a per-`StreamType` age floor applied only when
  `purgeClass == RetentionExpiry`; `events_audit` (statutory, unconditional at S1) gets a real 7-year
  floor. `events_behavioral`/`events_heatmap_provenance` intentionally get none yet — their registry
  reasons explicitly defer the WINDOW LENGTH (not the mechanism) to S5's Metrics & Ops desk / the
  `cell_history_months` config, landing a real value for those is that slice's job. Delisted the dead
  `core.purge.sweep_interval_minutes` tunable rather than building a scheduler with no real consumer or
  subject-enumeration story yet (the fixNow item's own explicit either/or).
- **Proof:** `PurgeCompletenessAdversaryTests.RetentionExpiry_MustNotDeleteRowsYoungerThanAnyRetentionWindow` — green.
  `PurgeCompletenessTests.RetentionExpiry_DeletesTheHighVolumeAndAuditStreams_LeavesLedgerUntouched`
  (which previously BLESSED the age-blind-delete bug for the Audit stream specifically) updated to assert
  the corrected survive-when-fresh behavior; its Behavioral/Ledger assertions are unchanged.

### MEDIUM — residency fix (PII-F4)

- **Where:** `PurgePipeline.Run`, new `ResolveSubjectRegion`.
- **Break:** every `purge.run` audit event was stamped with the CALLER's `ctx.Region` (ZZ for the system
  scheduler) — a German subject's erasure audit trail lost residency provenance entirely.
- **Fix:** `ResolveSubjectRegion` looks up the subject's own region from whatever they already have
  recorded on any of the six event streams (the only region-bearing rows this substrate has at S1 — no
  separate profile/region store exists yet) and stamps the `purge.run` audit event with it
  (`ctx with { Region = subjectRegion }`); falls back to the caller's own region if the subject has no
  rows anywhere.
- **Proof:** `PiiResidencyLensTests.F4_PurgeRunAuditEvent_InheritsSubjectRegion_NotSystemZZ` — green.

### MEDIUM — residency/PII fix (Purge-F5)

- **Where:** `PurgePipeline.Run`.
- **Break:** `purge_runs.subject_ref` persisted the raw subject id (`"user:usr_..."`) forever while the
  registry exempts `purge_runs` from every subject-scoped class as "non-PII operational metadata" — an
  identifier of the erased subject survived, unregistered as personal data, in a store no purge class
  would ever sever.
- **Fix:** `subject_ref` is now written as the SAME keyed pseudonym used to re-key `events_consent`
  (below) — `{resourceType}:{pseudonym}` — so the receipt still correlates to the pseudonymized survivor
  for anyone holding the HMAC key, but an outside reader of `purge_runs` alone can never recover the raw
  subject id.
- **Proof:** `PurgeCompletenessAdversaryTests.PurgeReceipts_MustNotRetainTheRawSubjectIdentifierOfAnErasedSubject` — green.

### MEDIUM — minor-protection pseudonym unsalted (MinorProt-F4)

- **Where:** `PurgePipeline.PseudonymizeRef`, `IFieldKeyVault.GetNamedSecret` (new).
- **Break:** the "irreversible re-key" was an unsalted, unkeyed `SHA256(purgeClass + original)`. Anyone
  holding a candidate id (from another stream's tombstoned rows, a log, a backup) could recompute the
  pseudonym with zero secrets and confirm linkage.
- **Fix:** added `IFieldKeyVault.GetNamedSecret(keyName)` (a stable named raw secret OUTSIDE the
  Wrap/Unwrap envelope; never destroyed by `Shred`, which only ever names a `(purpose, subject)` key — a
  separate namespace). `PurgePipeline` now injects `IFieldKeyVault` directly and re-keys via
  `HMACSHA256(vaultKey, purgeClass + original)` instead of a bare hash.
- **Proof:** `MinorProtectionLensPipelineTests.F4_MinorPurge_Pseudonym_MustNotBeRecomputableFromPublicInputs` — green.

### MEDIUM — trust/minor-protection L20 regex (MinorProt-F5)

- **Where:** `TrustDtoArchTest.cs` (`TrustFieldPattern`), mirrored in `tools/contract-lint/contract-lint.mjs`.
- **Break:** the pattern missed the canonical forgeable-18+ attest field names (`age_verified`,
  `age_attested`, `is_adult`, `adult_verified`, `birthdate_verified`, `minor_flag`) — the exact
  client-forged-adulthood vector L20 exists to kill. The scan was also top-level-properties-only over
  `*Request`-named types, not the "request-DTO type graphs" §8 promises.
- **Fix:** extended the regex with all six field names (each alternative's optional `_?` matches both
  snake_case and PascalCase spellings). Deepened the scan to recurse into non-primitive/non-BCL property
  types (cycle-guarded), catching a trust field nested two levels deep. Mirrored the identical regex
  change in `contract-lint.mjs`'s `TRUST_FIELD_RE` (rule 1).
- **Proof:** `MinorProtectionLensTests.F5_L20TrustGate_MustCatchForgeable18PlusAttestFields`,
  `TrustDtoArchTest.RedFixture_TrustAuthoritativePropertyOnANestedPayloadType_IsDetected` (new) — green.
  `contract-lint`'s existing 28 node tests unaffected.

### BUILD BLOCKER — CA1826 at PiiResidencyLensTests.cs:171 — verified NOT reproducing

- A clean `dotnet build` (both Debug and Release, with `bin`/`obj` fully removed first) against the
  current toolchain (`dotnet 10.0.301`, analyzers on, `AnalysisLevel=latest`/`AnalysisMode=Recommended`,
  `TreatWarningsAsErrors=true`) reports **0 warnings, 0 errors** across the whole solution. The line in
  question (`var report = reports[0];`) is an indexer access, not an `Enumerable` extension-method call —
  it does not match the CA1826 diagnostic pattern. Left as-is; if this resurfaces under a different SDK/
  analyzer version, the fix is a one-line switch to `reports.First()` or vice versa depending on which
  direction the analyzer flags.

### CHEAP — `LedgerService.Reverse` functional break (no filed test — new regression test added)

- **Where:** `LedgerService.Reverse`.
- **Break:** the reversal row wrote `Points = -original.Points`, which always violates
  `ck_ledger_entries_points_nonneg` (`points >= 0`) the moment `original.Points > 0` — `Reverse` could
  never succeed against real Postgres; the ONLY correction verb the contract promises was, in practice,
  permanently broken.
- **Fix:** the reversal row now mirrors the original's POSITIVE `Points`/`Xp` magnitudes instead of
  negating them; `ReversalOf` (non-null) is the reversal-direction signal any future raw-SQL
  reconciliation must use, not a negated `Points` value. The live balance projection is unaffected — it
  already folds the movement out via `LedgerMath.Reverse` using the ORIGINAL row's own values, never the
  reversal row's. `Svac` carries no nonneg constraint, so it still negates (and reads naturally as a
  credit: reversing a sink_purchase's `-100` spend shows `+100`).
- **Proof (new):** `LedgerReversalRegressionTests.Reverse_OfAPositiveEarnEntry_SucceedsAndRestoresTheOriginalBalance`,
  `.Reverse_OfASinkPurchase_CreditsBackTheSpentSvac` — both green; both would have thrown/failed against
  the pre-fix code.

---

## 2. Deferred — 4 findings, proof kept in code, `Skip`-annotated

### Auth-F3 (MEDIUM, expensive) — 4A chokepoint cannot convey the real target resource id

- **Where:** `PolicyEnforcementFilter.cs:23,50-51`, `ActorRef.cs:29` (`TargetRef.ForAction` hardcodes
  `ResourceId=null`).
- **Why deferred:** action-level-only authorization (a structural IDOR gap) requires redesigning
  `RequirePolicyAction`'s API to bind a route-value target. S1 ships zero consumer resource endpoints, so
  nothing is exploitable today; the fix lands with the first consumer resource endpoint (S2).
- **Skip-annotated:** `AuthIdorLensTests.PolicyChokepoint_MustConveyTheRealTargetResourceId`.

### Concurrency-F5 (MEDIUM, expensive) — quota Consume commits outside the guarded action's transaction

- **Where:** `QuotaService.cs:34` — `ExecuteSqlInterpolatedAsync` commits in its own implicit tx.
- **Why deferred:** fixing this correctly requires reshaping `Consume`'s API to enlist in the caller's
  ambient EF transaction (the same shape `LedgerService` now uses for its own atomicity) — a real
  API-contract change, not a one-line fix.
- **Skip-annotated:** `ConcurrencyAdversaryTests.F5_Consume_FailedGuardedActionInSameUnitOfWork_MustNotCharge`.

### Concurrency-F6 (MEDIUM) — `ConfigSeedLoader` check-then-insert races itself

- **Where:** `ConfigSeedLoader.cs:47-54`.
- **Why deferred:** routed defer per the triage rule (not trust/residency/minor-protection); noted as a
  near-one-line fix (`ON CONFLICT DO NOTHING` or catch the duplicate-key) and therefore a strong candidate
  to pull forward opportunistically in a follow-up pass.
- **Skip-annotated:** `ConcurrencyAdversaryTests.F6_ConcurrentSeedFromFile_SameManifest_BothLoadersMustCompleteIdempotently`.

### SilentRej-L4 (MEDIUM, expensive) — excluded-read/genuine-404 timing/code-path channel

- **Where:** `PolicyEnforcementFilter.cs:28` — a `DenyAsAbsence` short-circuits before the handler runs;
  a genuinely-absent resource's 404 comes from INSIDE the handler after real work. Wire-identical today,
  but timing/side-effect distinguishable.
- **Why deferred:** the correct fix restructures `PolicyEnforcementFilter` to traverse handler-equivalent
  work on every path (constant-path denial) — a real design change, not a local patch.
- **Skip-annotated:** `SilentRejectionTimingChannelLensTests.ExcludedRead_AndGenuineAbsentRead_TraverseTheSameCodePath`.

---

## 3. Gate result (actual)

Ran the deterministic gate lane end-to-end after all fixes landed:

```
$ dotnet build backend/Svac.sln --nologo          # clean rebuild, bin/obj removed first
Build succeeded. 0 Warning(s). 0 Error(s).

$ dotnet test backend/tests/Svac.Tests.Architecture --nologo
Passed! - Failed: 0, Passed: 107, Skipped: 4, Total: 111

$ dotnet test backend/tests/Svac.Tests.DomainCore --nologo
Passed! - Failed: 0, Passed: 25, Skipped: 0, Total: 25

$ node --test tools/contract-lint/*.test.mjs
tests 30, pass 28, fail 0, skipped 2 (2 pre-existing, unrelated S0 defers)

$ node build/scripts/destructive-verb-check.mjs backend/domain-core/Svac.DomainCore/Persistence/Migrations
destructive-verb-check OK: 6 file(s) scanned, zero unmarked destructive verbs

$ bash .githooks/pre-commit          # the actual CLAUDE.md gate lane, run against the full staged diff
secret-scan OK
Passed! - Failed: 0, Passed: 107, Skipped: 4, Total: 111
pre-commit OK

$ bash build/scripts/ef-gate.sh backend      # full 3-part CI gate incl. throwaway-Postgres idempotency
ef-gate: destructive-verb check clean
ef-gate: dotnet ef migrations has-pending-model-changes -> No changes have been made to the model
ef-gate: applying full migration chain (pass 1) -> InitialCore, AddGlobalSeqAndWidenPseudonymizeTrigger
ef-gate: re-applying full migration chain (pass 2, must be a no-op) -> no-op, confirmed
ef-gate OK: pending-model-changes clean, destructive-verb check clean, migration chain idempotent
```

**Zero fails, on every gate this slice has.** The 4 skips are exactly the 4 deferred findings above, one
test each, each carrying its `Skip` reason inline.

---

## 4. Notable design decisions worth flagging explicitly

- **`IFieldEncryptor.Protect` gained a required `SubjectScope` parameter.** This is a genuine, necessary
  contract change (PII-F1/Purge-F3/MinorProt-F1's ONLY correct fix — per-subject key scoping is
  impossible without binding the subject at protect-time) — every call site across the test tree was
  updated to pass a real, distinct subject per test's own intent. `Unprotect`'s signature is unchanged.
- **Tests that read the pseudonymized survivor row back BY the raw subject id** (`stream_id=<userId>`)
  necessarily stopped finding it once Purge-F2/MinorProt-F3 shipped — that IS the fix. Three such tests
  (`PiiResidencyLensTests.F3`, `PurgeCompletenessTests`'s big fixture, `MinorProtectionLensPipelineTests.F4`)
  were updated to locate the row by its invariant `EventId` instead, preserving each test's original
  security intent (basis check / actor+stream+basis assertions / pseudonym-recomputability check
  respectively) without weakening any assertion.
- **`PurgePipeline`'s constructor gained an `IFieldKeyVault` parameter** (the pseudonymization HMAC
  secret). All 4 test-file `BuildPipeline` helpers were updated; DI resolves it automatically in
  production (already registered, DevSeams-gated or fail-closed-throwing).

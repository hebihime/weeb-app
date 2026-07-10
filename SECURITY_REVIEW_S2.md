# SECURITY_REVIEW_S2.md — slice S2 (aiml-router) remediation record

**Scope:** `backend/modules/AimlRouter/**`, `backend/domain-core/Svac.DomainCore/Config/**`,
`backend/tests/Svac.Tests.AimlRouter/**`, `backend/tests/Svac.Tests.Architecture/**` (S2 lenses + one
strengthening pass on `TrustDtoArchTest.cs`/`DevSeamsNotInProdDiTests.cs`), `backend/e2e/aiml-router-
diagnostic-host/Program.cs`. **Method:** every `fixNow` finding below was already backed by an
adversarial "lens" test encoding the CORRECT behavior and RED against the shipped S2 code. Each finding
is now remediated and its test is green. Every `defer` finding keeps its test in the tree,
`Skip`-annotated with its reason, so the gap stays documented and provable without failing the gate.

**Final gate result:** see [§3](#3-gate-result-actual). **51 AimlRouter gate tests: 49 pass, 0 fail, 2
skipped** + **134 architecture tests: 130 pass, 0 fail, 4 skipped** (skips == the 3 deferred S2 findings
below + the 1 pre-existing S1 defer, one test each) + **25 domain-core unit tests: all pass** + **58
contract-lint/ddl-lint/i18n-lint node tests: all pass** (2 pre-existing, unrelated S0 skips). Zero fails.

---

## 1. Remediated (`fixNow`) — 17 findings, all green

### PII-S2-F1 / TRUST-BREAK-1 (HIGH) — set-time bounds validation did not exist

- **Where:** new `ConfigBounds.cs` (`Svac.DomainCore.Config`), wired into `ConfigRegistry.SetValue`.
- **Break:** `SetValue` checked key existence and the reason string, then serialized and saved
  *anything*. `config_entries.bounds_json` was seeded `null` and read by nothing. §4's promised law
  ("an unlawful or non-Claude-default policy cannot be *saved*, not merely not-served") and the second of
  the "two independent locks" on special-category egress did not exist — `special_category_ok:true`,
  a non-Claude `default_chain[0]`, an unallowlisted provider, and a timeout outside `[5,300]` all saved
  cleanly and were audited as legitimate.
- **Fix:** `ConfigBounds.ValidateAsync(key, valueJson, db, ct)` is a generic, JSON-shape validator keyed
  by 9A key NAME — never a typed reference to `Svac.AimlRouter`'s own record types, so domain-core stays
  the substrate and never depends on a module (1A; the reverse reference would be circular since
  AimlRouter already depends on domain-core). Mirrors the same discipline `LawfulBasisResolver` already
  applies to stream names. Three rules: `aiml.provider_allowlist` entries with `special_category_ok:true`
  throw; `aiml.routing_policy`'s `default_chain[0]` must name a provider present in the CURRENTLY
  COMMITTED allowlist, with `family=="claude"` and a declared model, or it throws; `aiml.invoke_timeout_
  seconds` outside `[5,300]` throws. `ConfigRegistry.SetValue` calls this BEFORE mutating the tracked row,
  so a rejected Set leaves the stored value and the audit stream byte-for-byte untouched.
- **Proof:** `TrustBoundaryLensS2ConfigDoorTests.SetValue_*` (3), `PiiResidencyLensS2Tests.
  PiiS2F1_Allowlist_SpecialCategoryOkTrue_MustFailBoundsAtSetValue_BeforeS17Exists` — green.

### PII-S2-F2 (HIGH, residency) — Resolver ignored region; ResidencyOverrides couldn't deserialize a real override

- **Where:** `Resolver.cs` (`ChainFor`, new `MatchesResidency`), `RoutingPolicy.cs` (new
  `ResidencyOverride` record).
- **Break:** `Resolver.Resolve` accepted `region` and never read it; it never touched
  `RoutingPolicy.ResidencyOverrides` or the allowlist entry's `residency` field — routing was
  region-blind by construction. `ResidencyOverrides` was typed `IReadOnlyList<string>`, so a real
  structured override (`{"region":"DE","chain":[...]}`) could not even deserialize; because SetValue had
  no bounds (F1), an ops-desk save of such an override was ACCEPTED and then poisoned every subsequent
  `GetValue<RoutingPolicy>` with a raw `JsonException` — an egress outage triggered by a tightening edit.
- **Fix:** `ResidencyOverride(Region, Chain)` replaces the bare string list — expressible and readable by
  both `ConfigRegistry.SetValue` and `Resolver.Resolve`. `ChainFor` now checks `ResidencyOverrides` for a
  region match FIRST (a region pin outranks the per-task/default chain), then falls through to task/
  default chains. `Resolver.Resolve`'s per-candidate loop now calls `MatchesResidency(allowlisted.
  Residency, region)` — `"global"` always matches (the OQ-A v0 posture); anything else must match the
  call's own region country code. Every test-tree call site's `ResidencyOverrides: Array.Empty<string>()`
  became `Array.Empty<ResidencyOverride>()` (13 files) — a genuine, necessary contract-shape change, the
  ONLY correct fix (a bare string array cannot express a structured override; §4's own doc comment
  already called this out).
- **Proof:** `PiiResidencyLensS2Tests.PiiS2F2a_Resolver_MustConsumeResidencyInputs_NotAcceptAndIgnoreThem`,
  `.PiiS2F2b_SavedResidencyOverride_MustEitherBeRefusedAtSet_OrRemainReadableByTheResolver` — green.

### PII-S2-F3 (HIGH, residency) — subject-region inheritance missing on `aiml.route_decided`

- **Where:** `AimlRouterService.ResolveAuditContext` (new), called once per `InvokeAsync`.
- **Break:** `AppendDecision` stamped the CALLER's `ctx` verbatim. A system-actor call about a German
  subject (T9-style background triage) recorded `region='ZZ'`, and `LawfulBasisResolver` then resolved
  `lawful_basis='n/a'` ("no personal data") on a decision row that carried the subject's raw id,
  `PayloadClass=Pseudonymous`, and a content fingerprint (`payload_sha256`) — a falsified lawful-basis
  record for exactly the consumer shape this slice pre-declares.
- **Fix:** `ResolveAuditContext` is the router's analog of S1's `PurgePipeline.ResolveSubjectRegion`: when
  `req.Subject` is present, it walks every `StreamType` via `IEventStore.ReadStream(stream, subject.Id.
  ToString())` and adopts the first non-`Unknown` region found, exactly the "whatever they already have
  recorded on any stream" rule PII-F4 established. Best-effort — a lookup failure or a subject with no
  rows anywhere falls back to the caller's own `ctx`, so a provenance lookup can never itself block
  routing. Computed once per call, used by every `AppendDecision` call site (all outcomes, not just
  Success).
- **Proof:** `PiiResidencyLensS2Tests.PiiS2F3_RouteDecided_ForAGermanSubject_FromASystemActor_MustBeEuScopedWork_NeverZzNa` — green.

### TRUST-BREAK-2 (HIGH) — model-door guard regressed Trust-F1; DevSeamsNotInProdDiTests never covered IModelProvider

- **Where:** `AnthropicApiTransport.cs` (`AnthropicApiKeyGuard.Enforce`), `AimlRouterServiceCollectionExtensions.AddAimlRouter`, `DevSeamsNotInProdDiTests.cs`.
- **Break:** `Enforce(bool policyCanReachApiTransport, bool devSeamsEnabled, bool apiKeyConfigured)`
  trusted a bare flag and could not see which environment it was running in — the exact boolean collapse
  SECURITY_REVIEW_S1.md's Trust-F1 outlawed for `ProdFieldKeyVaultGuard`. `DevSeamsNotInProdDiTests.cs`
  covered only `AddDomainCore`'s `IPaymentService` family, never `IModelProvider`.
- **Fix:** `Enforce` now takes the environment NAME and allowlists exactly `"Development"` (Trust-F1
  parity, same shape as `ProdFieldKeyVaultGuard`). `AddAimlRouter` gained an `environmentName` parameter
  (defaults `"Production"` — the strictest posture — for callers that state none). Extended
  `DevSeamsNotInProdDiTests` with `ProdAimlRouterComposition_NeverResolvesADevSeamsOnlyModelProvider` /
  `DevAimlRouterComposition_ResolvesTheDevSeamsOnlyModelProvider`, resolving the internal `IModelProvider`
  type via reflection (`Assembly.GetType(...)`) rather than `InternalsVisibleTo`, so the architecture
  suite never needs to see inside the module's own SPI.
- **Proof:** `TrustBoundaryLensS2Tests.ModelDoorGuard_MustSeeTheEnvironmentName_TrustF1Parity`,
  `DevSeamsNotInProdDiTests.ProdAimlRouterComposition_*` / `.DevAimlRouterComposition_*` — green.

### TRUST-BREAK-3 (HIGH) — keyless-prod guard fired at first resolution, not at build

- **Where:** `AimlRouterServiceCollectionExtensions.AddAimlRouter`, new `AnthropicApiKey` wrapper record.
- **Break:** the guard lived inside the `IModelProvider` singleton FACTORY LAMBDA. Empirically verified
  while fixing this: DI's `ValidateOnBuild` proves constructor-DEPENDENCY resolvability only — it invokes
  neither a factory delegate nor a throwing constructor body. A keyless production composition booted
  clean; the throw surfaced as a per-request 500 on the first `InvokeAsync` instead of a loud boot
  failure.
- **Fix:** `AnthropicApiTransport` is now registered by TYPE (`AddSingleton<IModelProvider,
  AnthropicApiTransport>()`, constructor-based, never a factory), and its constructor requires a typed
  `AnthropicApiKey` dependency. `AddAimlRouter` registers that dependency ONLY when the composition is
  lawful (Development, or a real key configured); an unlawful composition leaves it structurally
  unresolvable, so `BuildServiceProvider(ValidateOnBuild: true)`'s standard graph-resolvability pass
  throws AT BUILD. `AnthropicApiKeyGuard.Enforce` remains the single source of truth for the LAW itself,
  ready for a real host's Program.cs to call eagerly before `AddAimlRouter` (mirroring
  `ProdFieldKeyVaultGuard`'s own Program.cs-level call) — this method's own build-time behavior is the
  structural backstop that holds even if a host forgets that eager call.
- **Proof:** `TrustBoundaryLensS2Tests.ModelDoor_KeylessProdComposition_MustFailAtBuild_NeverAtFirstResolution` — green.

### CONC-S2-1 / SR-F3 (HIGH) — caller cancellation misclassified as a failed hop

- **Where:** `AimlRouterService.InvokeAsync`'s chain-walk catch clauses.
- **Break:** `catch (Exception ex) when (ex is not OutOfMemoryException)` also caught
  `OperationCanceledException` raised off the CALLER's own token — a cancelled call was recorded as a
  failed hop and the walk proceeded to the NEXT provider: a fresh vendor-egress attempt (spend + user
  data leaving the trust boundary) AFTER the caller had already given up.
- **Fix:** a new, earlier catch clause — `catch (OperationCanceledException) when (ct.IsCancellationRequested)`
  — audits the attempt (CONC-S2-2) and rethrows, never falling through to the generic failover catch.
  Failover (§1b) is for provider failure; cancellation now always propagates with zero further egress.
- **Proof:** `ConcurrencyLensTests.CONC_S2_1_CallerCancellation_MustNotFailoverToTheNextProvider`,
  `SilentRejectionLensTests.CallerCancellation_Propagates_AndDoesNotReAttemptRemainingHopsUnderDeadToken` — green.

### CONC-S2-2 (HIGH) — terminal/failure audit append could itself be killed by the caller's cancellation

- **Where:** `AimlRouterService.AppendDecision`.
- **Break:** every `AppendDecision` call forwarded the caller's own (possibly-already-cancelled) `ct` to
  `IEventStore.Append`. Against a real `PostgresEventStore` (EF honors the token), an invocation torn
  down by cancellation could leave ZERO `aiml.route_decided` events — "every `InvokeAsync` appends ONE
  event" (§1b audit law) silently broken exactly when calls are cancelled under load.
- **Fix:** `AppendDecision` no longer accepts a `CancellationToken` parameter at all — it always calls
  `IEventStore.Append` with `CancellationToken.None`, a fresh, never-cancelled scope. Every attempted
  egress (success, refusal, budget deny, chain exhaustion, or caller cancellation) now leaves exactly one
  decision record, regardless of the caller's own token state.
- **Proof:** `ConcurrencyLensTests.CONC_S2_2_CancellationDuringChainWalk_MustStillAuditTheEgressAttempt` — green.

### F-S2P1 / F-S2P2 / MINOR-S2-F1(+F1b) (HIGH, minor-protection + purge) — subject-bearing rows unreachable by every purge verb

- **Where:** `AimlRouterService.AppendDecision` (streamId selection).
- **Break:** every `aiml.route_decided` event was appended with `streamId = invocationId` (an `aiv_`
  ULID); the erased subject's identifier rode ONLY inside the payload's `subject_ref` field. The audit-
  stream purge machinery's ONLY subject-scoped selector is `stream_id == subject.ResourceId`
  (`PurgePipeline.ExecuteOnEventStream`) — so `MinorPurge`/`AccountDeletion`/`StatutoryErasure` (all
  registered `Tombstone` for `events_audit`) matched ZERO router rows, and `RetentionExpiry` had no
  reachable path either. The subject's raw id and a stable content fingerprint (`payload_sha256`)
  survived every purge class, forever, under the audit stream's 7-year retention floor — while the purge
  receipt reported `rowsAffected=0` as if the posture had been applied: false evidence of completeness.
- **Fix:** the decision's `streamId` is now `req.Subject?.Id.ToString() ?? invocationId.Value` — a
  subject-bearing call is keyed by the SUBJECT's own raw opaque id (the one column every purge verb
  already reads), a subject-less system call keeps the invocation id as its stream. The invocation id is
  never lost either way — `AimlRouteDecidedEvent.InvocationId` still carries it inside the payload on
  every path, so nothing downstream that keyed off the invocation id loses that correlation.
- **Proof:** `AimlPurgeCompletenessLensTests.S2P1_RouteDecidedEvent_MustNotSurviveSubjectPurge_WithRawSubjectRefInPayload`
  (×3 purge classes), `.S2P2_RouteDecidedEvent_CarryingASubject_MustBeKeyedBySubjectStreamId`,
  `MinorProtectionLensS2Tests.S2F1_MinorPurge_MustLeaveNoRouterAuditRowStillReferencingTheMinor`,
  `.S2F1b_MinorPurge_Receipt_MustNotClaimEventsAuditCleanWhileRouterResidueExists` — green.

### TRUST-BREAK-4 (MEDIUM) — trust-DTO scan missing provider*/model*/payload_class, request-only

- **Where:** `TrustDtoArchTest.cs` (`TrustFieldPattern`, the scanned-type filter).
- **Break:** the pattern was the S1 set verbatim (`verification|reputation|premium|...`) — zero coverage
  of `Provider`, `Model`, or `PayloadClass`, exactly the §1b-promised extension. The scan also ran only
  over `*Request`-named types; §1b's actual concern here ("neither the receipt nor provider identity
  ever serializes into a user-bound DTO") is a RESPONSE-shaped leak, not a request-forgery concern.
- **Fix:** extended the regex with `provider|model|payload_?class` (same `_?` convention matching both
  `PayloadClass` and `payload_class`). Widened the scan filter to `*Request`-OR-`*Response`-named types.
  Verified zero false positives against every existing response DTO (`LimitReached`, `Problem`,
  `ClientConfigResponse`, `HealthStatus`) — none carry a matching property name.
- **Proof:** `TrustBoundaryLensS2Tests.TrustDtoScan_MustCoverProviderModelPayloadClass_PerS2Contract1b` — green.

### TRUST-BREAK-5 (MEDIUM) — explicitly-empty task chain fell open to the default chain

- **Where:** `Resolver.ChainFor`.
- **Break:** `policy.TaskChains.TryGetValue(task, out var taskChain) && taskChain.Count > 0 ? taskChain :
  policy.DefaultChain` — an explicitly-empty task chain (the natural per-kind ops kill switch) was
  silently overridden by the default chain. With `task_chains == {}` (the v0 seed), every kind was
  already live through the default chain regardless — "a kind with no policy chain fails closed" (§1b)
  was true of NO kind that had ever been explicitly emptied.
- **Fix:** dropped the `.Count > 0` gate — `TryGetValue` alone is exactly "declared vs. undeclared", the
  distinction the fail-closed law needs. A declared-but-empty task chain now resolves to that empty
  chain (`NoRouteConfigured`); only an UNDECLARED task key falls through to the default chain.
- **Proof:** `TrustBoundaryLensS2Tests.Resolve_ExplicitlyEmptyTaskChain_FailsClosed_NeverFallsOpenToDefaultChain` — green.

### TRUST-BREAK-6 / CONC-S2-3 / SR-F1 (LOW-MED) + SR-F2 + SR-F4 — timeout/provider-error misaudited as ChainExhausted; only the last hop observable

- **Where:** `AimlRouterService.InvokeAsync`'s chain-walk (post-cancellation-fix catch clause).
- **Break:** the blanket catch converted every hop failure — timeout, provider error, or genuine chain
  exhaustion — into the identical opaque `ChainExhausted`, and the single `failoverFrom` local was
  overwritten every iteration, so a 3-hop chain where every hop failed left only the LAST hop's name on
  the audit trail. `AimlFailure.Timeout` and `.ProviderError` were dead enum members; an operator
  investigating provider health could not tell a slow provider from a down one, nor see which hops were
  even tried.
- **Fix:** the catch clause now tracks `attemptedHops` (a count) and `lastFailure` (the exception) across
  the loop, and accumulates a comma-joined `failoverFrom` trail (every hop, not just the last). After the
  loop: exactly one real attempt (`attemptedHops <= 1`) surfaces that attempt's OWN kind —
  `AimlFailure.Timeout` for a `TimeoutException`, `.ProviderError` for anything else — while more than one
  real attempt collapses to the genuinely-earned `.ChainExhausted`.
- **Proof:** `SilentRejectionLensTests.Timeout_IsSurfacedAsTimeout_NotSilentlyReclassifiedAsChainExhausted`,
  `.SingleHopProviderError_IsSurfacedAsProviderError_NotChainExhausted`,
  `.ChainExhausted_AuditEvent_PreservesEveryAttemptedHop_NotOnlyTheLast`,
  `ConcurrencyLensTests.CONC_S2_3_HopTimeout_OnSingleHopChain_ReturnsTimeout_NotChainExhausted`,
  `TrustBoundaryLensS2Tests.InvokeAsync_ProviderTimeout_SurfacesAsTimeout_NeverMisauditedAsChainExhausted` — green.

### TRUST-BREAK-7 / SR-F5 (LOW-MED) — TargetLocale never validated

- **Where:** `AimlRouterService.InvokeAsync` (new step 0), `ShippedLocales`.
- **Break:** `req.TargetLocale` was read by nothing in the module — a `Translate` call carrying a locale
  outside the four shipped ones (`en`, `es`, `pt`, `zh-Hans`) routed straight through to the provider.
  `AimlFailure.InvalidRequest` was a dead enum member.
- **Fix:** a new step 0, ahead of the egress authorizer and budget check (a malformed request should
  never spend budget or reach the privacy-floor lock): for `Task == Translate`, `TargetLocale` must be a
  member of a hardcoded `ShippedLocales` set mirroring `i18n/locales.json` — kept as a pure code table
  (Deterministic discipline: no file IO at runtime, which would not survive a real publish layout) rather
  than a repo-relative read. A miss returns `AimlFailure.InvalidRequest` before any egress, and still
  appends an audit decision (consistent with every other early-refusal path).
- **Proof:** `TrustBoundaryLensS2Tests.InvokeAsync_TranslateWithLocaleOutsideTheShippedFour_IsInvalidRequest_NeverRouted`,
  `SilentRejectionLensTests.TranslateWithInvalidTargetLocale_IsRejectedAsInvalidRequest_NotSilentlyAccepted` — green.

### S2-B (LOW-MED) — diagnostic host bound to every interface

- **Where:** `backend/e2e/aiml-router-diagnostic-host/Program.cs`.
- **Break:** the diagnostic `/invoke` route maps an unauthenticated request body onto `IAimlRouter.
  InvokeAsync` under a hardcoded `ActorRef.System`, with no credential check — "test tooling, never
  shipped" is a deployment CONVENTION (topology), not a structural guard. `Program.cs` bound
  `http://0.0.0.0:{port}` while its only real consumer (`aiml-router.e2e.mjs`) connects over loopback, so
  any peer on a shared dev/CI network could POST an arbitrary `Caller`/`PayloadClass` and drive the
  developer's authenticated `claude` CLI session as the System actor.
- **Fix:** both the `UseUrls` binding and the boot log line now read `http://127.0.0.1:{port}` — loopback
  only. The e2e script's own consumer already connects over `localhost`, unaffected.
- **Proof:** `AuthIdorS2LensTests.DiagnosticHost_ModelEgressEndpoint_MustBindLoopbackOnly` — green.

### PII-S2-F4 + F-S2P3 (MED/LOW) — CLI content egress via argv; unbounded on-disk session persistence

- **Where:** `AnthropicLocalTransport.ExecuteAsync`.
- **Break:** the last user turn and the system prompt rode raw process ARGUMENTS (`-p <userTurn>`,
  `--system-prompt <system>`) — argv is a world-readable channel on every dev OS (`ps -ef`, `/proc/<pid>/
  cmdline`) for the lifetime of the call, an unclassified egress with no ceiling, no audit event, no
  purge story. Separately, the `claude` CLI's print mode persists the full prompt+completion to its own
  on-disk session history (`~/.claude`) by default — an unregistered, unbounded-lifetime content store
  with no 13A row and no purge verb, contradicting §1b's "the router holds content for the duration of
  the call and not one second longer."
- **Fix:** the user turn now rides STDIN (closed after writing, sending EOF — mirrors `echo "$prompt" |
  claude -p ...`'s own standard non-interactive usage); the system prompt, when present, rides a 0600
  file under a per-call scratch directory — only the FILE PATH, never its content, is ever visible on
  argv. Session persistence is killed by the CLI's own `--no-session-persistence` flag (auth-path-
  independent — verified empirically that it works standalone without touching credential resolution).
  The scratch directory is ALSO set as `CLAUDE_CONFIG_DIR` (defense-in-depth against any other cache/lock
  file) and deleted in a `finally` block regardless of outcome, so any CLI-side derivative dies with the
  call. **Notable tradeoff, disclosed in the type's own doc comment:** redirecting `CLAUDE_CONFIG_DIR` to
  a genuinely empty directory moves where the CLI resolves its own config/credential file (`$HOME/.claude.
  json`, not inside `$HOME/.claude/`); an OAuth/keychain-authenticated interactive login does not carry
  over to a fresh directory. `ANTHROPIC_API_KEY` (an env var, unaffected by this override since only one
  key is added on top of the inherited environment) is the auth path this isolation is compatible with —
  a developer running the eval lane under an OAuth-only login sets that env var for the eval run. This
  limitation is confined to the paid eval lane; the gate lane never touches a real CLI (`SeedProvider`),
  and no production host reaches this transport at all (`[DevSeamsOnly]`, arch-tested never-in-prod-DI).
- **Proof:** `PiiResidencyLensS2ArgvTests.PiiS2F4_LocalTransport_MustNotPlacePromptOrSystemContent_OnTheProcessArgvChannel`,
  `AimlPurgeCompletenessLensTests.S2P3_LocalTransport_MustIsolateTheCliSessionStore_SoContentDerivativesDieWithTheCall` — green.
  `--no-session-persistence` behavior verified empirically against the real local CLI (session file
  absent from `~/.claude/projects/**` after a call; auth unaffected when `CLAUDE_CONFIG_DIR` is left at
  its default) while building this fix.

### CONC-S2-4b (cheap) — event-store throw could escape InvokeAsync

- **Where:** `AimlRouterService.AppendDecision`.
- **Break:** `IEventStore.Append` failures (a db blip, the `(stream_id, seq)` race, a crash window)
  propagated straight out of `InvokeAsync` — the §1b closed `Success|Failure` union ("never a silent
  drop") was bypassable by an event-store exception, and every consumer would need to know to catch it.
- **Fix:** `AppendDecision`'s whole body is now wrapped in `try { ... } catch (Exception ex) when (ex is
  not OutOfMemoryException) { }` — an event-store throw is swallowed at the audit boundary; the caller's
  own outcome (Success/Failure) is still returned honestly on every path. CONC-S2-4a (deferred, below)
  names the real fix for the resulting divergence risk.
- **Proof:** `ConcurrencyLensTests.CONC_S2_4b_WhenAuditAppendFails_InvokeAsyncMustStillReturnTheClosedUnion` — green.

---

## 2. Deferred — 3 findings, proof kept in code, `Skip`-annotated

### S2-A (LOW, auth/IDOR) — `aiml.invoke` not spliced into the enforced `PolicyTable`

- **Where:** `AimlRouterPolicyEntries.AimlInvoke` (a documented constant, read by nothing enforced).
- **Why deferred:** S2's own scope ruling (§0) does not authorize editing `Svac.DomainCore.Policy.
  PolicyTable`, and there is no route to gate yet (S2 ships zero consumers). Splicing this row in must
  land together with the S1 catch-all-`Map` boot-refusal gap before any consumer mounts the router.
- **Skip-annotated:** `AuthIdorS2LensTests.EnforcedPolicyTable_MustContainAimlInvokeRow_SoAnUngatedRouterRouteCannotShip`.

### CONC-S2-4a (MEDIUM, expensive) — quota Consume and the audit append are two unrelated transactions

- **Where:** `QuotaService.Consume` (its own autocommitted `ExecuteSqlInterpolatedAsync`) vs.
  `AimlRouterService.AppendDecision` (a second, autocommitted `SaveChangesAsync`).
- **Why deferred:** the true fix is a shared transaction / transactional-outbox shape across
  `QuotaService` and `PostgresEventStore` (§8's own transactional-outbox row) — an architectural change
  spanning two S1 substrate pieces, not a local patch. The exception-escape HALF of this finding (an
  event-store throw must never escape `InvokeAsync`) is fixed separately as CONC-S2-4b, above.
- **Skip-annotated:** `ConcurrencyLensTests.CONC_S2_4a_WhenAuditAppendFails_BudgetMustNotHaveBeenBurnedWithoutARecord`.

### CONC-S2-5 (LOW, fail-closed) — torn two-key config read across allowlist and routing policy

- **Where:** `AimlRouterService.InvokeAsync` — `aiml.provider_allowlist` and `aiml.routing_policy` are two
  separate, non-atomic `ConfigRegistry.GetValue` calls.
- **Why deferred:** the correct fix is an atomic multi-key snapshot read in `ConfigRegistry` — a real API
  shape change to the 9A seam itself, not a local patch. Fail-closed today: the worst case is a spurious
  `NoRouteConfigured` for an in-flight call racing a coordinated two-`SetValue` config change, never a
  privacy downgrade.
- **Skip-annotated:** `ConcurrencyLensTests.CONC_S2_5_TornConfigRead_AcrossAllowlistAndPolicy_MustNotFailACallEveryCommittedSnapshotAllows`.

---

## 3. Gate result (actual)

Ran the deterministic gate lane end-to-end after all fixes landed:

```
$ dotnet build backend/Svac.sln --nologo
Build succeeded. 0 Warning(s). 0 Error(s).

$ dotnet test backend/tests/Svac.Tests.AimlRouter --nologo
Passed! - Failed: 0, Passed: 49, Skipped: 2, Total: 51

$ dotnet test backend/tests/Svac.Tests.Architecture --nologo
Passed! - Failed: 0, Passed: 130, Skipped: 4, Total: 134

$ dotnet test backend/tests/Svac.Tests.DomainCore --nologo
Passed! - Failed: 0, Passed: 25, Skipped: 0, Total: 25

$ node --test tools/contract-lint/*.test.mjs tools/ddl-lint/*.test.mjs tools/i18n-lint/*.test.mjs
tests 60, pass 58, fail 0, skipped 2 (2 pre-existing, unrelated S0 defers)

$ node build/scripts/destructive-verb-check.mjs backend/domain-core/Svac.DomainCore/Persistence/Migrations
destructive-verb-check OK: 6 file(s) scanned, zero unmarked destructive verbs

$ bash .githooks/pre-commit          # the actual CLAUDE.md gate lane, run against the full staged diff
secret-scan OK
Passed! - Failed: 0, Passed: 130, Skipped: 4, Total: 134
pre-commit OK

$ bash build/scripts/ef-gate.sh backend      # full 3-part CI gate incl. throwaway-Postgres idempotency
ef-gate: destructive-verb check clean
ef-gate: dotnet ef migrations has-pending-model-changes -> No changes have been made to the model
ef-gate: applying full migration chain (pass 1) -> InitialCore, AddGlobalSeqAndWidenPseudonymizeTrigger
ef-gate: re-applying full migration chain (pass 2, must be a no-op) -> no-op, confirmed
ef-gate OK: pending-model-changes clean, destructive-verb check clean, migration chain idempotent
```

**Zero fails, on every gate this slice has.** The 4 architecture-suite skips are the 3 deferred S2
findings above (one test each) plus the 1 pre-existing S1 defer (`AuthIdorLensTests.
PolicyChokepoint_MustConveyTheRealTargetResourceId`, carried forward per SLICE_S2_CONTRACT.md §13
Correction 2 to the first client-reachable resource-scoped 4A slice). The 2 AimlRouter-suite skips are
CONC-S2-4a/CONC-S2-5 above. `ef-gate.sh` confirms SLICE_S2_CONTRACT.md §2's own promise: zero schema
delta from this slice — no tables, no migration, no EF entity.

Not run as part of this gate (deliberately, per CLAUDE.md's gate-lane/eval-lane split): the paid
periodic eval lane (`backend/modules/AimlRouter/evals/`, `Category=Eval`, requires the real local CLI and
real spend-adjacent latency) and the live Docker-compose E2E canary
(`backend/e2e/aiml-router.e2e.mjs`) — both are HARDENED-GATE/nightly material, not the deterministic
gate this remediation pass is scored against. The eval project's `FailoverUnderRealBackend_EndToEnd_*`
was spot-checked manually against the real local CLI while building the PII-S2-F4/S2P3 fix (see that
finding's own note above on the `CLAUDE_CONFIG_DIR`/auth tradeoff this surfaced).

---

## 4. Notable design decisions worth flagging explicitly

- **`RoutingPolicy.ResidencyOverrides` retyped `IReadOnlyList<string>` → `IReadOnlyList<ResidencyOverride>`.**
  A genuine, necessary contract-shape change (PII-S2-F2's only correct fix) — every call site across the
  test tree (13 files) was updated from `Array.Empty<string>()` to `Array.Empty<ResidencyOverride>()`,
  preserving the exact "empty v0" semantics with no behavior change for any existing test.
- **`ConfigRegistry` gained a bounds-validation call, never a bounds-validation dependency.** `ConfigBounds`
  is a static, internal, domain-core-owned code table keyed by 9A key NAME — deliberately not a typed
  reference to `Svac.AimlRouter`'s record types (which would create a circular module reference) and
  deliberately not an injectable per-module validator interface either, because the adversarial tests
  construct `ConfigRegistry` directly with exactly two constructor arguments (`db, eventStore`) and expect
  bounds enforcement to already be active — the mechanism has to be intrinsic to `ConfigRegistry` itself.
- **`AnthropicApiTransport`'s constructor signature changed from `string apiKey` to `AnthropicApiKey
  apiKey`.** The load-bearing move for TRUST-BREAK-3: DI's `ValidateOnBuild` only proves constructor-
  dependency graph resolvability, never invokes a factory delegate or a throwing constructor body
  (verified empirically while building this fix) — modeling "no key configured" as an unresolvable typed
  dependency is the only mechanism that makes `BuildServiceProvider` itself fail for that cause, rather
  than the first `InvokeAsync` call.
- **`AnthropicLocalTransport`'s argv shape changed materially** (stdin for the user turn, a 0600 scratch
  file for the system prompt, `--no-session-persistence` for the CLI's own history, `CLAUDE_CONFIG_DIR`
  isolation as defense-in-depth) — see PII-S2-F4/F-S2P3 above for the full rationale and the disclosed
  OAuth-auth tradeoff this surfaced during manual verification against the real local CLI.
- **`AddAimlRouter` gained an `environmentName` parameter** (default `"Production"`). Both real call sites
  (the diagnostic host, and the one adversarial test that omits it) compile unchanged; a future real
  host's `Program.cs` passes its own `IHostEnvironment.EnvironmentName`, exactly like `Svac.PublicApi/
  Program.cs` already does for `ProdFieldKeyVaultGuard.Enforce`.

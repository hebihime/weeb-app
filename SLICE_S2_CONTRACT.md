# SLICE S2 CONTRACT — aiml-router (LOCKED)

**Gate:** G0 · **Actor:** the system ("any module reaches a model through ONE egress") · **Kills:** B4 · **Ledger outcome:** arch test: zero provider SDK refs outside router (BUILD.md §7 S2 row) · **Authoritative ruling:** 15A (eng review §1b, founder-ratified 2026-07-10) + its failure-mode row.

Synthesized from three proposals by the design judge. Per-conflict adoptions and the one ruling contradiction are recorded in §12. One open question remains (§11); everything else is resolved with stated reasoning.

**Governing theses (grafted):**

- **From P1 (simplicity):** the router is an in-process module contract, not a service with state. ZERO OpenAPI paths, ZERO database tables, ZERO 13A registrations, ZERO notification rows, ZERO new message keys. Policy lives in `core.config_entries` (9A); decisions live on the 3A event substrate; fail-closed laws live in DI + arch tests. A router that persists nothing has nothing to purge, migrate, or leak.
- **From P2 (extraction-grade seams):** the contract is capability-shaped, never provider-shaped; every vendor is one adapter behind one internal SPI; and **local-Claude-Code vs Claude-API is a TRANSPORT selected by environment, never a provider selectable by policy** — a desk edit can never route production traffic to a keyless local process.
- **From P3 (privacy):** the router is the only place user data leaves our trust boundary toward a model vendor. Content statelessness, classified egress (PayloadClass × provider ceilings), residency-aware deterministic routing, and failure unobservability are structural at S2, or every latent consumer (T9, T8, S25, S36) re-litigates data protection per call site.

---

## 0. Scope ruling

S2 owns `backend/modules/AimlRouter/**` — the first occupant of `backend/modules/`, so it also sets the module template every later slice copies (1A precedent is part of the deliverable). It additionally owns one strengthening pass on `backend/tests/Svac.Tests.Architecture/ProviderSdkArchTest.cs`, its own test + eval projects, and one additive 9A manifest.

S2 does NOT:

- expose any HTTP endpoint (consumers are backend modules calling in-process through the contract assembly; clients never reach a model — that is the 15A posture itself);
- build any consumer task surface (T9→S12, T8→S13, vectors→S25, consultant→S36);
- design media payloads (S11's PhotoDNA/Content Safety legs land as one additive versioned contract change — task kind + payload shape together — when S11 exists to specify them);
- own any prompt (prompts + their evals belong to consuming slices; the router routes and executes neutral envelopes);
- create the T8 translation cache (S13's store, S13's 13A row);
- add Redis, workers, or queues (a hot path earns infrastructure later behind the same contract);
- touch `contracts/openapi.v0.json` or `contracts/message-keys.json` beyond proving zero drift.

**The one structural law this slice makes true:** after S2, exactly one assembly in the backend may reference a provider SDK, and a build violating that fails.

## 1. Module API surface + OpenAPI delta

### 1a. Assemblies (1A minimum; no pre-split — P1 over P2's extra granularity where they differed, none material)

- **`Svac.AimlRouter.Contracts`** (public) — the ONLY assembly consumers reference; references only `Svac.DomainCore.Contracts`; SDK-free forever (arch-tested), so consumers never transitively touch a vendor.
- **`Svac.AimlRouter`** (internal; `InternalsVisibleTo` tests) — `AimlRouterService`, the pure resolver, provider adapters. **The single assembly allowlisted by the provider-SDK arch test.** No `AimlDbContext` exists (§2).
- **`backend/tests/Svac.Tests.AimlRouter`** — gate lane: deterministic, faked/seed providers, <2s.
- **`backend/modules/AimlRouter/evals/`** — periodic lane, `Category=Eval`, threshold-gated, excluded from the pre-commit gate.

### 1b. Contract (signatures load-bearing; 15A verbatim names: module `Svac.AimlRouter`, contract `IAimlRouter`, impl `AimlRouterService`)

```
enum AimlTaskKind { Generate, ModerateText, Translate, EvalProbe }
// CLOSED. Pre-declared from RATIFIED consumer specs only (T9 text triage, T8, S25/S36 generate,
// plus the router's own eval probe) — so activating a consumer is policy DATA, not a contract
// change (P2's insight). ModerateImage/MediaScan is deliberately ABSENT: its payload shape
// (media refs) cannot be designed without S11; it arrives as ONE additive versioned contract
// change (kind + payload together). A kind with no policy chain fails closed (NoRouteConfigured).

enum PayloadClass { NonPersonal, Pseudonymous, Personal, SpecialCategory }   // REQUIRED; no default
enum CallerModule { Integrity, Conversations, Characters, PartnerIntel, Media, System }  // closed

record ProviderPin(string Provider, string Model)   // explicit mode; allowlist + privacy floor still bind

record AimlRequest(
  AimlTaskKind Task, CallerModule Caller,
  PayloadClass PayloadClass,
  ActorRef? Subject,                    // opaque; region + purge scoping; null = subject-less system work
  AimlPayload Payload,                  // neutral text envelope: System, Messages[], MaxTokens,
                                        //   Temperature, StructuredOutputSchema? — no vendor dialect;
                                        //   in-memory only, NEVER persisted by the router
  string? TargetLocale,                 // Translate only; must ∈ i18n/locales.json ×4
  ProviderPin? ExplicitPin)             // null ⇒ automatic (policy) mode

IAimlRouter { Task<AimlResult> InvokeAsync(AimlRequest req, RequestContext ctx, CancellationToken ct) }

AimlResult = Success(AimlPayload output, RoutingReceipt receipt)
           | Failure(AimlFailure cause)
// CLOSED union (S1 PolicyDecision pattern). AimlFailure is an INTERNAL cause union
// (NoRouteConfigured | NotAllowlisted | RefusedPrivacyFloor | BudgetDenied | Timeout
//  | ProviderError | ChainExhausted | InvalidRequest) — never a silent drop (15A failure row).
// FAILURE UNOBSERVABILITY (P3): consumers map ANY failure to their existing standard error path.
// Zero new message keys. RoutingReceipt { InvocationId /* aiv_ ULID */, Provider, Model,
// DecisionSource: policy|explicit|failover, PolicyVersion, FallbackDepth, LatencyMs, TokenCounts }
// is internal telemetry: an arch scan (extends S1's L20 trust-DTO rule with provider*/model*/
// payload_class patterns) proves neither the receipt nor provider identity ever serializes into
// a user-bound DTO. A reported user can never probe moderation-provider health.

// Internal SPI (buy-vs-build seam; never crosses the module boundary):
internal IModelProvider { Descriptor: ProviderDescriptor; ExecuteAsync(ProviderInvocation, ct) }
// ProviderDescriptor { ProviderId, Capabilities: AimlTaskKind[], Transport: Api|LocalProcess,
//                      CredentialRequirement }
// S2 ships ONE provider, `anthropic`, with two TRANSPORTS (P2, adopted over P1 — see §12):
//   AnthropicApiTransport   — Anthropic SDK; the ONLY provider-SDK reference in the backend;
//                             key from Key Vault via S1's IFieldKeyVault/secret path
//   AnthropicLocalTransport — local Claude Code CLI; Development-only DI, [DevSeamsOnly],
//                             arch-tested never-in-prod (IPaymentService family)
//   SeedProvider            — deterministic canned responses; test DI only, NEVER an allowlist
//                             or policy value (S1 DevSeams-not-in-9A ruling)
// PhotoDNA / Content Safety / MT adapters are slots filled by S11/S12/S13: one adapter + one
// founder allowlist entry + one DI line. S2 pre-builds zero vendor code.

// Vendor-egress authorization seam (P3; S17 arms it):
IVendorEgressAuthorizer { Authorize(PayloadClass, subject, ctx): Authorized | Refused }
// S2 registers RefuseAllSpecialCategoryAuthorizer: SpecialCategory ⇒ Refused, always — no code
// path can override it before S17's consent-ledger-backed impl exists. Two independent locks:
// this authorizer + the allowlist bounds rule that refuses saving any special_category_ok:true
// entry until S17 ships (§4).
```

**Routing resolution is deterministic space, structurally (15A's own text: a policy table, never a latent judgment).** `Resolve(policy, allowlist, registeredProviders, task, payloadClass, region) → ProviderChain` is a pure, IO-free function (S1 `Deterministic` discipline, arch-tested), golden-vectored. Effective set = allowlist (9A) ∩ DI-registered (environment truth); a chain resolving empty fails closed. Region rides S1's `RequestContext` law: system-actor calls inherit the SUBJECT's region — T9 triage of a German user's transcript is EU-scoped work. `residency_overrides` is a first-class policy input (empty v0).

**Explicit pin = honored verbatim, no policy override (15A verbatim) — but allowlist and privacy floor still bind:** the pin bypasses the routing *policy*, never the *laws*. A pin to a non-allowlisted provider or above the provider's payload-class ceiling is a typed, audited refusal. The router never silently reroutes a pin; that would be a policy override by another name. (All three proposals agree.)

**Failover (15A failure-mode row):** the Automatic path walks the resolved chain in order; a chain member failing the privacy floor for THIS call is **skipped, not tried** — availability never buys a privacy downgrade, and failover never downgrades to a cheaper model for cost (CLAUDE.md); every hop is audited with `failover_from`. Chain exhausted ⇒ `ChainExhausted` ⇒ consumer's standard error. Served by fallback or standard error — never a silent drop; the closed result union has no code path that returns nothing.

**Audit (3A, the ruling's word):** every `InvokeAsync` appends ONE `aiml.route_decided` event to the existing **audit stream**: `{invocation_id, caller, task, payload_class, subject_ref?, provider, model, transport, decision_source, policy_version, failover_from?, outcome, latency_ms, input_tokens, output_tokens, payload_sha256}`. Region + lawful_basis stamped by the S1 substrate from `RequestContext`. **Metadata only — prompt and completion text NEVER appear in any event payload**, enforced by a closed C# record type + a serialized-shape test. `payload_sha256` (P3) gives dispute/audit joins without content custody. Stream choice reasoning in §12 item 6. Latent inputs/outputs are the consumer's data under the consumer's 13A registrations; the router holds content for the duration of the call and not one second longer. This single law is why §2 and §6 are empty.

**Fail-closed keys (L18 analog, unanimous):** in prod, if the resolved policy can reach the `anthropic` API transport and no key is configured (Key Vault ref, 2A), the host **throws at startup**. Dev transport only under DevSeams. Same shape as S1's `IPaymentService` prod-throw, arch-tested the same way.

### 1c. OpenAPI delta

**Zero paths, zero components — asserted, not assumed (unanimous).** CI drift gate proves `contracts/openapi.v0.json` byte-identical across S2; a test fails if any `Svac.AimlRouter*` type leaks into the document. The E2E proof runs against a test-host-only diagnostic canary (S1's ratified 4A-canary pattern), never shipped in the contract. `contracts/message-keys.json` unchanged — router failures render through consumers' existing `error.generic`; there is no router string to localize, which is the correct i18n answer stated, not an omission (i18n-lint baseline stays clean ×4 locales). Parallel lanes B/C/D are untouched by S2, proven.

## 2. Schema DDL

**None (P1 adopted — see §12 item 1).** No tables, no migration, no EF entity, no DbContext. `ef-gate.sh` proves zero schema delta. Policy state = `core.config_entries` (9A); decision history = `events_audit` (3A); quota counters = S1's `quota_counters`. There is no fourth place. If a future consumer proves a hot-path need for an invocation table or response cache, that store is THAT slice's, with its own 13A registration; the promotion path is one DI decorator behind `IAimlRouter`.

## 3. 4A policy entries

One row, internal chokepoint, S1 `core.ledger.append` precedent — the row's existence makes an ungated route unshippable forever:

| action | actor kinds | axes | denyMode | requires_reason |
|---|---|---|---|---|
| `aiml.invoke` | system only — never client-reachable, never staff-reachable from consumer apps; no request DTO maps to it (structural, arch-asserted) | — | DenyStandard | false (the audit event carries full provenance) |

Allowlist and routing-policy edits ride the EXISTING `core.config.set` rows (founder scope for the allowlist, ops for the rest) — 9A is the mechanism; no parallel config door, no new mutation endpoint, so no other row exists. The generated matrix suite asserts exactly this delta; consumer-actor reachability asserts as a violation, not a gap. **P3's `aiml.eval.run` row is REJECTED:** evals are a dotnet test project (P1), not a runtime verb; there is no mutation to gate, and a policy row for a non-existent surface is theater.

## 4. 9A config entries

Additive manifest `backend/modules/AimlRouter/config/aiml-router.config.json` (S1 union-merge format; sibling file, never edits to domain-core rows). Every entry has a real S2 consumer — the dead-tunable lint holds:

| key | scope | type | v0 | requires_reason | consumer |
|---|---|---|---|---|---|
| `aiml.provider_allowlist` | **founder** | json[] | `[{"name":"anthropic","family":"claude","kinds":["llm"],"payload_class_ceiling":"pseudonymous","dpa_signed":false,"special_category_ok":false,"residency":"global","models":["claude-fable-5"]}]` | true | resolver + adapter registry (both Auto and Explicit paths) |
| `aiml.routing_policy` | ops | json | `{"version":1,"default_chain":[{"provider":"anthropic","model":"claude-fable-5"}],"task_chains":{},"residency_overrides":[]}` | true | pure `Resolve()`; desk-tunable per 15A |
| `aiml.invoke_timeout_seconds` | ops | int | `60` | false | invoker; bounds [5, 300]; feeds the Timeout failure kind |
| `aiml.daily_call_ceiling` | ops | int | `10000` | false | budget circuit breaker (§5) |

**Set-time bounds validation does the ruling's work structurally (P3's mechanism, adopted):** a `routing_policy` naming a provider absent from the allowlist, or a model absent from that provider's declared `models` list (no silent downgrade — best available model by default, CLAUDE.md), or routing a payload class above a provider's ceiling, or whose `default_chain[0]` resolves to `family != "claude"` **fails bounds at `SetValue`** — an unlawful or non-Claude-default policy cannot be *saved*, not merely not-served. 15A's "Claude is the default provider and the policy default" and "never added unprompted" become a CHECK, not a habit. A second bounds rule refuses saving any allowlist entry with `special_category_ok: true` until S17 exists (two locks with the refuse-all authorizer, §1b). Adding a non-Claude provider is a founder act via the allowlist, reachable only through task-chain/failover rows or an explicit pin — audited 3A events end to end, rendered at S5.

**Conservative vendor posture (P3, pending OQ-A):** `anthropic` ships `payload_class_ceiling: "pseudonymous"` and `dpa_signed: false` until counsel's L-1/L-3 package confirms the Anthropic DPA + retention terms; the flip to `personal` is a founder-scope audited config change, zero deploy. Blocks nothing: S2 has no consumers, and the first personal-data consumer (T9, S12) lands after the L-1 window.

**Dropped:** P3's `aiml.failover_max_attempts` (chain composition IS the failover policy; a second knob for the same thing is a dead tunable) and `aiml.invocation_retention_days` (no table to sweep).

## 5. 10A quota keys

One internal key, live, the router itself as consumer (P3 adopted — see §12 item 4):

- **`aiml.call.daily`** — actor = `sys_<caller_module>`, window daily/UTC, cap = `aiml.daily_call_ceiling`, via S1's one `Consume` verb on existing `quota_counters`. The runaway-spend/runaway-loop circuit breaker at the single egress, day one. Deny ⇒ `Failure(BudgetDenied)`, audited outcome on the route event — it never renders the `LimitReached` UI because no system actor has a UI; token law 4's one limit surface is untouched.

No consumer-facing keys (unanimous; they belong to consuming slices, priced against real denial behavior — T9's report volume is S12's problem). Reserved naming convention for those: `aiml.<task>.<window>`; any such cap is consumed at the CONSUMER's call site, upstream of the router, so the router never grows a second deny shape.

## 6. 13A store registrations

**Zero new registrations — a design theorem, not an omission (P1):** the router owns no store. `events_audit` is registered by S1 with its OQ-1-ratified verbs (retain, tombstone actor PII in payload); `aiml.route_decided` events are metadata-only, so subject purge rides existing audit-stream machinery with nothing new to register. `quota_counters` is S1's registered store. The purge-completeness suite needs no new cells. Honesty checks: the 13A CI gate re-runs green after S2 (already build-failing on unregistered stores), and a test asserts the module's EF surface contribution is empty.

## 7. Notification taxonomy rows

**Zero — asserted (unanimous, S1 closure-baseline pattern).** System actor; no consumer-visible state change exists. Provider-outage/chain-exhausted alerting is operator paging (S5 desk material), outside the nine-category consumer taxonomy. Recorded for S12 (P3's note): when T9 becomes SLA-bearing, "router chain exhausted for ModerateText" joins the page-now classes beside IDV-vendor outage. S4 inherits a clean baseline.

## 8. BUILD.md §9 seams made concrete

| Seam | S2 concrete form |
|---|---|
| 15A router chokepoint | `ProviderSdkArchTest` graduates vacuous → ARMED: all-assembly scan; provider-SDK references legal ONLY in `Svac.AimlRouter` (internal, never Contracts); **red-fixture proven both directions** (SDK ref outside the module fails; the same ref inside passes). Second rule: consumers reference `Svac.AimlRouter.Contracts` only. Third (P3): gate-hook source lint fails any inference-endpoint literal (`api.anthropic.com`-class) outside the module |
| Schema-per-context (1A) | Contracts vs internal split; first `backend/modules/*` template set here; opaque `aiv_`/`ActorRef` ids; zero cross-module reach; first non-vacuous exercise of S1's module-boundary tests |
| Buy-vs-build, one interface per vendor | `IModelProvider` SPI + `ProviderDescriptor`; `anthropic` adapters now; any future vendor = one adapter + one founder allowlist row + one DI line; local transport + SeedProvider `[DevSeamsOnly]`, arch-tested never-in-prod-DI (S1 test family extended) |
| Model-door secrets fail CLOSED (L18 analog) | prod + policy-reachable API transport + no Key Vault key = startup throw; dev fallback only under DevSeams; tested |
| Deterministic math in pure libs | `Resolve()` pure + IO-free (arch-tested) with golden vectors: allowlist ∩ registered, chain order, region variants, floor-skips, explicit-pin refusal, empty-chain fail-closed, Claude-default bounds rejection. The LATENT part of any router call is only what is behind the provider adapter (CLAUDE.md split rule) |
| Region-first PII (L21) | region + lawful_basis stamped on every `aiml.route_decided` event by the S1 substrate; subject-region inheritance for system actors; `residency_overrides` a first-class resolver input |
| Server-authoritative trust (L20) | no request DTO maps to `aiml.invoke`; trust-DTO arch scan extended with `provider*/model*/payload_class` patterns — receipt/provider identity never in user-bound DTOs |
| Silent rejection unobservable | one internal failure union → one downstream standard error; single code path for all refusal modes (timing-structural, S1 pattern); zero new message keys |
| Consent WRITTEN, not just honored | `IVendorEgressAuthorizer` refuse-all-special-category default: special-category egress cannot exist before the ledger that could authorize it (S17); allowlist bounds rule is the second lock |
| Transactional outbox (3A) | `aiml.route_decided` append via `IEventStore.Append` in the router's scope; the event IS the record (no table), so decision and record cannot diverge |
| Analytics written AND received | E2E reads the route event BACK off the audit stream and asserts the watermark row — never emit-only (the hmg scar) |
| Foreign-event skip (§8 cl.7) | router emits, consumes no streams, registers zero projections — N/A-with-note, asserted by test, so no vacuous consumer appears |
| i18n (DR-7.7 / token law 2) | zero new keys asserted; `TargetLocale` validated ∈ `i18n/locales.json` ×4; canonical values returned, consumer owns dual-text (T8) |
| Concurrency | no shared mutable state: chain walk is per-request; quota rides S1's counters; idempotency deliberately absent — the first consumer with retry semantics designs it against a real duplicate cost |

## 9. Dependency classification (every not-yet-built system S2 reads)

| Dependency | Class | Handling |
|---|---|---|
| S1 substrate (9A/3A/4A/10A, RequestContext, DevSeams, IFieldKeyVault, arch harness, config manifests) | **built** | consumed as-is; S2 is the second real consumer, which is itself a test of S1's seams |
| Anthropic API key / Azure Key Vault (OQ-3, no subscription yet) | seam-now | dev transport = local Claude Code, no key, no per-call cost (15A); prod wiring = config against the S0-reserved Key Vault path; fail-closed boot test ships now; nothing blocks S2 green |
| Counsel L-1/L-3 (DPA posture) | seam-now | allowlist privacy flags conservative (ceiling `pseudonymous`); counsel outcome = audited founder config flip, zero deploy (OQ-A) |
| Consent ledger (S17) | seam-now | `IVendorEgressAuthorizer` refuse-all default; S17 swaps the impl; provenance on every event means upgrading authorization never rewrites recorded basis |
| Metrics & Ops desk (S5) | swap-safe | `aiml.*` config entries + route events land now in S1 formats; desk editor and tiles (volume, failover rate, latency, provider mix, policy version in force) render later from data already flowing |
| Consumers: T9 (S12), T8 (S13), vectors (S25), consultant (S36) | **not read** — deliberate | task kinds pre-declared from their ratified specs; empty policy chains keep unbuilt kinds fail-closed; each consumer's activation = adapter/policy data; prompts + consumer evals stay theirs |
| S11 media stack (PhotoDNA / Content Safety) | deferred by design | ModerateImage kind + media payload shape arrive as ONE additive versioned contract change at S11; adapter slot exists in the SPI |
| Local Claude Code CLI on the dev box | dev-only, eval lane | gate tests use SeedProvider (free, deterministic, <2s); evals require the CLI and say so in their skip message |
| Redis | deliberately unused | quota rides S1's Postgres counters |
| MUST-BUILD-FIRST blocking S2 | **none** | S1 is ratified and landed; S2 starts now |

## 10. Outcome moved + evidence at sign-off

Ledger row: **"arch test: zero provider SDK refs outside router"** — B4 dies (T9, T8, vector drafts, consultant all become buildable).

1. **Armed, non-vacuous 15A arch test:** red fixture with a provider-SDK ref outside the module fails the build; the same ref inside `Svac.AimlRouter` passes; Contracts stays SDK-free; endpoint-literal lint red-fixture-proven; Contracts-only consumer rule proven.
2. **Deterministic gate suite (<2s, seed/faked providers):** resolver golden vectors (Auto chain, Explicit verbatim, allowlist deny incl. pinned, privacy-floor skip, region variants, empty-chain `NoRouteConfigured`, failover order + exhaustion); 9A bounds rejections (unregistered provider, undeclared model, ceiling violation, non-Claude default, special_category_ok); fail-closed prod boot throw; DevSeams transports never-in-prod-DI; budget-deny path; DTO trust scan; serialized event shape carries no content fields.
3. **Live E2E** (test-host diagnostic canary, `backend/e2e/aiml-router.e2e.mjs` against compose): `InvokeAsync(EvalProbe, Automatic)` → Success → `aiml.route_decided` read BACK off `events_audit` with correct policy version, region/lawful_basis stamped, watermark advanced → failover drill (primary transport killed, chain serves, both hops audited) → privacy-floor refusal drill (Personal payload vs pseudonymous ceiling ⇒ `RefusedPrivacyFloor` on the event, standard error out) → budget drill → fresh-boot clause → zero-exception log sweep.
4. **Evals — the first real member of the periodic lane (15A: own tests + evals before any second consumer); this harness is the reusable one every later latent surface runs on** (S12/S13 evals become fixture files + thresholds against it, not new harnesses): via local transport, no key, no spend — structured-output schema conformance ≥95% over a checked-in canary prompt set (N=20); refusal-honesty probe; failover-under-real-backend end to end; latency recorded as baseline. Checked-in fixtures only; production content never becomes eval data. Tooling: a dotnet test project, trait-filtered — unless a layer-2 scan finds a .NET eval lib that beats ~100 lines of xUnit (search-before-building, S1's zero-dependency precedent sets the bar).
5. **Drift gates:** `contracts/openapi.v0.json` byte-identical; `message-keys.json` unchanged; `ef-gate.sh` zero schema delta — lanes B/C/D untouched, proven.
6. **Renders:** until S5, Actions status + committed arch-rule count + eval baseline in the slice retro (S0 interim posture); at S5, desk tiles from the audit stream and `aiml.*` config entries already flowing.

## 11. Open questions for Julien

**OQ-A — vendor-egress ceiling interim (from P3; genuinely a legal posture, not a design call):** ratify the conservative default — `anthropic` ceiling `pseudonymous`, `dpa_signed:false`, flipped to `personal` only on counsel's L-1/L-3 confirmation of the Anthropic DPA + retention terms, via audited founder-scope config change (zero deploy). Reversible at any moment; forecloses nothing; no consumer needs `personal` egress before S12. Judge recommendation: yes.

No other question forks this slice. Everything below in §12 is resolved with stated reasoning and is ops- or founder-revisable from the desk without a contract change.

## 12. Judge's synthesis record (conflicts + the one ruling contradiction)

1. **Persistence: ZERO tables (P1) over `aiml.invocations` (P2, P3).** 15A's own text says routing is "audited on the 3A event substrate" — the ruling names the stream, not a table. A table duplicates the event, adds a migration, a 13A row, a retention config, and a sweep worker, all to store what the audit stream already holds; desk tiles render from streams (S1's ratified pattern). P3's `payload_sha256` and region/lawful_basis survive as event-payload fields (the substrate stamps the latter). Promotion path if load evidence demands it: a consumer-slice store behind a DI decorator.
2. **Provider vs transport (P2) over dev providers in config (P1). RULING CONTRADICTION, P1 loses the point automatically:** P1's allowlist v0 `["claude_api","claude_local","seed"]` and default chain `["claude_local","claude_api"]` put DevSeams backends into founder/ops 9A values — contradicting S1's ratified ruling "DevSeams is an environment flag, NOT a 9A entry; a runtime-tunable that swaps fake backends from the ops desk must be structurally impossible." Adopted: one provider `anthropic`; local-vs-API is a transport selected by environment, Development-only DI; SeedProvider is test DI only. A desk edit can never route prod to a keyless local process, structurally.
3. **One verb `InvokeAsync` (P1, P3) over three capability verbs (P2); pre-declared task kinds (P2, P3) over `{ General }` (P1).** P2's `TranslateAsync`/`ScanMediaAsync` design payloads for absent consumers — speculative surface, rejected. P1's single `General` forces a contract change per consumer — rejected the other way. Synthesis: one verb, closed `AimlTaskKind` pre-declared from RATIFIED specs only (text-shaped), so consumer activation is policy data; ModerateImage excluded because its payload is unknowable before S11 (one additive change there, kind + payload together).
4. **Quota breaker `aiml.call.daily` (P3) over zero keys (P1, P2).** P1/P2's objection — quotas belong to consuming slices — governs consumer-facing keys and stands (§5). It does not apply to router-owned spend protection at the single egress: the router is the consumer, the deny is typed and testable in-slice, and it rides S1's existing counters. Retrofitting the breaker at S12 is exactly the "later" the build rules ban for protective infrastructure.
5. **PayloadClass + ceilings + refuse-all special-category (P3) adopted.** Retrofitting egress classification onto four consumers later is the named rewrite; at S2 it is one required enum field, one pure-function input, and one bounds rule. The counsel-dependent ceiling value is OQ-A; the mechanism is not.
6. **Stream: audit (P1, P3 majority) over behavioral-primary (P2); ONE event per invoke, not P3's two.** 15A says "audited"; the audit stream survives deletion with tombstoned actor PII (S1 OQ-1a), which suits a decision record. Telemetry fields ride inside the same event payload — one append, not two. P2's volume objection is real at T9 scale: S26's burst test (T15) is the named checkpoint to revisit sampling or a behavioral split, with load evidence.
7. **Failure surface: P3's unobservability over P1's per-failure `MessageKey`.** No user ever sees a router error directly; per-cause message keys would build a rendering surface for an actor that has no UI and leak provider state to adversaries. Zero new keys; consumers map to their standard error.
8. **`aiml.eval.run` 4A row (P3) rejected; evals as a test project (P1) adopted.** No runtime verb exists to gate.
9. **Dropped tunables:** `failover_max_attempts` (P3 — chain composition is the failover policy) and `invocation_retention_days` (P3 — no table). Dead-tunable lint stays honest.
10. **Unanimous points adopted as written:** zero OpenAPI delta drift-gated with test-host canary; explicit pin verbatim with allowlist binding; fail-closed startup on missing prod key; pure golden-vectored resolver; `aiml.invoke` system-only 4A row; zero notification rows; ProviderSdkArchTest armed with red fixtures; module template precedent; nothing blocks S2 — it starts now.

---

## 13. RATIFICATION (orchestrator, 2026-07-10 — Julien's in-absence pre-approval)

Contract **RATIFIED** with two corrections that bind the build. Julien's standing rule: in-absence ratification of the current slice's contract is pre-approved; it does not auto-start the next slice.

**Scope ratified as written.** System-only, in-process router; zero HTTP paths, zero DDL, zero 13A registrations, zero notification rows, zero new message keys. The one structural law (§0) — exactly one backend assembly may reference a provider SDK, build fails otherwise — is the whole deliverable. This is the correct read of 15A ("audited on the 3A event substrate" names a stream, not a table); §12 items 1/6 stand.

**OQ-A — RATIFIED conservative, reversible (mirrors S1 OQ-1a).** `anthropic` ships `payload_class_ceiling: "pseudonymous"`, `dpa_signed: false`. Flip to `personal` only on counsel's L-1/L-3 confirmation of the Anthropic DPA + retention terms, via founder-scope audited config change, zero deploy. Reversible at any moment; forecloses nothing; no consumer needs `personal` egress before T9 (S12), which lands after the L-1 window. The refuse-all-special-category authorizer + the allowlist bounds rule that refuses saving `special_category_ok:true` before S17 both stay armed (§1b, §4). Judge recommended yes; ratified yes.

**CORRECTION 1 — v0 default model is the best-available Claude, not `claude-fable-5`.** CLAUDE.md law: "Always use the best available model by default; no silent downgrades to a cheaper or smaller model for cost." A specialized/smaller model as the router-wide default IS that downgrade. The build MUST set, in both places:
- `aiml.provider_allowlist[0].models` → `["claude-opus-4-8"]`
- `aiml.routing_policy.default_chain[0].model` → `"claude-opus-4-8"`

Doctrine (record it in the module README + the resolver golden-vector comment): the Automatic-path default is the best-available general-purpose Claude model. A consumer that wants a faster/cheaper Claude model (e.g. latency-critical T9 moderation at report scale) does so ONLY by an explicit `ProviderPin` with a stated reason — honored verbatim (§1b), audited as `decision_source=explicit`, never a silent default. The §4 bounds rule already enforces `family=="claude"` on `default_chain[0]`; do NOT add a machine "best-available" ranking oracle (over-engineering) — the value is set correctly at v0 and the doctrine governs changes. The allowlist `models` list must contain whatever `default_chain[0].model` names, or set-time bounds must reject the policy (existing rule); keep them consistent.

**CORRECTION 2 — Auth-F3 does NOT belong to S2; re-target it.** The S1 defer assumed S2 would be "the first consumer resource endpoint." It is not: S2 exposes zero HTTP endpoints, no request DTO maps to `aiml.invoke`, and the 4A row is action-level system-only with no resource id (§1c, §3). There is no route-value target to bind, so the IDOR surface Auth-F3 describes (`TargetRef.ForAction` hardcoding `ResourceId=null`, `PolicyEnforcementFilter.cs:23,50-51`) does not exist here. The `RequirePolicyAction` redesign is NOT in S2 scope.
- **Re-carry Auth-F3** to the first slice that adds a **client-reachable, resource-scoped 4A action** (a read or mutation authorized against a specific target id — a profile/conversation/character endpoint, not the router).
- **S2 security phase (auth/IDOR lens) must instead CONFIRM the absence:** assert `aiml.invoke` carries no target resource, no request DTO maps to it, and no consumer-actor reachability exists (§3 already claims this as an arch assertion — the lens verifies it, red-fixture both directions). That confirmation, not the redesign, is the S2 auth deliverable.

**Carried into future contracts (unchanged from S1):** Auth-F3 → first client-reachable resource-scoped 4A slice (per Correction 2); Concurrency-F5 → S14; SilentRej-L4 → first policy-gated consumer read.

Ratified. Proceeding to Phases 1-3 (scaffold → build → security) through THE HARDENED GATE.

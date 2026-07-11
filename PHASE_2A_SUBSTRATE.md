# PHASE_2A_SUBSTRATE.md — the ONE combined domain-core mutation (S3 + S5 + S6)

**Status: reconciled + ratified for execution (orchestrator, 2026-07-11).** This is the single Phase-2a
surgery on the DONE domain-core assemblies, designed once with all three LOCKED contracts in hand
(SLICE_S3_CONTRACT §0/§13, SLICE_S5_CONTRACT §0/§1d/§13, SLICE_S6_CONTRACT §0/§366). It exists so the
shared substrate is mutated a SINGLE time — not three — before any feature builder fans out. Gate before
fan-out (SLICE_PLAYBOOK 2a): **byte-identical-behavior proof on S1/S2 suites + rerun of the S1 live E2E**,
verified by me, twice, never on agent word.

Reconciliation verdict: **no conflict, no fork.** Both S3 and S5 explicitly declare their deltas "additive
and disjoint; whichever lane merges second rebases mechanically" (S5 §0 line 22). S6's footprint is one
enum member. Everything below composes.

## The invariant this surgery must preserve

Nothing in S1 or S2 changes behavior. Every addition is either (a) a brand-new type/interface no S1/S2 code
references, or (b) an additive nullable field / new-default that leaves existing decisions identical. The
proof is not "it compiles" — it is `Svac.Tests.Architecture` + `Svac.Tests.DomainCore` + `Svac.Tests.AimlRouter`
green UNCHANGED, and the S1 substrate E2E green unchanged, BEFORE any identity/admin/anime code exists.

---

## 1. `Svac.DomainCore.Contracts.Policy` — the chokepoint composition

The load-bearing reconciliation. `PolicyTableEntry` gains THREE additive things at once; the engine ANDs
three independent axes; the table becomes a union of sources.

- **[S3] `PolicyTargetBinding` (closed union, new file):** `None | SelfAccount | FromRoute(string paramName, string resourceType)`. How a mapping CONVEYS the target. `None` = today's `TargetRef.ForAction(action)`.
- **[S3] `TargetRule` (closed union, new file):** `ActionScoped | SelfOnly | OwnedResource(string resourceType)`. What a row DEMANDS. New additive column on `PolicyTableEntry` (default `ActionScoped` = S1/S2 behavior).
- **[S3] `IResourceOwnershipResolver { string ResourceType { get; } Task<OpaqueId?> OwnerOf(string resourceId, CancellationToken ct); }`** (new file). Registered per resource type by the owning module (identity: session/device/export). Unknown id ⇒ null ⇒ deny-as-absence.
- **[S3] account-state axis:** additive column `IReadOnlySet<string>? AllowedAccountStates = null` on `PolicyTableEntry` (null = "any"); `PolicyAxis += AccountState = 32` for the CI matrix generator. Value flows from `RequestContext.AccountState` (§4), resolved server-side from the session join, NEVER client input.
- **[S5] `StaffRole` (closed enum, new file):** `SuperAdmin, SafetyAgent, ContentModerator, VenueConOps, EconomyOps, Analyst`. NOT a new `ActorKind` (`Staff` already exists, S1).
- **[S5] `PolicyTableEntry += IReadOnlySet<StaffRole>? StaffRoles = null`** (null = no role restriction). `StaffRoleAllowlistNote` retained as provenance.
- **[S5] `IStaffRoleResolver { Task<IReadOnlySet<StaffRole>> GrantsOf(ActorRef staff, CancellationToken ct); }`** (new file) + `DenyAllStaffRoleResolver` default impl (fail-closed, ThrowingPaymentService family; empty set).
- **[S5] `IPolicyTableSource { IReadOnlyList<PolicyTableEntry> Entries { get; } }`** (new file). **THE shared table mechanism.** The concrete `IPolicyTable` becomes the boot-time UNION of all registered sources; a duplicate action key across sources ⇒ boot refusal. This SUPERSEDES S3 §3b's "regenerates from this table" phrasing: S3 ships `IdentityPolicyTableSource` (module-owned), S5 ships `AdminPolicyTableSource` (module-owned), S6 ships `AnimePolicyTableSource` (module-owned); domain-core rows become `CorePolicyTableSource` (source #1, this surgery). No slice ever edits the table directly.

**The chokepoint goes ASYNC (forced, not optional).** `IResourceOwnershipResolver.OwnerOf` and `IStaffRoleResolver.GrantsOf` are async DB reads, so `IPolicyEngine.Authorize` changes signature from `PolicyDecision Authorize(ActorRef, string, TargetRef)` to `Task<PolicyDecision> Authorize(ActorRef actor, string action, TargetRef target, CancellationToken ct = default)`. This is a signature change to a DONE interface, but byte-identical in DECISIONS — every existing decision is unchanged, callers just `await`. Ripple is exactly TWO places (both updated in this surgery, both re-proven green): the Hosting `PolicyEnforcementFilter` (already async — `InvokeAsync`) and `PolicyEngineTests`. No module calls `Authorize` directly (S2's `aiml.invoke` is the S2-A deferred finding — the router does not enforce via the engine yet), so the ripple stops there. The engine gains constructor deps: `IEnumerable<IResourceOwnershipResolver>` (indexed by `ResourceType`; none registered at S1/S2) + `IStaffRoleResolver` (the `DenyAllStaffRoleResolver` default). The consumer-denial coercion (User/Anonymous ⇒ always `DenyAsAbsence`) and the fail-closed unmapped-action path are PRESERVED verbatim — they are load-bearing SilentRej-L1/L2/L3 fixes.

**Engine (`Svac.DomainCore/Policy/PolicyEngine.cs`) — three ANDed axes, all independent:**
1. **Target-ownership (S3):** `SelfOnly` ⇒ deny unless `target.ResourceId == actor.Id`. `OwnedResource(t)` ⇒ call the registered `IResourceOwnershipResolver` for `t`; owner null or ≠ actor ⇒ deny **as the row's denyMode** (`DenyAsAbsence` for these rows — folded into the query predicate so not-found and not-owned are one path; this is what discharges SilentRej-L4).
2. **accountState (S3):** if `AllowedAccountStates != null` and actor is `User`, require `ctx.AccountState ∈ AllowedAccountStates`.
3. **Role (S5):** if `actor.Kind == Staff` and `row.StaffRoles != null`, require `grants ∩ row.StaffRoles ≠ ∅` (grants from `IStaffRoleResolver`).
Order is irrelevant (all ANDed); a deny at any axis returns that row's `DenyMode`. S1/S2 rows carry `ActionScoped` + null state + null roles ⇒ every existing decision is byte-identical.

**Boot refusal (`Svac.DomainCore.Hosting/StartupPolicyCoverage.cs`, extended, fail-closed BOTH directions, red-fixtured):** a row declaring `SelfOnly`/`OwnedResource` whose endpoint mapping binds `None` ⇒ refuse; a `FromRoute(param,…)` naming a param absent from the route pattern ⇒ refuse; a `None`/`ActionScoped` row given a route binding ⇒ refuse. Existing `RequireMutationsPolicyMapped()` unchanged and reused.

**[S5] core-row typing (data, `Svac.DomainCore/Policy/PolicyTable.cs` → `CorePolicyTableSource`):** `core.config.set.founder → {SuperAdmin}`; `core.config.set.ops → {SuperAdmin, EconomyOps}`; `core.ledger.reverse → {SuperAdmin, EconomyOps}`; `core.purge.execute` staff leg `→ {SuperAdmin}`. No semantic change (engine still denies consumers as before); byte-identical-behavior because no Staff actor exists in S1/S2 suites.

## 2. `Svac.DomainCore.Contracts` — new seams (buy-side + registry + reads), all unreferenced by S1/S2

- **[S3] Email seam (new `Email/` folder, IPaymentService precedent):** `IEmailSender { Task<EmailResult> SendAsync(EmailMessage msg, RequestContext ctx, CancellationToken ct); }`; `EmailMessage { To, TemplateKey, Locale, IReadOnlyDictionary<string,string> Model }` (keyed ×4, never prose); `EmailResult = Sent(providerRef) | Failed(reasonKey)` (closed union). Fail-closed default `ThrowingEmailSender` lives in `Svac.DomainCore/Email/` (no external dep). **The concrete `SmtpEmailSender` transport (with its SMTP client package dependency) is DEFERRED to the S3 build** — it is first-consumed and E2E-tested against Mailpit there; adding a MailKit/SMTP package to domain-core in this surgery is needless churn. Phase-2a ships the seam + the throwing default only. Neither is DI-registered by S1/S2 hosts → no consumer → no boot throw (S2 retro: fail-closed guard fires only when a typed consumer depends on it). Transport selection is env/config, NEVER a 9A entry.
- **[S3] Export registry (new `Export/` folder):** `IExportSink` (writes `(path, schema-versioned JSON)`); `IExportContributor { string StoreKey { get; } Task ContributeAsync(SubjectRef subject, IExportSink sink, CancellationToken ct); }`; disposition union `ExportDisposition = Contributes | NotExportable(reason) | Withheld(basisRef)`. This is registry machinery every module registers into (like 13A) — MUST live in domain-core so a registrant never references identity. The `export-registry.json` file + the export⋈purge CI cross-gate are created by the S3 build (not this surgery — no `export-registry.json` exists yet).
- **[S3] Consent plumbing (new `Consent/` folder — distinct from the existing `Con/` con-events folder):** `ConsentKind` (CLOSED): `AgeAttestation18Plus | TermsAcceptance | PushCategory(int 1..7,9) | IrlAccess | BackgroundLocation | SpecialCategoryIdentity | IdentityVerification | Marketing`. **PushCategory(8) is unrepresentable — "never mutable" is a type fact, not a check.** `IConsentLedgerWriter { Task Record(SubjectRef subject, ConsentKind kind, string version, string surface, ConsentDecision decision, RequestContext ctx, ...); }` where `ConsentDecision = Granted | Revoked`. Appends ONE `consent.recorded` to `events_consent` in the caller's tx.
- **[S5] `StaffContext` (new file, root Contracts):** `sealed record StaffContext(OpaqueId StaffId, IReadOnlySet<StaffRole> RolesHeld, StaffRole ActingHat)`.
- **[S5] `IConfigRegistry += IReadOnlyList<ConfigEntryView> ListEntries();`** + `ConfigEntryView { key, type, scope, value, bounds, requiresReason, updatedAt, updatedBy }` (new DTO).
- **[S5] `IAuditReader` (new `Audit/` folder):** `AuditPage Query(AuditFilter filter, CursorPage page);` + DTOs `AuditFilter { eventTypePrefix?, actorRef?, streamId?, from?, to? }`, `AuditPage`. **No PublicApi endpoint may map this** (staff-only reachability, arch-gated at S5).
- **[S5] `IPurgeRunReader` (Purge folder):** `PurgeRunPage Recent(CursorPage page);` + `PurgeRunPage`.
- **[S5] `CursorPage` (new shared DTO, root Contracts or Api/):** opaque-cursor pagination request/response shape. Single definition; S3 §1c already lists `CursorPage` under "Reuse".
- **[S3] `IdPrefixes += ses, dev, chl, exp, del`; [S5] += srg** (`stf` ALREADY exists in `OpaqueId.cs` — do NOT re-add it). Additive const registrations; ActorPrefixConsistency arch test extends only for actor-kind prefixes (these are resource ids, not actor ids — confirm the test's scope before adding).

## 3. `Svac.DomainCore.Deterministic` — pure, golden-vectored, all new files

- **[S3] `AgeMath`** — `AgeYears(birthdate, asOf)`, `IsAtLeast(birthdate, years, asOf)`; **18/13 floors are CODE CONSTANTS here** (no 9A key may match `age|floor|minor`); Feb-29 → Mar-1 rule; future date = invalid input, never a verdict. Golden-vector file **shared verbatim with iOS + Android + web** test suites (a lint compares file-to-file).
- **[S3] `HandleRules`** — NFKC-fold + lowercase canonicalization, charset/length lock, confusable rejection. Golden-vectored.
- **[S5] `HatFor(action, grants)`** — least-privileged role among grants satisfying a row (SuperAdmin > four operational > Analyst; ties by enum ordinal). Golden-vectored.
- **[S5] cursor/pagination math** — pure encode/decode for `CursorPage`. Golden-vectored.

## 4. `Svac.DomainCore.Hosting` — the auth seam + policy-source wiring

- **[S3] `IBearerAuthenticator` (new file):** `Task<AuthenticatedActor?> Authenticate(HttpContext httpContext, CancellationToken ct);` returning `AuthenticatedActor { ActorRef Actor, string? AccountState, RegionCode? Region, string? Locale }` or null. Default registered in `AddSvacHosting` = `AnonymousBearerAuthenticator` (returns null). `RequestContextMiddleware` calls it; on null it builds the SAME anonymous actor it builds today (keep `BuildAnonymousId` verbatim) ⇒ **byte-identical S1/S2**. On non-null it folds `AccountState` + region/locale into `RequestContext`. Identity registers the session-backed resolver (S3 build); admin host uses cookie auth with its own resolver (S5 build); partner host later (S29). One middleware, three credential systems.
- **[S3] `RequestContext += string? AccountState = null` (§2 file lives in Contracts); [S5] += StaffContext? Staff = null.** Both null-defaulted ⇒ byte-identical. `RequestContext.System(...)` factory keeps both null.
- **[S3] `PolicyEnforcementFilter` + `RequirePolicyAction(action, PolicyTargetBinding)` overload:** existing no-arg overload = `PolicyTargetBinding.None` (byte-identical). The filter resolves `TargetRef` from the binding — `SelfAccount` ⇒ `("account", actor.Id)` from the SESSION; `FromRoute(param, type)` ⇒ `(type, routeValues[param])` — then calls `Authorize`. Ownership resolution happens inside the engine via the registered resolver.
- **[S5] `DenyAllStaffRoleResolver` default DI registration on EVERY host** (fail-closed). Admin host overrides (module).
- **[S5] `IPolicyTableSource` union assembly + duplicate-key boot refusal** wired into the Hosting startup pipeline; `IPolicyTable` resolves to the union. With only `CorePolicyTableSource` registered (S1/S2), the union == today's table exactly.

## 5. Shared registries / contracts artifacts

- **`backend/domain-core/purge-registry.json`** — this surgery makes NO edits. Identity's ~11 store rows land in the S3 build; admin's 2 staff rows in the S5 build; anime's 5 rows in the S6 build. (Kept out of Phase-2a so the shared file churns per-slice, not in the shared surgery; each slice re-proves the purge-completeness gate non-vacuous.)
- **`contracts/openapi.v0.json` + `contracts/message-keys.json`** — NO edits in this surgery. S5 asserts both byte-identical (drift-gated; a test fails if any `Svac.AdminHost*` type appears). S3 adds the first real paths in the S3 build; S6 adds anime paths in the S6 build.
- **`export-registry.json`** — does not exist; created by the S3 build.

## 6. Explicitly OUT of this surgery (so #19 stays bounded)

- **S6 `FieldEncryptionPurpose.AnimeAnswers`** — this IS in the surgery (§ below), but its `purge-registry.json` rows + openapi/message-keys deltas are S6-build.
- **S5 config-manifest `pending_consumer_slice` field + dead-tunable-lint change** — shared node TOOLING (`tools/`), not a DONE .NET assembly; purely additive (accept an optional field); only S5 exercises it. Rides the S5 build. Folding it here would churn #19 for zero S3/S6 benefit and needs no S1/S2 .NET re-proof.
- **All per-slice CONTENT** — identity/admin/anime policy rows, DDL, endpoint handlers, module DI. Each slice's own build phase.

## 7. `Svac.DomainCore.Contracts.FieldEncryption` + `Svac.DomainCore` — S6's one member

- **[S6] `FieldEncryptionPurpose += AnimeAnswers`** (`IFieldEncryptor.cs`, currently `{SpecialCategory, Birthdate, VerificationAudit, IdentityExclusionFilters}`).
- **[S6] `AesFieldEncryptor.PurposeKeyName` case:** `FieldEncryptionPurpose.AnimeAnswers => "field-enc-anime-answers-v1"` (the switch has a throwing default — the new member REQUIRES this case or every anime answer read/write throws). `PurgePipeline` iterates `Enum.GetValues<FieldEncryptionPurpose>()`, so crypto-shred wiring flows automatically.
- Byte-identical: no S1/S2 code encrypts under `AnimeAnswers`.

## 8. Execution + gate

Single sequential chain (one shared unit — the playbook forbids parallel agents on one unit). Order:
Contracts types → Deterministic (with golden vectors) → DomainCore engine/impl → Hosting wiring → build clean → **byte-identical proof**. Then STOP for identity builders to fan out.

**Gate (I verify, twice, never on agent word):** `dotnet build` 0/0 · `Svac.Tests.Architecture` green with new rules red-fixture-proven both directions (target-binding boot refusal both directions; the two S1 lens tests still SKIPPED here — they un-skip at S3 when the identity resolvers exist) · `Svac.Tests.DomainCore` + `Svac.Tests.AimlRouter` **byte-identical green (unchanged counts)** · ef-gate idempotent if any migration touched (the two S5 audit indexes are additive DDL — land in S5 build, not here, to keep the migration chain untouched at Phase-2a) · compose fresh-boot clean · **S1 substrate live E2E green unchanged**. Suite run TWICE.

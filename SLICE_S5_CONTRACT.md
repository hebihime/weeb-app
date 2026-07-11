# SLICE S5 CONTRACT — admin-foundation (LOCKED)

**Gate:** G0 · **Actor journey finished:** operator signs in (MFA), acts with reason, everything audited; founder edits config from the desk · **Unit owned:** `backend/admin-host` (Blazor) — LANE F, a HOST over S1's domain core, not a module · **Depends:** S1 (landed; S2 landed feeds tiles) · **Kills:** B6 (partial) · **Ledger outcome:** config change = audited 3A event, rendered from registry (BUILD.md §7 S5 row).

Synthesized from three proposals by the design judge. Per-conflict adoptions are recorded in §12. One open question remains (§11); everything else is resolved with stated reasoning. No proposal contradicted a LOCKED ruling — all three honored eng-review dispositions 13/14 instead of relitigating them; the synthesis conflicts were architecture calls, adjudicated below.

**Governing theses (grafted):**

- **From P1 (simplicity):** S5 adds almost no mechanism because S1 already built the mechanisms. The admin host is a thin structural CLIENT of the four pillars: config edits ride the existing `config.set` (bounds + reason + same-tx audit already enforced at `ConfigRegistry.SetValue`), the immutable audit is a READ of `events_audit`, the dashboard renders events already flowing. Two tables total, zero session store, zero parallel audit store, zero OpenAPI delta.
- **From P2 (extraction-grade seams):** S5 is the chassis, not a portal. Every deliverable is a registration seam — desk, tile, audited verb, policy rows, search source, auth transport — and S5's own five surfaces are the first registrants, so every seam is non-vacuous on day one. Later desks (S8/S12/S18/S25/S29/S30/S33) add themselves with additive files + one DI registration and zero S5 edits.
- **From P3 (security/audit/RBAC rigor):** the admin host is the staff trust boundary. Four properties become build-failing tests before any desk exists: MFA'd external auth fail-closed in OUR code; a 4A role axis that is finally typed and engine-enforced; exactly ONE immutable 3A audit event per staff action, same-tx, stamped with which hat acted; staff power expressible in exactly one deploy unit. An unaudited, reason-less, or role-less staff action cannot compile, boot, or ship.

---

## 0. Scope ruling

**S5 owns:**

- `backend/admin-host/Svac.AdminHost` (host: auth shell, layout, desk/tile registration, the five v0 surfaces) + `backend/admin-host/Svac.AdminHost.Domain` (internal, `InternalsVisibleTo` tests: `AdminDbContext` schema `admin`, staff directory, `AdminActionExecutor`, role resolver, policy source, auth transports, tile sources) — the S2 module-template assembly discipline applied to a host.
- `backend/tests/Svac.Tests.AdminHost` + arch-test extensions in `Svac.Tests.Architecture` + `backend/e2e/admin-host.e2e.mjs`.
- Two additive 9A manifests: `backend/admin-host/config/admin-host.config.json` (host tunables, §4) and `backend/admin-host/config/v0-batch.config.json` (the eng-review §5 batch, §4) — sibling manifests per S1's union-merge format, never edits to `domain-core.config.json` rows.
- **One additive, versioned change to `Svac.DomainCore.Contracts` / `Svac.DomainCore` / `Svac.DomainCore.Hosting`** (§1d) — run as a Phase-2a mutation per SLICE_PLAYBOOK: mutate → byte-identical-behavior proof for existing consumers → rerun S1/S2 suites + both E2Es → then fan out. Both sides of the contract change land in this slice (CLAUDE.md contract-change rule). **Coordination note:** SLICE_S3_CONTRACT (LOCKED, code not yet landed) also extends `PolicyTableEntry` additively (`TargetRule`, `accountState` axis, `IBearerAuthenticator`). Both deltas are additive and disjoint; whichever lane merges second rebases mechanically; the generated policy-matrix suite guards both.
- One additive Bicep module under `infra/` for the admin host as its own Container App with **internal/allowlisted ingress** — `bicep build`-validated now, undeployable until OQ-3 (subscription). Compose binds a distinct port now.
- Schema `admin`: **two tables** (staff accounts + role grants). Nothing else — no sessions, no search-audit, no audit-view, no dashboard cache, no config mirror.

**S5 does NOT own:** any desk's business logic (Safety→S12, Verification→S18, Content→S10/S15/S25, Venue&Con→S8/S29, Quest&Economy→S30/S33 — they register into S5's seams); kill-switch verbs OR their UI (see stub ruling); moderation actions warn/mute/suspend/ban (S12, riding S5's executor); S3's identity module or its contract (§9 port only); the separate "admin API" REST host (§1a note); Azure Monitor/paging wiring (disposition 14 is design-resolved; first page-now producer is S12; subscription is OQ-3); any consumer/partner surface; `contracts/openapi.v0.json` and `contracts/message-keys.json` (byte-identical, drift-gated).

**Kill-switch UI stub — REJECTED (P1/P2 over P3, token law 3):** a kill-switch button or "wires at S12" panel with no verb behind it is a decorative affordance. S12/S29 add surface WITH verb. Same ruling for desk nav: at S5 the nav shows only live surfaces (Dashboard · Config Registry · User Search · Audit Trail · Staff & Roles); unregistered desks are ABSENT, never grayed or placeholder-dark — real-or-honestly-dark governs data tiles, absence governs affordances.

**Brand-neutral:** one portal, one product, one user graph; brand is a data column on future desk rows, never a build flavor. Recorded so no B18-style question ever opens here.

**The one structural law S5 makes true:** *staff power exists only inside the admin host, and only through the audited-action chokepoint.* Enforced four independent ways, each red-fixture-proven (tension 6, structural): (a) 4A `admin.*` rows carry only `ActorKind.Staff` (+`System` for bootstrap) — the generated matrix asserts staff-only reachability on every row; (b) a source lint fails any `"admin."` action literal outside `Svac.AdminHost*` + domain-core tests; (c) an arch test fails any `Svac.AdminHost*` reference from `Svac.PublicApi` or any future consumer/partner host, and no mutating domain-contract member is invocable inside the admin host outside `AdminActionExecutor`; (d) the admin host is its own deploy unit behind internal-only ingress (Bicep + compose), never path-mounted under a consumer domain.

## 1. Host + API surface

### 1a. Host shape — hand-built Blazor; ADMIN OQ1 is RESOLVED, confirmed, concretized

Eng-review disposition 13 (2026-07-08) resolved ADMIN OQ1: **hand-built Blazor Server on the shared domain core; no admin framework — "the audit/role rules are custom either way and AI builds the CRUD."** LOCKED; not relitigated. All three architects re-ran the Layer-1/2/3 search as post-S1 due diligence and all three CONFIRM it:

- **Admin scaffolds (ABP ~13k★, Oqtane, ASP.NET Boilerplate): REJECTED.** Each ships its own identity, RBAC, audit-log, and settings system — the exact three things our pillars already own (4A table IS our RBAC, 3A stream IS our audit, 9A IS our config). Two authorization systems is how a privileged bypass is born; the framework buys CRUD scaffolding, the cheap part.
- **Themed component libraries (MudBlazor ~9k★, Radzen ~4k★, Fluent UI Blazor): REJECTED, narrowly.** Healthy projects, but each carries a theme system that fights DESIGN.md's token layer (neutral-professional register, Sky action color, radius ≤16, no chocolate outlines, no Black 900) — override cost exceeds hand-build for S5's surface (nav, tables, forms, one interstitial).
- **ADOPTED (Layer 1, first-party):** Blazor Razor Components on .NET 10 + **`Microsoft.AspNetCore.Components.QuickGrid`** (ships with ASP.NET Core) + ~300 lines of CSS generated from `design/tokens.v1.json` (1180px portal shell, body 16/24, `data` rows 14/20 tabular-nums, Sky primary, FA Regular icons). **Responsive from commit one** — S12's con-floor mobile Safety Desk is a hard later requirement and a responsive retrofit is a rewrite.

**Render mode (P2 adopted, §12.1): static SSR + enhanced form POSTs by default; per-component interactivity only where a desk later earns it.** Consequences, both deliberate: (a) staff mutations are REAL HTTP endpoints, so S1's `RequireMutationsPolicyMapped` boot-refusal chokepoint stays meaningful on this host exactly as on `Svac.PublicApi`; (b) `admin-host.e2e.mjs` drives real HTTP (cookie jar + form posts) in the established mjs harness — the hardened-gate E2E with **zero new toolchain** (Playwright rejected, §12.1).

**The "admin API" REST host (1A's four-host enumeration) — not S5's, not an open question.** The BUILD.md §7 S5 row scopes this slice to `backend/admin-host` (Blazor) only; every admin journey through S36 is a human in a browser. 1A's target set is untouched — the admin API host arrives with its first machine consumer, mounting the same `Svac.DomainCore.Hosting` chokepoint and the same executor. Pre-creating a consumerless host is the move S1's ratification already rejected. Recorded in §11 as a note, not a fork.

### 1b. Auth shell + staff identity (tension 2 — RESOLVED: seam-now)

**Two-layer identity: Entra authenticates; Svac authorizes.** The seam is the auth *transport*; the pipeline after it (claims check → directory mapping → cookie → executor) is IDENTICAL in dev and prod, so the E2E exercises the real code path, never a stub bypass.

- **Prod/Staging: OIDC to Entra ID** via `Microsoft.Identity.Web` (Layer-1 standard; no hand-rolled OIDC) + ASP.NET cookie auth. Authority/client-id from config; client credential from **Key Vault via the S0-reserved path — no staff-auth secret in the repo (2A)**. `ProdStaffAuthGuard`: any non-Development boot without complete Entra config **throws at startup** — the ProdFieldKeyVaultGuard family, Development allowlisted by NAME (Trust-F1: Staging/QA fail closed), modeled as an unresolvable typed dependency (the S2 `ValidateOnBuild` lesson), never a factory-lambda throw.
- **Dev: `DevSeamsStaffTransport`** (`[DevSeamsOnly]`, arch-tested never-in-prod-DI — the `IPaymentService` family): a dev-only sign-in page issuing deterministic fixture principals with the SAME claim shape Entra emits — founder-all-roles, one-per-role, **one without the MFA claim**, one with no staff row — so every refusal path is E2E-testable forever. **NEVER a 9A entry** (S1 §12 ruling: DevSeams is an environment flag; a desk edit must never swap auth backends).
- **MFA is enforced by US, fail-closed, not delegated to tenant config:** the staff authorization policy requires an MFA-satisfied claim (`amr` contains mfa / auth-context `acr`); absence ⇒ sign-in refused with a neutral-register page + `admin.signin.refused` audit event. Tenant-side Conditional Access requiring MFA is Julien's action (§11) — defense in depth; a misconfigured tenant fails closed instead of silently unprotected.
- **Principal → actor mapping (allowlist posture, fail-closed both hops):** subject (`oid` / fixture id) → `admin.staff_accounts.external_subject` lookup. No row, `status != 'active'`, or `security_stamp` mismatch ⇒ signed out, refused, audited. **JIT provisioning REFUSED** — an authenticated Entra user with no staff row is nobody. Match ⇒ `ActorRef(stf_<ulid>, ActorKind.Staff)`. **Roles come from OUR grants table, never Entra app-roles/groups** — role grants are founder controls that must be audited 3A events performed from the desk; an Entra assignment is invisible to the event substrate.
- **Revocation is live twice over (P1+P2/P3 composed, §12.6):** (1) `security_stamp` on the staff row, bumped on deactivate/grant/revoke; cookie validation + revalidation (interval from 9A `admin.session_revalidate_seconds`) re-checks stamp + status — a deactivated operator loses a LIVE session within the interval; (2) `AdminActionExecutor` re-reads status + grants from the DB on EVERY action — revocation bites at the next mutation regardless. No server session table exists.
- **Bootstrap (first SuperAdmin):** if `admin.staff_accounts` is empty AND `SVAC_ADMIN_BOOTSTRAP_SUBJECT` (+email/display-name/region) is set, provision that subject + SuperAdmin grant as a system-actor action, audited like any grant. One-shot: no bootstrap path exists once any account exists (tested). Unset + empty in prod ⇒ nobody signs in — fail-closed, safe. Env var, not 9A (bootstrap precedes the desk that edits 9A).
- **Blazor lifetime trap, named and solved once:** S1's `IRequestContextAccessor` is request-scoped. `StaffContextProvider` mints a fresh `RequestContext` per staff operation (staff ActorRef, declared region from the account row, fresh CorrelationId, `Staff` context §1d) and scopes it around every domain call — modules never touch `HttpContext`; every future desk inherits the fix.
- **Which hat acted — computed, not picked (P1/P3 over P2's header picker, §12.2):** `HatFor(action, grants)` = the least-privileged role among the actor's grants that satisfies the policy row (privilege order: SuperAdmin > the four operational roles > Analyst; ties by enum ordinal) — a pure function in `Svac.DomainCore.Deterministic`, golden-vectored. The audit payload ALSO carries `roles_held` (full snapshot): the hat answers "acting in what capacity", the snapshot answers "with what total power" — complete truth, zero ceremony a solo founder can misuse. Reversible to an explicit picker without schema change (the recorded field is the contract, not its derivation).

### 1c. The audited-action chokepoint (tensions 3 + 6)

Every staff mutation — now and for every future desk — flows through ONE door in `Svac.AdminHost.Domain`:

```
sealed class AdminActionExecutor : IAdminActionExecutor {
  Task<AdminActionResult> Execute(
    string action,        // a PolicyTable action key, e.g. "core.config.set.founder"
    TargetRef target,     // REAL resource ids, never ForAction-null: ("config_entry","match.swipe_cap_free_daily"),
                          // ("staff_account","stf_…") — the admin-path half of Auth-F3 (§9)
    string? reason,       // mandatory iff the row RequiresReason; whitespace = refused before Authorize
    Func<RequestContext, Task> work)
}
// Sequence, ONE EF transaction end to end:
//  1. re-read staff row (active? stamp?) + grants — revocation bites now
//  2. hat = HatFor(action, grants); ctx = ctx with Staff = StaffContext(staffId, rolesHeld, hat)
//  3. policyEngine.Authorize(actor, action, target) — Role axis evaluated via typed StaffRoles (§1d);
//     deny ⇒ staff-visible standard deny AND the refusal itself audited (admin.action.refused, metadata only)
//  4. four-eyes check (9A admin.four_eyes_required, §4) — fail-closed refusal when armed
//  5. reason check per the table row
//  6. work(ctx) — the domain mutation
//  7. ONE audit event per action, ever: verbs with a native 3A event (config.set) are NOT double-logged —
//     ConfigRegistry's existing same-tx event is enriched with {hat, roles_held} from ctx.Staff (additive
//     payload fields, no event-type change). Verb-less actions (grant/revoke/provision/deactivate) get one
//     executor envelope event admin.action.executed {action, target_ref, hat, roles_held, reason} in the
//     same tx as the row mutation (the ConfigRegistry.SetValue pattern verbatim: one SaveChanges).
//     User-impacting events key stream_id = the TARGET's ref (S2 purge scar pre-applied: a subject's purge
//     machinery must FIND the staff actions about them); staff-lifecycle events key by the staff ref.
// Boot law: RequireAdminActionsCovered() — every action key registered with the executor resolves to a
// PolicyTable row at startup or the host refuses to boot (the S1 boot-refusal law at the layer the admin
// host actually mutates through). RequireMutationsPolicyMapped() ALSO runs: SSR form-post endpoints carry
// [PolicyAction]; Blazor infrastructure endpoints map to the admin.host.transport row (§3) honestly.
// Arch rule (red-fixture): no type outside AdminActionExecutor's namespace invokes a mutating
// domain-contract member (IConfigRegistry.SetValue, ILedger.Reverse, staff-directory writes, future desk
// verbs) from Svac.AdminHost*. UI code literally cannot skip the chokepoint.
```

**Zero parallel audit store — proven, not claimed (tension 3):** S5's migrations contain no table matching `*audit*`/`*log*`/`*session*`/`*search*` (ddl-lint pattern + red fixture); `AdminDbContext`'s model enumerates to exactly the two schema-`admin` entities (arch test); the audit VIEW, per-staff search audit, sign-in history, and config history are all reads of `core.events_audit` via `IAuditReader` (§1d); the host maps no EF entity onto any `events_*` or `config_entries` table. Immutability stays S1's in-database append-only enforcement — inherited, nothing added, nothing weakened.

### 1d. Versioned additive changes to domain-core (both sides in this slice; S1/S2 byte-identical proof before fan-out)

```
// Svac.DomainCore.Contracts
enum StaffRole { SuperAdmin, SafetyAgent, ContentModerator, VenueConOps, EconomyOps, Analyst }  // CLOSED, adminportal verbatim
PolicyTableEntry += IReadOnlySet<StaffRole>? StaffRoles      // null = no role restriction; prose
   // StaffRoleAllowlistNote stays as provenance. S1's staff rows migrate note→typed set (no semantic change):
   // core.config.set.founder → {SuperAdmin} · core.config.set.ops → {SuperAdmin, EconomyOps}
   // core.ledger.reverse → {SuperAdmin, EconomyOps} · core.purge.execute (staff leg) → {SuperAdmin}
interface IStaffRoleResolver { Task<IReadOnlySet<StaffRole>> GrantsOf(ActorRef staff, CancellationToken ct); }
   // DEFAULT registration on every host: DenyAllStaffRoleResolver — a staff actor with no real resolver has
   // NO roles, fail-closed (ThrowingPaymentService family). AdminHost registers the grant-table resolver.
   // PolicyEngine: actor.Kind==Staff ∧ row.StaffRoles≠null ⇒ Allow requires grants ∩ row.StaffRoles ≠ ∅.
   // Red fixture: the test S1 called structurally unwritable — a staff actor without SuperAdmin denied on
   // core.config.set.founder — now written and green.
sealed record StaffContext(OpaqueId StaffId, IReadOnlySet<StaffRole> RolesHeld, StaffRole ActingHat);
RequestContext += StaffContext? Staff = null                 // additive, null on every non-admin host;
   // "which hat acted" as a first-class typed field the substrate stamps into event payloads.
interface IPolicyTableSource { IReadOnlyList<PolicyTableEntry> Entries { get; } }
   // PolicyTable = boot-time UNION of registered sources; duplicate action key across sources ⇒ boot
   // refusal (red-fixture). Domain-core rows are source #1; AdminPolicyTableSource contributes §3; every
   // desk slice adds its verbs with zero edits to domain-core or S5 (L29). Retires the S2-A splice-gap
   // pattern; the actual aiml.invoke splice stays S12's, unchanged.
IConfigRegistry += ListEntries(): IReadOnlyList<ConfigEntryView>   // key,type,scope,value,bounds,
                                                                    // requires_reason,updated_at/by — the editor's read
interface IAuditReader  { Query(AuditFilter{ eventTypePrefix?, actorRef?, streamId?, from?, to? }, CursorPage): AuditPage; }
interface IPurgeRunReader { Recent(CursorPage): PurgeRunPage; }
   // read-only pillar contracts implemented in Svac.DomainCore; two additive indexes on core.events_audit
   // (event_type, recorded_at DESC) and (actor_ref, recorded_at DESC) — indexes are not stores; 13A untouched.
   // No PublicApi endpoint maps IAuditReader; §0 lock (c) covers staff-only reachability.
IdPrefixes += stf_ (staff account), srg_ (staff role grant)
// Svac.DomainCore impl detail: ConfigRegistry.SetValue payload gains {hat, roles_held} when ctx.Staff≠null —
// additive payload fields, same event type, foreign-event law holds, projections unaffected.
```

### 1e. OpenAPI delta

**Zero paths, zero components — asserted, drift-gated (the S2 pattern).** `contracts/openapi.v0.json` + `contracts/message-keys.json` byte-identical across S5; a test fails if any `Svac.AdminHost*` type appears in the document. The adminportal spec's "admin-scoped API surface that is a separate application boundary" is satisfied MORE strongly by a separate host with no public contract at all: the public API cannot serve admin data even by bug because no admin type exists in its process or its document (arch-asserted). Lanes B/C/D untouched, proven. A future programmatic admin client adds a versioned `contracts/openapi.admin.v1.json` THEN — a deliberate act, never a delta to the consumer contract.

## 2. Schema DDL (schema `admin`, owned solely by AdminDbContext; migrations through ef-gate)

```sql
CREATE SCHEMA admin;

CREATE TABLE admin.staff_accounts (
  id               text PRIMARY KEY,             -- stf_ ULID
  external_subject text NOT NULL UNIQUE,         -- Entra oid; dev fixtures use deterministic 'devseams:*'
  email            text NOT NULL UNIQUE,         -- staff PII
  display_name     text NOT NULL,                -- staff PII
  status           text NOT NULL CHECK (status IN ('active','deactivated')),
  security_stamp   text NOT NULL,                -- bumped on deactivate/grant/revoke; kills live sessions
  region           text NOT NULL,                -- L21: staff are data subjects; declared at provisioning
  lawful_basis     text NOT NULL,                -- 'contract' (employment/engagement) via the S1 resolver
  created_at       timestamptz NOT NULL,
  updated_at       timestamptz NOT NULL,
  deactivated_at   timestamptz
);

CREATE TABLE admin.staff_role_grants (
  id            text PRIMARY KEY,                -- srg_ ULID
  staff_id      text NOT NULL REFERENCES admin.staff_accounts(id),
  role          text NOT NULL CHECK (role IN ('super_admin','safety_agent','content_moderator',
                                              'venue_con_ops','economy_ops','analyst')),
  granted_by    text NOT NULL,                   -- stf_ or sys_ (bootstrap only)
  grant_reason  text NOT NULL,
  granted_at    timestamptz NOT NULL,
  revoked_at    timestamptz,                     -- revocation = state transition, NEVER a DELETE
  revoked_by    text,
  revoke_reason text,
  region        text NOT NULL, lawful_basis text NOT NULL
);
CREATE UNIQUE INDEX ux_active_grant ON admin.staff_role_grants(staff_id, role) WHERE revoked_at IS NULL;
```

**That is the entire schema.** No sessions (cookie + stamp + per-action re-read), no search-audit table (rides `events_audit`), no audit-view table, no config mirror, no dashboard cache. DELETE revoked from the app role on both tables (S1 pattern): staff lifecycle is deactivate/revoke, never row surgery — the audit chain must always resolve `stf_` ids. The partial unique index is the check-then-act guard on double-grants (catch violation → re-read winner, idempotent-under-race tested). Every grant/revoke/provision/deactivate row change appends its 3A event in the SAME EF transaction (§1c law 7).

**Cross-module reads, enumerated and contract-only:** `core.config_entries` via `IConfigRegistry.GetValue/ListEntries`; `core.events_audit` + purge runs via `IAuditReader`/`IPurgeRunReader`; identity via the `IUserSearchSource` port (§9). Zero cross-schema joins, zero foreign EF mappings — arch-asserted with red fixture.

## 3. 4A policy entries (via `AdminPolicyTableSource`, union-merged; boot refusal on duplicate keys)

**Ledger-shorthand correction, stated:** the six roles are NOT new 4A actor kinds — `ActorKind` is a closed shipped enum. Roles are the **Role axis** on `ActorKind.Staff`, exactly what S1's `PolicyEngine` comment deferred to "S5's Entra." S5 makes the prose structural. All rows DenyStandard (legal for staff — staff may see reasons; S1's engine still coerces any consumer probe to absence).

| action | actor kinds | StaffRoles | requires_reason | IsReadPath | notes |
|---|---|---|---|---|---|
| `admin.staff.provision` | Staff, System (bootstrap) | SuperAdmin | **true** | false | creates account row |
| `admin.staff.deactivate` | Staff | SuperAdmin | **true** | false | bumps stamp; sessions die |
| `admin.staff.reactivate` | Staff | SuperAdmin | **true** | false | lifecycle completeness |
| `admin.staff.role_grant` | Staff, System (bootstrap) | SuperAdmin | **true** | false | per A5: grants are SuperAdmin-only, audited |
| `admin.staff.role_revoke` | Staff | SuperAdmin | **true** | false | |
| `admin.user_search.execute` | Staff | SuperAdmin, SafetyAgent, ContentModerator | false — the audited query IS the record; typed reasons per search train reason-fatigue before S12 needs reasons to mean something | true | Analyst structurally excluded ("no user PII beyond aggregate"); VenueConOps/EconomyOps widened by their desk slices when a journey exists (row edit) |
| `admin.audit.read` | Staff | SuperAdmin (v0) | false — each view query audited (filter metadata, not results) | true | raw events carry user refs/PII → least privilege; desk slices widen per-desk by row edit (§12.9) |
| `admin.dashboard.read` | Staff | all six | false | true | Analyst's whole scope |
| `admin.host.transport` | Staff, Anonymous | — | false | true | maps Blazor infrastructure endpoints (pre-auth sign-in page, component dispatch) honestly for RequireMutationsPolicyMapped; DenyAsAbsence; every actual staff verb is gated inside by the executor + RequireAdminActionsCovered |

- **Config edits add ZERO new rows (tension 5):** the editor is a client of S1's existing `core.config.set.founder` / `core.config.set.ops` rows, now enforceable because the role axis is typed (§1d). The confirm-with-reason interstitial is UI over the existing `requires_reason` flag. Set-scope rows render display-only (ratified structural constants are contract changes, not desk tuning — the editor refuses scope='set', no new policy row needed).
- Kill switches: NO rows (S12/S29 own verb + surface together).
- **Generated matrix suite extended:** every `admin.*` row asserts staff-only reachability; the Role axis gets a per-row grant/deny matrix generated FROM the typed table (each role × each action: exactly the allowlisted hats allow; deny never executes work, never appends a success event) — the first non-identity axis evaluation in the product, red-fixture both directions.

## 4. 9A config entries

**Admin-host tunables** (`admin-host.config.json`; every entry consumed by S5 code — dead-tunable lint holds):

| key | scope | type | v0 | requires_reason | consumer |
|---|---|---|---|---|---|
| `admin.session_lifetime_hours` | ops | int | 8 (bounds [1,24]) | false | cookie ticket lifetime |
| `admin.session_revalidate_seconds` | ops | int | 300 (bounds [15,900]) | false | stamp revalidation interval (§1b) |
| `admin.user_search_daily_cap` | ops | int | 500 | false | the §5 quota key's cap |
| `admin.four_eyes_required` | founder | bool | false | true | executor step 4: when true, any requires_reason action by a non-SuperAdmin hat refuses with "second approver required" — fail-closed placeholder; the approval WORKFLOW arrives with the first multi-staff desk. The adminportal's "config, on by default once staff count > 1" as a real switch, trigger recorded |
| `admin.staff_pii_retention_years` | founder | int | 6 | true | 13A retention_expiry verb for deactivated staff PII (§6); rides S1's OQ-1a ceiling family |

**The v0 batch (eng review §5) — seeded HERE, the ledger row's headline** (`v0-batch.config.json`). Every founder-ratified value becomes a typed, desk-visible, desk-editable row from day one. Canonical keys fixed now (renames later are migrations):

| key | scope | type | v0 | pending consumer |
|---|---|---|---|---|
| `verification.age_gate_challenge_threshold` | founder | int | 21 | S18 — requires_reason; **LOWERING additionally trips the interstitial** (spec-mandated, direction-aware UI rule) |
| `verification.reverify_deadline_days` | founder | int | 7 | S18 |
| `integrity.minor_report_rate_limit` | founder | json | `{count:3,window_days:30}` | S12 |
| `integrity.l4_reporter_tenure` | founder | json | `{days:30,requires_photo_verified:true,fallback_days:90}` | S12 |
| `match.swipe_cap_free_daily` | founder | int | 100 | S14 (D26) |
| `premium.price_usd_monthly` / `premium.grace_days` | founder | number / int | 9.99 / 16 | S23 |
| `romantic.superlike_budget` | founder | json | `{free:1,premium:3}` | S20 (R5 closed) |
| `romantic.pending_ttl` | founder | string | `disabled` | S20 (12A-r dial) |
| `nakama.daily_budget` | founder | int | 3 | S34 (tier-neutral) |
| `match.reciprocity_signal_budget` | ops | int | 30 | S14 |
| `battle.freemium_limit` | founder | json | `{free:5,premium:20}` | S22 |
| `invite.combined_budget_r8` | founder | json | `{free:5,premium:25}` | S28 (one counter per R8) |
| `crew.captain_invite_daily` | founder | int | 10 | S27 |
| `premium.dm_baseline_daily` | founder | int | 5 | S23 |
| `economy.gifting_daily` | founder | int | 5 | S35 |
| `economy.trade_status` | set | json | `{free:true,new_account_cooldown_days:7,min_level:5}` | S35 |
| `ads.sponsored_card_ratio` | founder | json | `{operating_one_in:25,structural_ceiling_one_in:15}` | S30 — set-time bounds rule: `operating_one_in >= 15`, the 1:15 ceiling enforced at SET before S30's arch test exists |
| `characters.ai_card_frequency_cap_one_in` | founder | int | 20 | S25 |
| `quest.party_cap` / `crew.squad_cap` | set | int | 8 / 11 | S33 / S27 |
| `heatmap.cell_provenance_days` / `heatmap.cell_history_months` | set | int | 365 / 12 | S32 (R2) |
| `heatmap.residential_geohash_floor` | ops | int | 5 | S32 |
| `nakama.radius_km` / `nakama.presence_ttl_days` / `nakama.per_pair_cap` | ops | int / int / json | 25 / 14 / `{count:3,window_days:30}` | S34 |
| `battle.modal_ttl_seconds` / `battle.resume_window` | set | int / string | 90 / `same_con_day` | S22 |
| `match.category_density_floor` / `match.category_density_resume` | ops | int | 30 / 40 | S19 — bounds: resume > floor |
| `match.pass_reserve` | set | json | `{hours:48,alt:"next_con_day",max:2}` | S14 |
| `match.exposure_floor` | ops | json | `{eligible_min_serve_per_active_con_day:1,watch_tier_per_days:2,restricted_exempt:true}` | S19 |
| `quest.spot_check_rates` | set | json | `{general:0.05,sponsored:0.10}` | S30 |
| `quest.kill_bonus_threshold` / `economy.svac_weekly_cap` | set | number / int | 0.5 / 50 | S33 / S35 |

**Deliberately NOT seeded, reasons in the manifest:** `nakama_rep_floor` (units await INTEG calibration — a key with an unratified unit is fabrication, S16 seeds it); ε-budget-per-principal (the review itself defers to pre-G3.5b); annual Premium ("annual later"). `core.con_day_cutoff` already S1-seeded — never duplicated (union-merge, one truth). The exclusion list renders on the desk, not silently.

**Dead-tunable lint reconciliation (the one real S1-rule tension — P3+P1 synthesized, P2's mode rejected, §12.7):** the manifest format gains `pending_consumer_slice: "S<N>"` — the lint accepts a pending key iff the value names a BUILD.md §7 ledger slice (P1's validation), AND CI fails when that slice is marked DONE without a registered consumer claiming the key (P3's teeth). A key that is neither consumed nor validly pending still fails; red-fixture both ways. P2's `desk_rendered` satisfaction mode is REJECTED: the desk renders every key, so it would legalize any dead key forever. The editor renders a "consumer lands at S<N>" chip per pending key — honestly dark, still editable.

**Founder vs ops rendering (tension 5):** scope is a first-class column; founder-scope edits walk the confirm-with-reason interstitial (typed old→new diff, mandatory reason, explicit re-confirm — DESIGN.md neutral-professional modal); ops-scope edits take an inline mandatory-reason field; set-scope rows are display-only. Bounds violations surface `ConfigBounds`' own set-time rejection verbatim — the editor never pre-validates with a second bounds implementation (one truth; a rejected Set leaves value + stream byte-identical, S1-proven, re-asserted through the editor path).

## 5. 10A quota keys

One key (P1/P3 over P2's zero, §12.5):

- **`admin.user_search.daily`** — actor = the staff `stf_` ref, window daily, cap from 9A `admin.user_search_daily_cap` (v0 500 — generous; con-weekend triage is search-heavy), via S1's one `Consume` verb. Rationale: the audit gives DETECTION of insider scraping; the quota gives PREVENTION at the single search door — a compromised staff session cannot bulk-enumerate users, on existing machinery, day one (the S2 §12.4 protective-infrastructure precedent). Deny renders the portal's neutral limit notice from the ONE LimitReached shape.

No other keys. Admin mutations are low-volume human acts gated by role + reason; a quota on `admin.staff.role_grant` is theater.

## 6. 13A store registrations (purge-registry.json; CI gate re-proves non-vacuous)

| store | account_deletion | statutory_erasure | minor_purge | consent_revocation | retention_expiry | orphaned_blob |
|---|---|---|---|---|---|---|
| `admin.staff_accounts` | NotApplicable (staff are not consumer accounts; the consumer pipeline never reaches them) | Pseudonymize/Tombstone PII (email, display_name, external_subject re-keyed; `stf_` id + status survive so every audit chain still resolves — the S1 OQ-1a posture applied to staff) | NotApplicable (18+ employment by construction) | NotApplicable (no consent-gated staff field) | after `admin.staff_pii_retention_years` post-deactivation → Pseudonymize | n/a |
| `admin.staff_role_grants` | NotApplicable | Tombstone reason texts + grantor/revoker PII where the erased subject appears; grant/revoke structure survives (accountability) | NotApplicable | NotApplicable | rides staff_accounts | n/a |

Zero registrations beyond these two — search audit, sign-ins, action envelopes, and config history all live on `events_audit`, registered by S1 with OQ-1a's ratified verbs. **Named honestly:** search-query events carry the raw query term (an email searched is a subject's PII inside an event keyed by the staff ref) — retained under the OQ-1a defensible-record posture (`lawful_basis=legal_obligation`; reconstructing what an operator searched IS the accountability product); flagged for counsel's L-1 pass. Purge-completeness suite gains the staff-pseudonymize case: seed → erase → audit chain resolves `stf_` → PII gone.

## 7. Notification taxonomy rows

**Zero — asserted (S1/S2 closure-baseline pattern).** Staff are operators, not consumers; nothing here maps to the nine-category consumer taxonomy. Operator paging is disposition 14 (Azure Monitor + paging; page-now classes enumerated) — a different channel class, wired by its first page-now producer (S12) once the OQ-3 subscription exists. No fake paging tile ships. S4 inherits a clean baseline in either landing order.

## 8. BUILD.md §9 seams made concrete — each an extraction point, each non-vacuous day one

| # | Seam | S5 concrete form | First registrant → later consumers |
|---|---|---|---|
| 1 | **Desk registration** | `IDeskModule { DeskId, TitleKey, NavOrder, IReadOnlySet<StaffRole> VisibleTo, RootComponent }`; nav renders ONLY registered desks, role-filtered; unregistered = ABSENT | S5's five surfaces → S8/S10/S12/S15/S18/S25/S29/S30/S33: additive files + one DI registration, zero S5 edits |
| 2 | **Tile registration** | `IMetricsTileSource { TileId, TitleKey, VisibleTo, Query(ct) }`; a tile with no live source is not registered (L6: absent, never fabricated) | S1 tiles (config-change events, purge runs, stream volumes, staff sign-ins) + S2 tiles (`aiml.route_decided`: volume, failover, latency, provider/model mix, policy version — the S2 §10.6 promise kept) → every gate metric lands as one registration from its owning slice |
| 3 | **Audited-action chokepoint** | `IAdminActionExecutor` (§1c): Authorize(real target) → four-eyes → reason → work + ONE same-tx 3A event with hat | grant/revoke/provision/deactivate/config-set (5 real consumers day one) → S12 warn/mute/suspend/ban/kill, S29 venue verbs, S30 reversals — all 16 console capabilities ride this funnel |
| 4 | **Staff-auth transport** | `AddStaffAuth`: one claim contract + one MFA policy + one directory mapping behind dev/prod transports; ProdStaffAuthGuard throws keyless | DevSeams transport → prod Entra = config + tenant, zero rewrite; IdP swap = one transport |
| 5 | **Composable policy table** | `IPolicyTableSource` union, duplicate-key boot refusal (§1d) | domain-core + admin sources → every desk slice's verbs |
| 6 | **Search port** | `IUserSearchSource` (§9), one audited execute path — humans use the engine machines use | `EmptyUserSearchSource` → S3 adapter, one DI line |
| 7 | **Audit read model** | `IAuditReader`/`IPurgeRunReader` pillar reads (§1d) | audit-trail desk + tiles → every desk's decision-log view |
| 8 | 1A boundary isolation | host references `*.Contracts` + Hosting only; AdminDbContext admin-schema-only; every core read is a contract read (arch + red fixture) | — |
| 9 | 3A same-tx | executor law 7; config.set reused never doubled; grants join their row's tx | — |
| 10 | 4A entry per mutation | `RequireMutationsPolicyMapped` (SSR posts carry [PolicyAction]) + `RequireAdminActionsCovered` boot refusal + no-mutation-outside-executor arch rule | — |
| 11 | Region-first PII (L21) | region + lawful_basis NOT NULL on both tables; declared at provisioning; events stamped from RequestContext | — |
| 12 | Fail-closed secrets (L18 family) | prod boot without Entra config throws; MFA-claim absence refuses; Key Vault only (2A); DevSeams never-in-prod-DI | — |
| 13 | Edge guard vs traversal (L17) | own Container App + hostname, internal ingress, never path-mounted under a consumer domain; e2e probes `%2e/%2f/%5c`/dot-segment → 404 pre-auth; edge-guard e2e gains an admin-unreachable-via-public-origin probe | — |
| 14 | Keyed strings | ALL admin strings keyed from commit one (hardcoded-literal lint on the host); catalog ships EN-only pending §11 OQ-1 ratification; locales later = data, not rework | — |
| 15 | One deny shape / absence | staff denials = standard staff deny, themselves audited; nav absence law (§0); consumer coercion untouched | — |
| 16 | Real-or-honestly-dark (L6) | tiles render only flowing data; search renders the honest "identity module not yet live" state, never fabricated rows | — |
| 17 | Analytics written AND received | desk page-views emit on the behavioral stream; the E2E reads one back; the tiles ARE the receipt side of S1/S2 emissions | — |
| 18 | Deterministic math in pure libs | `HatFor` pure + golden-vectored in `Deterministic`; cursor math likewise | — |
| 19 | Concurrency at check-then-act | double-grant partial-unique-index race (catch → re-read); stamp compare-and-bump; two SuperAdmins editing one key = last-write-wins WITH both audit events present (tested); executor re-read closes the deactivated-staff race | — |
| 20 | Foreign-event skip | the host registers ZERO projections (reads are query-time) — N/A-with-note asserted; no watermark consumer to wedge | — |

## 9. Dependency classification

| Dependency | Class | Handling |
|---|---|---|
| S1 substrate (3A/4A/9A/10A/13A, Hosting, DevSeams, guards, manifests) | **built** | consumed as-is; S5 is the first STAFF exerciser of rows S1 shipped "unexercisable until S5's Entra" — itself a test of S1 |
| S2 AimlRouter events/config | **built** | tiles render from `aiml.route_decided` + `aiml.*` entries already flowing |
| Entra ID tenant + Conditional Access MFA (no Azure subscription — OQ-3) | **seam-now** | dev transport now; prod = config + Key Vault secret + Julien's tenant actions (§11); fail-closed boot guard ships now; MFA enforced in OUR claims check so the tenant is never a trust root; nothing blocks S5 green |
| S3 identity (user search) | **seam-now / contract-boundary** | **host-owned port** `IUserSearchSource { Search(query: HandlePrefix\|EmailExact\|DeviceExact, cursor): UserSearchPage }` with `EmptyUserSearchSource` registered; honest-dark UI; EVERY query audited (`admin.user_search.executed` {query_class, query_term, hat}, stream_id = staff ref) + quota-consumed regardless of emptiness; the E2E asserts the WIRING (auth→4A→quota→audit→render), never fabricated results. When S3 lands: one adapter in the admin host maps the port onto `Svac.Identity.Contracts` (host→module-contract direction; S3 never references the host; S3's LOCKED `IAccountDirectory` is the natural target). **Recorded obligations for S3:** indexed handle-prefix/email-exact/device-exact lookups (the hmg `.includes()` scar — the port's contract tests define the semantics S3's impl must pass; an in-memory scan cannot), no trust fields in the result DTO, opaque ids |
| Auth-F3 / SilentRej-L4 carries | **coherence with S3's LOCKED contract** | S5's executor conveys REAL TargetRefs on every admin verb — the admin-path exposure is closed here; the HTTP `PolicyEnforcementFilter`/`TargetRule` redesign LANDS AT S3 per its locked contract (not re-carried to S10 as proposals assumed — corrected against the ratified doc). SilentRej-L4 likewise lands at S3 (`GET /v1/me`); S5 adds staff-only reads, consumer indistinguishability untouched (existing suite asserts) |
| The desks (S8/S12/S15/S18/S25/S29/S30/S33) | **swap-safe, not read** | they register into seams 1/2/3/5; S5 references none of them; zero desk scaffolding pre-built (absence law) |
| Kill-switch verbs | **not read** | S12/S29 own verb + UI together; no stub (§0) |
| S4 notifications | **not read** | zero admin rows in the consumer taxonomy (§7); no ordering dependency either way |
| Azure Monitor + paging (disposition 14) | **deferred by design** | first page-now producer is S12; subscription-blocked (OQ-3); S5 renders `/health` + tile-level truth only, no paging pretended |
| Key Vault / Azure subscription | seam-now | S1's `IFieldKeyVault`/secret path + S0-reserved names |
| Playwright / any browser toolchain | **REJECTED — not a dependency** | static SSR form posts make the E2E pure HTTP in the existing mjs harness (§1a); zero new dev dependencies |
| MUST-BUILD-FIRST blocking S5 | **none** | S1 + S2 landed; S5 starts now (S3/S4/S7 explicitly not prerequisites) |

## 10. Outcome moved + evidence at sign-off (THE HARDENED GATE for S5)

Ledger row: **"config change = audited 3A event, rendered from registry."** B6 partially dies: the Content Desk's chassis (role, audited verb, shell, audit view) exists for S15/S10 to register into.

1. **Arch suite extended, every rule red-fixture-proven:** staff-auth + `admin.*` actions only in AdminHost (source lint + reference scan) · no mutating contract call outside the executor · AdminDbContext admin-schema-only, no `events_*`/`config_entries` mapping · no `*audit*`/`*session*`/`*log*` table in admin migrations (ddl-lint) · DevSeams staff transport never-in-prod-DI · ProdStaffAuthGuard throw on prod-unconfigured · policy-source duplicate-key boot refusal · `RequireAdminActionsCovered` boot refusal on an unregistered executor key · DenyAllStaffRoleResolver default on non-admin hosts · generated role-axis matrix (six roles × every admin row, allow and deny legs) · staff-only reachability on every `admin.*` row.
2. **Deterministic gate suite (<2s lane):** `HatFor` golden vectors · executor tx-atomicity (kill the tx after work(), assert NEITHER mutation nor event persisted) · one-event-per-action (serialized-shape test: no double event on config.set; envelope on verb-less) · reason-refusal · MFA-claim refusal · unknown-subject refusal · security-stamp live revocation · deactivation bites at next action · four-eyes refusal when armed · bounds rejection leaves value + stream untouched through the editor path · bootstrap one-shot idempotence · grant-race idempotency · v0-batch manifest completeness vs a checked-in transcription of eng-review §5 (a dropped row is a test failure, not an oversight) · `pending_consumer_slice` lint honest both directions · staff-pseudonymize purge case.
3. **Live E2E** (`backend/e2e/admin-host.e2e.mjs`, real HTTP + cookie jar + SSR form posts against the compose stack, REAL auth pipeline — the dev transport is the dev backend of the same pipeline prod uses, no stub bypass): no-MFA fixture REFUSED at sign-in (the check is live) → not-provisioned fixture refused → SuperAdmin fixture signs in (real cookie, MFA claim) → dashboard renders live S1/S2 tiles (real counts) → Config Registry renders the FULL v0 batch from `core.config_entries` (spot-check `verification.age_gate_challenge_threshold=21`, scope badges, pending chips) → edits an ops key with reason → **the `config.set` audit event is read back off `events_audit` with {staff `stf_` actor, hat, roles_held, reason, region, lawful_basis} AND the editor re-renders the new value from a fresh registry read AND the config-change tile reflects it — the ledger outcome, observed** → founder-scope edit walks the interstitial → a non-qualifying fixture's founder-scope attempt is REFUSED, `admin.action.refused` audited, stored value byte-identical → out-of-bounds edit refused with the registry's own message → user search executes: `admin.user_search.executed` event read back + quota consumed + honest-dark result, zero fabricated rows → SuperAdmin grants EconomyOps to a second fixture with reason (event read back; new hat live without redeploy) → that fixture edits an ops key → revoke → denied at next action mid-session → deactivate → live session dies within the revalidation interval → traversal probes 404 pre-auth → behavioral page-view event read back → fresh-boot (`down -v` → up) zero errors/restarts → zero-exception log sweep → suite twice, identical. Honesty note: Microsoft's Entra servers cannot be exercised without the tenant (OQ-3); every enforcement line of OUR code (claims check, mapping, refusal paths, guard) IS exercised — the prod delta is configuration, fail-closed if absent.
4. **Prod-build clause:** the Release-published container serves the sign-in page + `/health` 200; Production env + unconfigured Entra ⇒ startup throw, asserted. Two-instance leg: N/A-with-note (no realtime fan-out surface; circuit affinity recorded in the Bicep module as the prod infra requirement).
5. **UX-coherence crawl** against `design/08 Admin Portal.dc.html` + DESIGN.md neutral-professional laws (Sky actions, 1180px shell, tabular-nums data rows, no candy, no Black 900); no leaked brief labels; every nav target real or absent.
6. **Renders:** the Metrics & Ops desk shell IS the rendering surface from this slice forward — S1 §10's "interim posture until S5" retires on schedule; the slice's own metric (config changes as audited events) is tile #1.

**Evals: N/A with reason** — S5 contains zero latent-space surface (BUILD.md §0 enumerates the product's four; none is here — the desk renders T9/T8 outputs in later slices but produces none). Deterministic tests + the live E2E are the entire quality story, per S1/S2 precedent.

## 11. Open questions for Julien (genuine forks only) + Julien-executed actions

**OQ-1 — admin-portal locale exception (the one genuine fork; unanimous recommendation).** BUILD.md's ×4-locale law binds every surface; the staff portal's audience is internal operators. (a) **Recommended:** all strings keyed from commit one (lint-enforced), catalog ships EN-only, exception recorded in i18n-lint config with this reason; flipping to ×4 later is translation data, zero rework. (b) Full ×4 now: hundreds of operator strings translated before a second operator exists. This is a stated exception to a LOCKED law, so it is Julien's to ratify, not the judge's to assume. Recommend (a).

**Not open — resolved rulings, honored:** ADMIN OQ1 (eng disposition 13; concretized §1a, confirmed by a re-run search) · ADMIN OQ3 (disposition 14; S5 ships nothing for it beyond real health/ops tiles; first producer S12, blocked by OQ-3 which is already Julien's standing item) · Analyst audit access (called by the spec's own "no user PII beyond aggregate" — Analyst excluded from search and raw audit; widening is a per-desk row edit later) · the separate "admin API" REST host (the §7 ledger row scopes S5 to the Blazor host; 1A's target set intact, deferred to first machine consumer — reversible: the executor is UI-agnostic and a REST host later mounts the same table + executor).

**Julien-executed actions (not questions):** (1) when OQ-3's subscription lands — create the Entra staff tenant + app registration + Conditional Access MFA policy for the staff group, place the client credential in Key Vault under the S0-reserved name (checklist ships in the module README; the first prod sign-in smoke test asserts the amr claim end to end); (2) supply his Entra `oid` as `SVAC_ADMIN_BOOTSTRAP_SUBJECT` in the prod parameter set; (3) ratify OQ-1; (4) ratify `admin.staff_pii_retention_years` v0=6 (founder-scope, desk-revisable, seeded reversible per the S0/S1 interim pattern).

## 12. Judge's synthesis record (conflicts, adoptions, reasoning)

1. **Transport + E2E toolchain: P2's static SSR + enhanced form posts over P1/P3's interactive circuits + Playwright.** Form POSTs keep staff mutations as real HTTP endpoints, so S1's `RequireMutationsPolicyMapped` chokepoint binds on this host exactly as on PublicApi (verified against `StartupPolicyCoverage.cs`: null-method metadata is treated mutation-capable — P2's `admin.host.transport` row + `RequireAdminActionsCovered` is the honest mapping), and the E2E stays pure HTTP in the established mjs harness. Playwright (P1's justified-new-dependency) becomes unnecessary — vanilla wins twice. Per-component interactivity remains available when a desk earns it.
2. **Which hat: deterministic `HatFor` (P1/P3) over P2's header hat-picker; P1's `roles_held` snapshot and P3's `StaffContext` shape both adopted.** A picker is per-action state a solo operator will misuse and adds nothing the snapshot doesn't preserve; the derivation is deterministic-space work (pure, golden-vectored), and the recorded field — not its derivation — is the contract, so an explicit picker later is schema-free. `RequestContext.Staff: StaffContext{StaffId, RolesHeld, ActingHat}` (P3) over a bare `ActingRole` field (P2) because the substrate can then stamp hat AND snapshot into native event payloads (config.set enrichment) without executor plumbing.
3. **Composable policy table: P2's `IPolicyTableSource` union + duplicate-key boot refusal ADOPTED.** Without it, every desk slice edits domain-core's `PolicyTable` — cross-unit edits that violate the parallel-session law and recreate the S2-A splice-gap class. One small contract, first-registrant-proven, L29 applied to 4A. Compatible with S3's locked PolicyTable delta (both additive; merge-order note in §0).
4. **Search seam: host-owned port `IUserSearchSource` (P1/P3) over `IUserDirectorySearch` in DomainCore.Contracts (P2).** S3's LOCKED contract already owns `Svac.Identity.Contracts` (incl. `IAccountDirectory`) — the substrate has no business knowing about user search, and the correct dependency direction is admin host → identity module contract via one adapter when S3 lands. P2's placement would burden every host with a seam only one host uses. S3's indexed-semantics obligations (P3's contract-test framing, the hmg scar) recorded in §9.
5. **Search quota: one key (P1/P3) over zero (P2).** P2's "audit not throttling" names detection; the quota is prevention at the single search door against a compromised session bulk-enumerating users — the same protective-infrastructure logic the S2 judge used for the router breaker (S2 §12.4). Rides existing machinery, costs one 9A entry.
6. **Sessions/revocation: composed.** P2/P3's `security_stamp` + revalidation (kills live READ access within the interval) AND P1's executor per-action re-read (kills mutation power immediately). No session table (unanimous). Both legs tested.
7. **Dead-tunable lint: P3's `pending_consumer_slice` + DONE-without-claiming CI teeth, plus P1's must-name-a-§7-slice validation. P2's `desk_rendered` mode REJECTED** — the desk renders every key, so desk-rendering as lint satisfaction legalizes any dead key forever; the lint would stop meaning anything.
8. **Desk nav + kill switches: absence (P1/P2) over honest-dark desk pages / "wires at S12" panels (P3).** Real-or-honestly-dark governs DATA tiles; affordances follow token law 3 — an unregistered desk is absent, and `IDeskModule` makes desk arrival one registration, so absence costs nothing.
9. **Audit-view gate: SuperAdmin-only v0 (P3) over all-except-Analyst (P2), over ungated (P1's omission).** Raw audit events carry user refs; least privilege at a solo-founder moment costs nothing and widening is a per-desk row edit. Analyst exclusion decided by spec text, not left as P3's OQ.
10. **Naming/layout: schema `admin` + `AdminDbContext` (P1/P2) over `staff` schema + module-pair (P3);** the ledger unit is one host; P2's two-assembly split (host + internal Domain) adopted as the template-consistent minimum. Grant prefix `srg_` (P3) for specificity.
11. **Retention config: seeded now (P1) over noted-not-seeded (P3)** — the consumer is S1's 13A retention_expiry machinery via the registry row; founder-ratification listed as a Julien action (§11).
12. **Admin API host: P1's OQ-A demoted to a §11 note** — the §7 ledger row already scopes S5 to the Blazor host; no 1A contradiction exists to ratify away.
13. **Bicep now (P2/P3) over compose-only (P1)** — the ingress-isolation lock (§0 law d) should exist in IaC the day the host exists; `bicep build` validation is free, deployment stays OQ-3-blocked.
14. **Corrected against ratified docs:** all three proposals carried Auth-F3's HTTP half "to S10/S3/S9" loosely — SLICE_S3_CONTRACT (LOCKED) already lands it at S3; §9 states the coherent disposition. The ledger row's "roles ×6" shorthand corrected to the Role axis on `ActorKind.Staff` (P2/P3), since `ActorKind` is a closed shipped enum.
15. **Unanimous points adopted as written:** hand-built Blazor + QuickGrid + token CSS (disposition 13 confirmed); Entra authenticates / Svac authorizes; roles as audited grant rows, never IdP claims; JIT refused; MFA in our claims check with a no-MFA negative fixture; fail-closed prod boot; env-var one-shot bootstrap; two tables total; zero parallel audit store with ddl-lint red fixture; one-event-per-action with config.set enrichment; editor as pure 9A client with zero new config mechanism and zero new config policy rows; interstitial as UI over requires_reason; v0 batch seeded minus the two honestly-unratified values; zero OpenAPI delta drift-gated; zero notification rows; zero projections; region+lawful_basis on all staff PII; keyed strings from commit one; evals N/A with reason; nothing blocks S5 — it starts now.

## 13. RATIFICATION (orchestrator, 2026-07-11 — Julien's in-absence pre-approval)

Contract **RATIFIED**. Julien authorized starting S5 this session, then stepped away; in-absence ratification of a current slice's contract is pre-approved. The one OQ is reversible with a unanimous recommendation I endorse.

**Scope + the six tension resolutions ratified as written.** Hand-built Blazor Server + QuickGrid + token CSS (eng-disposition 13, re-confirmed by search; no framework, no themed lib); static SSR + enhanced form POSTs (staff mutations stay real HTTP endpoints, E2E pure-HTTP mjs, Playwright rejected); Entra-authenticates/Svac-authorizes with MFA enforced fail-closed in OUR claims check and a no-MFA negative fixture; audit = 3A reads via `IAuditReader`, zero parallel audit store (ddl-lint red fixture); config editor a pure 9A client (zero new mechanism, zero new config policy rows); no-admin-from-consumer enforced four independent ways. Two tables total. The v0 eng-review §5 config batch seeded (the ledger headline), minus the two honestly-unratified values.

**OQ-1 — admin-portal locale exception: RATIFIED (a).** All admin strings keyed from commit one (hardcoded-literal lint on the host), catalog ships EN-only, the exception recorded in i18n-lint config with its reason. This is a stated exception to the ×4-locale LOCKED law, so flagged prominently for Julien's veto — but it is unanimous and reversible (flipping to ×4 later is translation data, zero rework; translating hundreds of operator strings before a second operator exists is waste). Endorsed.

**Julien-executed actions recorded (not blockers; none gate S5 green):** at OQ-3 (Azure subscription) — create the Entra staff tenant + app registration + Conditional Access MFA policy, place the client credential in Key Vault under the S0-reserved name (README checklist ships); supply his Entra `oid` as `SVAC_ADMIN_BOOTSTRAP_SUBJECT` in the prod parameter set; `admin.staff_pii_retention_years` v0=6 ratified conservative-reversible (S0/S1 interim pattern). ADMIN OQ1 (disposition 13) and OQ3 (disposition 14) correctly treated as resolved, not re-litigated.

**RECONCILIATION with S3 (LOCKED) — the shared domain-core Phase-2a delta, verified compatible:** S3 and S5 both extend `PolicyTableEntry`/`PolicyEngine.Authorize` additively and orthogonally — S3 adds `TargetRule` + `accountState` axis + `IResourceOwnershipResolver` + `IBearerAuthenticator` + the email/consent/export seams + `AgeMath`/`HandleRules`; S5 adds `StaffRoles` + `IStaffRoleResolver` + `IPolicyTableSource` union + `IConfigRegistry.ListEntries`/`IAuditReader`/`IPurgeRunReader` + `HatFor`. `Authorize` evaluates target-ownership, accountState, and Role as independent ANDed axes; no collision. Two build-binding rulings:
- **`IPolicyTableSource` (S5) is the shared mechanism.** S3's identity rows contribute as `IdentityPolicyTableSource`, S5's as `AdminPolicyTableSource`, domain-core is source #1; duplicate-key boot refusal guards all. No slice edits domain-core's table directly — supersedes S3's §3b "regenerates from this table" phrasing (the table becomes the union of sources).
- **Cookie (S5) vs bearer (S3) is not a conflict.** The Blazor browser host correctly uses cookie auth; `IBearerAuthenticator` (S3) serves the API hosts (public now, partner + future admin-API later). Both mount the same `Authorize` + executor chokepoint. S3 §1b's "admin host mounts the same bearer middleware" prose is superseded.

**BUILD-ORDERING (binds the parallel plan):** the S3 + S5 (+ pending S6) domain-core deltas are designed and landed as ONE combined Phase-2a substrate mutation (policy engine axes + `IPolicyTableSource` + resolvers + Hosting + Deterministic + the S3 seams), byte-identical-behavior proof on S1/S2 suites + both E2Es BEFORE any feature/host builders fan out (SLICE_PLAYBOOK 2a). Then S3 identity module, S5 admin host, S6 anime module build on the shared substrate. The combined-delta design is finalized once the S6 panel lands and its needs are folded in.

Ratified. Proceeds to Phases 1–3 through THE HARDENED GATE after the S6 reconciliation; STOPS at DONE for /compact (stop-after-slice).

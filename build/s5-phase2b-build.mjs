// build/s5-phase2b-build.mjs — S5 Phase 2b (Build): the admin-host content.
//
// The admin host is ONE unit → a SEQUENTIAL chain (playbook forbids parallel agents on one unit), but it
// is LARGE → sub-passes, a FRESH agent per pass with full runway (S3 retro lesson #1). test-author writes
// the FULL live E2E RED first (L30). Each pass owns a DISJOINT file set and MUST leave `dotnet build`
// green before returning. The orchestrator verifies THE HARDENED GATE + commits after the workflow.
//
// Phase 2a (domain-core: config.set enrichment + audit indexes) already landed + committed (086baf1).
// The Phase-2a SUBSTRATE (StaffRole/StaffRoles/IPolicyTableSource/IAuditReader/IPurgeRunReader/HatFor/
// StaffContext/RequestContext.Staff/stf,srg prefixes/async 3-axis engine) is on master — CONSUME it.

export const meta = {
  name: 's5-build',
  description: 'S5 admin-host Phase 2b: E2E-first, then sequential fresh-agent builder passes to a working slice',
  phases: [{ title: 'Test-author' }, { title: 'Build' }, { title: 'Finish' }],
}

const M = 'sonnet' // execution model: tests/types/compose catch mistakes (playbook model rule)
const CONTRACT = 'SLICE_S5_CONTRACT.md'

const COMMON =
  `\n\nHARD RULES (all passes): ${CONTRACT} is LOCKED and OVERRIDES any generic wording — build ONLY your ` +
  `pass's deliverables; never scaffold a later desk (S8/S10/S12/S15/S18/S25/S29/S30/S33 register into S5's ` +
  `seams). Do NOT edit domain-core contracts (Phase-2a substrate is DONE + on master — CONSUME it; verify a ` +
  `symbol exists with grep before relying on it, never re-add it). Every staff mutation flows through ` +
  `AdminActionExecutor (§1c) — UI code MUST NOT call a mutating domain-core member directly (arch-gated). ` +
  `Events on the 3A substrate in the SAME tx; tunables via 9A ListEntries/SetValue; quota via 10A Consume; ` +
  `keyed strings only (EN-only catalog, OQ-1(a) ratified) — no hardcoded UI literals (i18n-lint scans ` +
  `*.razor); region+lawful_basis on staff PII; trust fields absent from request DTOs; absence/one-deny-shape ` +
  `for consumers untouched. DESIGN.md neutral-professional (Sky actions, 1180px shell, tabular-nums data ` +
  `rows, radius <=16, NO candy, NO Black 900) from design/tokens.v1.json — read design/08*.dc.html. ` +
  `Own ONLY your pass's files; NEVER a later pass's surface. Leave 'dotnet build backend/Svac.sln' GREEN ` +
  `before returning; re-run it yourself and return the ACTUAL output. If you cannot finish a deliverable, ` +
  `say so explicitly (honest-BLOCKED) — never stub-and-claim.`

async function critical(prompt, label, phaseName) {
  let r = await agent(prompt, { label, phase: phaseName, model: M })
  if (r == null) { log(`${label}: null — retry once`); r = await agent(prompt, { label, phase: phaseName, model: M }) }
  if (r == null) throw new Error(`${label} died twice — resume-from-runId after inspecting`)
  return r
}

// ---------- Stage 1: test-author — the FULL live E2E, RED (L30) ----------
phase('Test-author')
await critical(
  `You are the test-author for weeb-app slice S5 (admin host) per ${CONTRACT} §10 (THE HARDENED GATE) and ` +
  `BUILD.md §8. Write the hardest path FIRST, RED (the surfaces do not exist yet). Deliver TWO things:\n` +
  `(A) backend/e2e/admin-host.e2e.mjs — the FULL §10.3 live flow as real HTTP against the compose stack ` +
  `(cookie jar + SSR form POSTs, the DEV staff-auth transport which is the dev backend of the SAME pipeline ` +
  `prod uses — NOT a stub bypass; SQL/stub state-faking is BANNED). Cover, in order: no-MFA fixture REFUSED ` +
  `at sign-in; not-provisioned fixture refused; SuperAdmin fixture signs in (real cookie, MFA claim); ` +
  `dashboard renders live S1/S2 tiles (real counts); Config Registry renders the FULL v0 batch from ` +
  `core.config_entries (spot-check verification.age_gate_challenge_threshold=21, scope badges, pending chips); ` +
  `edit an ops key with reason → the config.set event read back off events_audit with {staff stf_ actor, ` +
  `hat, roles_held, reason, region, lawful_basis} AND the editor re-renders the new value AND the ` +
  `config-change tile reflects it (the ledger outcome, OBSERVED); founder-scope edit walks the interstitial; ` +
  `a non-qualifying fixture's founder-scope attempt REFUSED + admin.action.refused audited + value ` +
  `byte-identical; out-of-bounds edit refused with the registry's own message; user search executes → ` +
  `admin.user_search.executed read back + quota consumed + honest-dark zero fabricated rows; SuperAdmin ` +
  `grants EconomyOps to a 2nd fixture (event read back; new hat live without redeploy) → that fixture edits ` +
  `an ops key → revoke → denied at next action mid-session → deactivate → live session dies within the ` +
  `revalidation interval; traversal probes (%2e/%2f/%5c/dot-segment) 404 pre-auth; a behavioral page-view ` +
  `event read back. Assert BEHAVIOR (events/state/rendered values), never bare status codes; poll eventual ` +
  `consistency; stable seed ids. It is EXPECTED to fail now — that is the RED target the builders turn green.\n` +
  `(B) The deterministic gate test files in backend/tests/Svac.Tests.AdminHost (<2s lane) per §10.2: ` +
  `executor tx-atomicity (kill the tx after work() → NEITHER mutation NOR event persisted); one-event-per-action ` +
  `(no double event on config.set; envelope on verb-less); reason-refusal; MFA-claim refusal; unknown-subject ` +
  `refusal; security-stamp live revocation; deactivation bites next action; four-eyes refusal when armed; ` +
  `bounds rejection leaves value+stream untouched through the editor path; bootstrap one-shot idempotence; ` +
  `grant-race idempotency; v0-batch manifest completeness vs a checked-in transcription of eng-review §5 ` +
  `(a dropped row = a test failure); pending_consumer_slice lint honest both directions; staff-pseudonymize ` +
  `purge case. Some will not compile until the builders add the types — write them so they compile against ` +
  `the INTENDED public surface you define in a short INTERFACE-SKETCH comment at the top of the test file ` +
  `(the builders implement to it). Own ONLY backend/e2e/admin-host.e2e.mjs + backend/tests/Svac.Tests.AdminHost/**. ` +
  `Return the files written + the RED run output.` + COMMON,
  'test-author', 'Test-author'
)

// ---------- Stage 2: Pass A — auth shell + staff identity + host chrome ----------
phase('Build')
await critical(
  `PASS A (weeb-app S5) — auth shell + staff identity + host chrome, per ${CONTRACT} §1a/§1b/§8 seams ` +
  `1/4/11/12/14/15. Own: Program.cs/DI, the AddStaffAuth seam, auth transports, StaffContextProvider, the ` +
  `layout shell + nav (IDeskModule seam, role-filtered, unregistered=ABSENT), token CSS from ` +
  `design/tokens.v1.json, the EN-only string catalog + keyed-string infra. Deliver:\n` +
  `- AddStaffAuth: one claim contract + one MFA policy (require an amr=mfa / acr auth-context claim; absence ` +
  `⇒ neutral-register refusal + admin.signin.refused audit) + cookie auth (lifetime from 9A ` +
  `admin.session_lifetime_hours). PERSIST DataProtection keys to the existing core DataProtectionKeys EF ` +
  `store (public-host pattern) so cookies/antiforgery survive restart + multiple instances — the scaffold ` +
  `logged the not-persisted warning; fix it here.\n` +
  `- DevSeamsStaffTransport ([DevSeamsOnly], arch-tested never-in-prod-DI): a dev sign-in page issuing ` +
  `deterministic fixture principals with the SAME claim shape Entra emits — founder-all-roles, one-per-role, ` +
  `one WITHOUT the MFA claim, one with NO staff row. NEVER a 9A entry.\n` +
  `- Prod: OIDC via Microsoft.Identity.Web (authority/client-id from config, secret from the Key Vault seam, ` +
  `none in repo) + ProdStaffAuthGuard modeled as an UNRESOLVABLE TYPED dependency (S2 ValidateOnBuild lesson, ` +
  `the ProdFieldKeyVaultGuard family) that throws at boot in any non-Development env lacking complete Entra ` +
  `config; Development allowlisted BY NAME (Staging/QA fail closed).\n` +
  `- Principal→actor mapping, fail-closed both hops: subject → admin.staff_accounts.external_subject; no row / ` +
  `status!='active' / security_stamp mismatch ⇒ signed out+refused+audited; JIT provisioning REFUSED. Roles ` +
  `from the grants table (register the grant-table IStaffRoleResolver, overriding DenyAll), NEVER Entra claims.\n` +
  `- Revocation live twice: security_stamp on the staff row + cookie revalidation (interval from 9A ` +
  `admin.session_revalidate_seconds) re-checking stamp+status.\n` +
  `- StaffContextProvider mints a fresh RequestContext per staff op (staff ActorRef, region from the account ` +
  `row, fresh CorrelationId, Staff=StaffContext) — modules never touch HttpContext.\n` +
  `- Bootstrap: empty staff_accounts + SVAC_ADMIN_BOOTSTRAP_SUBJECT set ⇒ provision that subject + SuperAdmin ` +
  `grant as a system-actor audited action; one-shot (no path once any account exists). Env var, not 9A.\n` +
  `Gate: build green; the host boots (all boot-refusal checks pass); the DevSeams sign-in issues a cookie; ` +
  `the test-author's MFA-refusal + unknown-subject-refusal deterministic tests go GREEN. Return build+test output.` + COMMON,
  'passA-auth', 'Build'
)

// ---------- Stage 3: Pass B — AdminActionExecutor chokepoint + Staff & Roles desk ----------
await critical(
  `PASS B (weeb-app S5) — the audited-action chokepoint + Staff & Roles desk, per ${CONTRACT} §1c/§8 seam 3. ` +
  `Own: Svac.AdminHost.Domain/AdminActionExecutor + the Staff & Roles surface (Razor + form-post endpoints). ` +
  `Do NOT touch Pass A's auth files or Pass C/D surfaces. Deliver:\n` +
  `- sealed AdminActionExecutor : IAdminActionExecutor with the EXACT §1c sequence in ONE EF transaction: ` +
  `(1) re-read staff row (active? stamp?) + grants — revocation bites now; (2) hat=HatFor(action,grants), ` +
  `ctx=ctx with Staff=StaffContext(staffId,rolesHeld,hat); (3) policyEngine.Authorize(actor,action,REAL ` +
  `target) — deny ⇒ staff-visible standard deny AND admin.action.refused audited (metadata only); (4) ` +
  `four-eyes check (9A admin.four_eyes_required) fail-closed when armed; (5) reason check per the row; (6) ` +
  `work(ctx); (7) EXACTLY ONE audit event per action — verbs with a native 3A event (config.set) are NOT ` +
  `double-logged (that enrichment already lands via ctx.Staff, Phase-2a); verb-less actions ` +
  `(grant/revoke/provision/deactivate/reactivate) get ONE admin.action.executed envelope {action,target_ref,` +
  `hat,roles_held,reason} in the SAME tx as the row mutation. User-impacting events key stream_id=TARGET ref; ` +
  `staff-lifecycle events key by the staff ref.\n` +
  `- Make RequireAdminActionsCovered REAL (every executor-registered action key resolves to a PolicyTable row ` +
  `at boot or the host refuses to boot) and add the arch rule (red-fixture): no type outside ` +
  `AdminActionExecutor's namespace invokes a mutating domain-contract member from Svac.AdminHost*.\n` +
  `- Staff & Roles surface: provision / deactivate / reactivate / role_grant / role_revoke as SSR form posts, ` +
  `each carrying [PolicyAction], each routed THROUGH the executor (real TargetRefs), each with mandatory ` +
  `reason. Revocation = revoked_at state transition (never DELETE); double-grant guarded by the partial ` +
  `unique index (catch → re-read winner, idempotent-under-race). security_stamp bumped on ` +
  `deactivate/grant/revoke.\n` +
  `Gate: build green; the test-author's executor tx-atomicity, one-event-per-action, reason-refusal, ` +
  `security-stamp-revocation, deactivation-bites, grant-race deterministic tests GREEN. Return build+test output.` + COMMON,
  'passB-executor', 'Build'
)

// ---------- Stage 4: Pass C — Config Registry editor + full v0 batch + pending-consumer lint ----------
await critical(
  `PASS C (weeb-app S5) — the Config Registry editor (THE LEDGER HEADLINE) + full v0 config batch + the ` +
  `pending_consumer_slice lint, per ${CONTRACT} §4/§10 + judge §12.7. Own: the Config Registry Razor surface, ` +
  `the two 9A manifests, and the tools/ dead-tunable-lint change. Do NOT touch the executor or other surfaces ` +
  `beyond calling AdminActionExecutor. Deliver:\n` +
  `- SEED the FULL v0 batch: backend/admin-host/config/v0-batch.config.json gets EVERY eng-review §5 row in ` +
  `${CONTRACT} §4's second table (verification.*, integrity.*, match.*, premium.*, romantic.*, nakama.*, ` +
  `battle.*, invite.*, crew.*, economy.*, ads.*, characters.*, quest.*, heatmap.*, with the EXACT canonical ` +
  `keys/scopes/types/v0 values shown), each with pending_consumer_slice naming its §7 ledger slice; and ` +
  `backend/admin-host/config/admin-host.config.json gets the 5 host tunables (§4 first table). Do NOT seed ` +
  `the deliberately-excluded keys (nakama_rep_floor, ε-budget, annual Premium) — render the exclusion list. ` +
  `NEVER duplicate core.con_day_cutoff (S1-seeded; union-merge one truth).\n` +
  `- Config editor as a PURE 9A client (zero new mechanism, zero new policy rows): render ListEntries via ` +
  `QuickGrid; scope is a first-class column; founder-scope edits walk the confirm-with-reason interstitial ` +
  `(typed old→new diff, mandatory reason, explicit re-confirm — DESIGN.md neutral modal); ops-scope edits take ` +
  `an inline mandatory-reason field; set-scope rows display-only (editor refuses scope='set'). The edit goes ` +
  `THROUGH AdminActionExecutor on the existing core.config.set.founder/ops rows. Bounds violations surface ` +
  `ConfigBounds' OWN set-time rejection verbatim (never a second bounds impl); a rejected Set leaves value+stream ` +
  `byte-identical. Pending keys render a 'consumer lands at S<N>' chip, honestly dark but editable. ` +
  `LOWERING verification.age_gate_challenge_threshold additionally trips the interstitial (direction-aware).\n` +
  `- tools/ dead-tunable lint: add the manifest field pending_consumer_slice: accept a pending key iff its ` +
  `value names a BUILD.md §7 ledger slice, AND fail CI when that slice is marked DONE without a registered ` +
  `consumer claiming the key; a key neither consumed nor validly pending still fails. Red-fixture both ways ` +
  `(+ tests in the tool's own test file). Do NOT adopt a desk_rendered satisfaction mode.\n` +
  `Gate: build green; the test-author's v0-batch-completeness, bounds-rejection-through-editor, and ` +
  `pending_consumer_slice-lint deterministic tests GREEN; node --test on the changed tool GREEN. Return output.` + COMMON,
  'passC-config', 'Build'
)

// ---------- Stage 5: Pass D — Dashboard tiles + User Search + Audit Trail ----------
await critical(
  `PASS D (weeb-app S5) — the three read surfaces, per ${CONTRACT} §8 seams 2/6/7 + §9. Own: the Dashboard, ` +
  `User Search, and Audit Trail Razor surfaces + the IMetricsTileSource/IUserSearchSource registrations. ` +
  `Deliver:\n` +
  `- Dashboard: IMetricsTileSource { TileId, TitleKey, VisibleTo, Query(ct) }; register S1 tiles ` +
  `(config-change events, purge runs via IPurgeRunReader, stream volumes, staff sign-ins) + S2 tiles ` +
  `(aiml.route_decided: volume, failover, latency, provider/model mix, policy version). A tile with no live ` +
  `source is NOT registered (real-or-honestly-dark, never fabricated). The config-change tile is tile #1 (the ` +
  `slice metric). Dashboard is admin.dashboard.read (all six roles — Analyst's whole scope).\n` +
  `- User Search: the HOST-OWNED port IUserSearchSource { Search(query: HandlePrefix|EmailExact|DeviceExact, ` +
  `cursor) } with EmptyUserSearchSource registered now (S3 adapter is a later one-DI-line; honest-dark UI, ` +
  `never fabricated rows). EVERY query (even empty) is audited admin.user_search.executed {query_class,` +
  `query_term,hat} stream_id=staff ref AND quota-consumed via 10A admin.user_search.daily (cap from 9A ` +
  `admin.user_search_daily_cap). Gated to SuperAdmin/SafetyAgent/ContentModerator (Analyst excluded). The ` +
  `execute path runs THROUGH the audited flow (auth→4A→quota→audit→render).\n` +
  `- Audit Trail: IAuditReader.Query via QuickGrid, cursor-paged; SuperAdmin-only (v0); each VIEW query ` +
  `itself audited (filter metadata, not results). Staff-actor redaction as S3 established.\n` +
  `Gate: build green; the test-author's search-audit+quota+honest-dark and dashboard-live-tiles legs GREEN. ` +
  `Return build+test output.` + COMMON,
  'passD-reads', 'Build'
)

// ---------- Stage 6: finisher — drive the full E2E green + UX crawl + gate ----------
phase('Finish')
await critical(
  `FINISHER (weeb-app S5). The four builder passes are in. Your job: make backend/e2e/admin-host.e2e.mjs ` +
  `(the FULL §10.3 flow) pass GREEN end to end against a fresh compose stack, and close any seam the passes ` +
  `left. Steps: (1) dotnet build backend/Svac.sln — fix any residual break at the pass boundaries (DI wiring, ` +
  `nav registration, a missing [PolicyAction]); (2) docker compose down -v && up --build; (3) run ` +
  `admin-host.e2e.mjs against it and drive it fully green WITHOUT weakening assertions or faking state ` +
  `(SQL/stub bypass BANNED — if a real bug blocks it, FIX the bug, do not soften the test); (4) run the full ` +
  `deterministic suite (dotnet test each backend/tests project) and the changed node tools; (5) a UX-coherence ` +
  `self-check vs design/08*.dc.html + DESIGN.md (Sky actions, 1180px shell, tabular-nums, no candy, no Black ` +
  `900, no dead nav, no leaked brief labels); (6) i18n-lint the host (EN-only, keyed). Return the ACTUAL ` +
  `output of the build, the full E2E run, the suite, and the log sweep — plus an HONEST list of anything ` +
  `still red or stubbed. Do NOT claim green you did not observe.` + COMMON,
  'finisher', 'Finish'
)

return { slice: 'S5', phase: '2b', note: 'orchestrator verifies THE HARDENED GATE + commits' }

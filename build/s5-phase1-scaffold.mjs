// build/s5-phase1-scaffold.mjs — S5 Phase 1 (Scaffold) ONLY.
//
// S5's contract (SLICE_S5_CONTRACT.md) is ALREADY ratified (§13) and the combined S3+S5+S6 Phase-2a
// substrate ALREADY landed with S3 (PHASE_2A_SUBSTRATE.md; on master). So this script SKIPS Phase 0
// (design panel would overwrite the ratified contract) and SKIPS Phase-2a (substrate exists). It runs
// the single Scaffold stage, then STOPS so the orchestrator verifies the gate by hand before Phase 2
// (SLICE_PLAYBOOK: human checkpoint between phases).

export const meta = {
  name: 's5-scaffold',
  description: 'S5 admin-host: stand up the empty-but-running Blazor skeleton per the ratified contract',
  phases: [{ title: 'Scaffold' }],
}

const EXECUTION_MODEL = 'sonnet'
const CONTRACT = 'SLICE_S5_CONTRACT.md'

// Contract-beats-template clause (L33): the generic wording is a typical slice; the LOCKED contract wins.
const OVERRIDES =
  `\n\nIMPORTANT: ${CONTRACT} is LOCKED and OVERRIDES any generic wording. Build ONLY what the contract's ` +
  `deliverables assign to SCAFFOLD (skeleton, no Phase-2 logic). Create NOTHING owned by a later slice ` +
  `(the desks S8/S10/S12/S15/S18/S25/S29/S30/S33 register into S5's seams — do NOT scaffold them). If the ` +
  `contract scopes an area to "None"/"N/A", create nothing there and report "N/A per contract" with proof.`

phase('Scaffold')
await agent(
  `You are the scaffolder for weeb-app slice S5 (admin-host), a Blazor Server HOST over S1's domain core ` +
  `(LANE F, not a module). Stand up the EMPTY-BUT-RUNNING skeleton per ${CONTRACT}. The combined S3+S5+S6 ` +
  `Phase-2a substrate ALREADY LANDED on this branch's base (PHASE_2A_SUBSTRATE.md): StaffRole, ` +
  `PolicyTableEntry.StaffRoles, IStaffRoleResolver + DenyAllStaffRoleResolver, StaffContext, ` +
  `RequestContext.Staff, IPolicyTableSource union + duplicate-key boot refusal, IConfigRegistry.ListEntries, ` +
  `IAuditReader (Query takes CursorPageRequest), IPurgeRunReader (Recent takes CursorPageRequest), HatFor ` +
  `(Deterministic, golden-vectored — SelectLeastPrivileged over int ordinals), the stf/srg IdPrefixes, and ` +
  `the async 3-axis PolicyEngine.Authorize all EXIST and are VERIFIED present — do NOT re-add or modify any ` +
  `domain-core contract; CONSUME them.\n` +
  `EXPLICITLY OUT OF SCAFFOLD (deferred to Phase 2's domain-core sub-pass, where they get a byte-identical ` +
  `proof): (a) enriching ConfigRegistry.SetValue's audit payload with {hat, roles_held} from ` +
  `RequestContext.Staff; (b) the two additive core.events_audit read indexes ((event_type, recorded_at DESC), ` +
  `(actor_ref, recorded_at DESC)). Do NOT touch domain-core in this scaffold. If you find you need either to ` +
  `make the host BOOT or BUILD, STOP and report — you should not.\n\n` +
  `SCAFFOLD DELIVERABLES (skeleton only — stubs, no business logic):\n` +
  `1. backend/admin-host/Svac.AdminHost (Blazor Server, .NET 10, static SSR + enhanced form POSTs per ` +
  `§1a; NO admin framework, NO themed component lib; add Microsoft.AspNetCore.Components.QuickGrid) and ` +
  `backend/admin-host/Svac.AdminHost.Domain (internal, InternalsVisibleTo the test project) — the S2 ` +
  `module-template assembly discipline applied to a host. Add both to backend/Svac.sln.\n` +
  `2. backend/tests/Svac.Tests.AdminHost (xUnit harness, added to the sln).\n` +
  `3. AdminDbContext for schema 'admin' with the TWO tables EXACTLY as §2 DDL (admin.staff_accounts, ` +
  `admin.staff_role_grants incl. the ux_active_grant partial unique index) + one EF migration; NO other ` +
  `table; DELETE revoked from the app role on both. Migrations apply on startup via the S1 migration ` +
  `service under the advisory lock, stream consumers register AFTER.\n` +
  `4. A stub AdminPolicyTableSource (IPolicyTableSource) registering the §3 rows so RequireAdminActionsCovered ` +
  `+ RequireMutationsPolicyMapped pass at boot; a stub sign-in page + one dashboard stub route carrying ` +
  `[PolicyAction]/the admin.host.transport mapping honestly. Endpoints are stubs (return the shell), but ` +
  `their 4A wiring must be real so the host BOOTS (S1 boot-refusal law).\n` +
  `5. Two additive 9A manifests as EMPTY-VALID skeletons: backend/admin-host/config/admin-host.config.json ` +
  `and backend/admin-host/config/v0-batch.config.json (sibling union-merge format per S1). Full row ` +
  `seeding is Phase 2 — scaffold just the valid files + wiring so they load. Do NOT edit domain-core.config.json.\n` +
  `6. Compose wiring: add the admin host as a service on a DISTINCT port; it must reach postgres/redis/etc.\n` +
  `7. One additive Bicep module under infra/ for the admin host as its own Container App with ` +
  `internal/allowlisted ingress; bicep build-validated; undeployable until OQ-3 (fine).\n` +
  `8. Add admin's TWO rows to backend/domain-core/purge-registry.json per §6 (admin.staff_accounts, ` +
  `admin.staff_role_grants with their disposition verbs) — REQUIRED so the 13A CI gate stays non-vacuous ` +
  `now that AdminDbContext maps two new stores. Re-prove the purge-completeness gate.\n` +
  `9. Assert byte-identical: contracts/openapi.v0.json + contracts/message-keys.json UNCHANGED (no ` +
  `Svac.AdminHost* type in the document).\n\n` +
  `GATE (re-run yourself, return the ACTUAL output not a narrative): dotnet build backend/Svac.sln clean; ` +
  `the admin host boots (RequireAdminActionsCovered + RequireMutationsPolicyMapped + policy-source ` +
  `duplicate-key checks all pass); a trivial container/integration test; compose health (/health 200 on ` +
  `the admin host AND the existing public host, contract emits byte-identical); ef-gate idempotent if you ` +
  `added a migration. Report anything you could NOT complete honestly — do not paper over.` + OVERRIDES,
  { label: 'scaffolder', phase: 'Scaffold', model: EXECUTION_MODEL }
)

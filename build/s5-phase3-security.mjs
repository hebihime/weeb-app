// build/s5-phase3-security.mjs — S5 Phase 3 (Security): adversarial review of the admin/staff trust boundary.
//
// Phases 1/2a/2b are DONE + committed (HARDENED GATE verified: build 0/0; suite 455/0/10; e2e 20/20 on two
// fresh boots). This runs N adversarial lenses (fable — best model, bookend, no compiler checks the
// reasoning) → triage (routing schema) → remediate (sonnet, each fix proven by a now-green test) →
// SECURITY_REVIEW_S5.md. The orchestrator re-verifies THE HARDENED GATE + commits after.

export const meta = {
  name: 's5-security',
  description: 'S5 admin-host Phase 3: adversarial lenses on the staff trust boundary, triage, remediate to green tests',
  phases: [{ title: 'Lenses' }, { title: 'Triage' }, { title: 'Remediate' }],
}

const JUDGE = 'fable'
const EXEC = 'sonnet'
const CONTRACT = 'SLICE_S5_CONTRACT.md'

async function critical(prompt, opts, label) {
  let r = await agent(prompt, opts)
  if (r == null) { log(`${label}: null — retry once`); r = await agent(prompt, opts) }
  if (r == null) throw new Error(`${label} died twice — resume-from-runId`)
  return r
}

// S5-specific adversarial lenses (the standing SLICE_PLAYBOOK set specialized to a staff/admin host).
const LENSES = [
  ['auth-idor-staff', 'AUTH / IDOR / no-admin-from-consumer. Try to reach any admin.* action or admin data ' +
    'from Svac.PublicApi or as a consumer/anonymous actor; defeat staff-only reachability; bypass ' +
    'RequireAdminActionsCovered / RequireMutationsPolicyMapped; escalate the 4A Role axis (act with a hat ' +
    'you were not granted; make HatFor pick a higher hat); reach a mutating domain-contract member from UI ' +
    'code WITHOUT going through AdminActionExecutor; encoded-traversal / path-mount the admin host under a ' +
    'consumer origin (%2e/%2f/%5c/dot-segment). Entra authenticates but Svac authorizes — try to make an ' +
    'Entra app-role/group grant confer power, or JIT-provision an unknown subject.'],
  ['authz-executor', 'THE CHOKEPOINT. Break AdminActionExecutor\'s §1c 7-step invariants: make work() commit ' +
    'without a passing Authorize; make a deny NOT get audited (admin.action.refused) or a success get a ' +
    'DOUBLE event (config.set enrichment vs envelope); make the audit event + row mutation NOT share one tx ' +
    '(kill the tx after work → assert neither persists); bypass the mandatory-reason / whitespace-reason ' +
    'check; defeat four-eyes when armed (admin.four_eyes_required). Confirm the stream_id keying (target ref ' +
    'for user-impacting, staff ref for lifecycle) so a subject purge FINDS staff actions about them.'],
  ['auth-lifecycle-revocation', 'STAFF LIFECYCLE + REVOCATION + LOCKOUT. Attack security_stamp revalidation ' +
    '(keep a deactivated/revoked operator\'s LIVE session working past the interval; race stamp bump vs ' +
    'in-flight action); the executor per-action re-read (revocation must bite at the next mutation); MFA-claim ' +
    'refusal + no-staff-row refusal + status!=active. KNOWN MUST-FIX (seeded, build it): there is NO guard ' +
    'against revoke/deactivate dropping the ACTIVE SuperAdmin count to zero → founder self-lockout. Prove it ' +
    'and specify the fix: refuse any revoke/deactivate that would leave zero active SuperAdmins, regression-tested.'],
  ['pii-residency-staff', 'PII / RESIDENCY / PURGE. Staff are data subjects (§ region+lawful_basis on both ' +
    'tables). Attack: staff PII (email/display_name/external_subject) not pseudonymized on statutory_erasure ' +
    'while stf_/srg_ ids must survive (audit chain); the staff-pseudonymize purge case (seed → erase → chain ' +
    'resolves stf_ → PII gone); search-query events carrying a searched email (a subject\'s PII inside an ' +
    'event keyed by the staff ref) — is it purge-reachable when THAT subject erases? region never ' +
    'client-settable; declared at provisioning. DataProtection keys: confirm they persist to the core store ' +
    '(cookie/antiforgery survive restart + multi-instance) and are not a plaintext-at-rest hole.'],
  ['config-authority', 'THE CONFIG EDITOR (the ledger headline) as an authority surface. Bypass the ' +
    'founder-scope confirm-with-reason interstitial (commit on the propose step; skip the re-confirm); edit a ' +
    'set-scope row (must be display-only / refused); defeat bounds by editing through the editor with a second ' +
    'bounds implementation instead of ConfigBounds\' own set-time rejection; make a rejected Set mutate the ' +
    'value or append a stream event (must be byte-identical on reject); forge the {hat, roles_held} on the ' +
    'config.set event; the direction-aware rule (LOWERING age_gate_challenge_threshold must ALSO trip the ' +
    'interstitial). pending_consumer_slice lint: legalize a genuinely-dead key, or make a DONE slice with an ' +
    'unclaimed key pass CI.'],
  ['concurrency-search', 'CONCURRENCY + SEARCH-DOOR ABUSE. The double-grant partial-unique-index race (two ' +
    'concurrent grants of the same role → transient dup or a crash instead of idempotent re-read); two ' +
    'SuperAdmins editing one key (last-write-wins but BOTH audit events must persist); the Blazor static-SSR ' +
    'scoped-DbContext race the finisher fixed via DbContextFactory (confirm no shared-context second-operation ' +
    'hazard remains on ANY desk page or layout read). User search: bypass the 10A admin.user_search.daily ' +
    'quota (a compromised staff session bulk-enumerating users); make an empty/honest-dark search skip the ' +
    'audit or the quota consume; fabricate result rows.'],
  ['silent-rejection-minor', 'SILENT-REJECTION LEAKS + MINOR-PROTECTION baseline. Staff denials are a standard ' +
    'staff deny (staff may see reasons) but must NOT leak a consumer-observable channel or change the ' +
    'consumer absence/one-deny-shape coercion (assert the existing suite still holds). Confirm S5 adds NO ' +
    'consumer-facing surface and NO minor-reachable path (staff are 18+ by employment); no new 18+ invariant ' +
    'is weakened. Any admin read that could echo a subject\'s existence to an unauthorized hat is a finding.'],
]

phase('Lenses')
const findings = await parallel(LENSES.map(([key, brief]) => () =>
  agent(
    `You are a skeptical adversary with ONE lens for weeb-app slice S5 (the admin/staff host) per ${CONTRACT} ` +
    `and the RUNNING code under backend/admin-host/. LENS: ${brief}\n\n` +
    `Read the actual shipped code (backend/admin-host/**, the executor, auth, config editor, the arch tests ` +
    `already present) — do NOT theorize from the contract alone. For every real break: give severity ` +
    `(CRITICAL/HIGH/MEDIUM/LOW), exact file:line, and a demonstrated exploit (concrete inputs → wrong ` +
    `result/state). Prefer a FAILING test that proves it (name the test file you would add). If the lens ` +
    `finds nothing real, say so in one line — do not manufacture findings. Return text (schema-free).`,
    { label: `lens-${key}`, phase: 'Lenses', model: JUDGE }
  )
)).then(f => f.filter(Boolean))

phase('Triage')
const triage = await critical(
  `Triage these S5 adversarial findings into fixNow (every CRITICAL/HIGH + ALL cheap trust/residency/` +
  `minor-protection/lockout fixes, incl. the seeded last-SuperAdmin lockout guard) vs defer (expensive ` +
  `MEDIUM/LOW, each getting a [Fact(Skip="deferred: SECURITY_REVIEW_S5.md <id>")] proof test). Drop ` +
  `non-findings. Return the routed list with a stable id + one-line fix per item.\n\n` +
  findings.map((f, i) => `=== LENS ${i + 1} ===\n${f}`).join('\n\n'),
  { label: 'triage', phase: 'Triage', model: JUDGE,
    schema: { type: 'object', properties: {
      fixNow: { type: 'array', items: { type: 'string' } },
      defer: { type: 'array', items: { type: 'string' } } }, required: ['fixNow', 'defer'] } },
  'triage'
)

phase('Remediate')
await critical(
  `Remediate EVERY fixNow item for weeb-app slice S5, each proven by its own now-green test (deterministic ` +
  `where possible; a live-E2E leg if it needs the running host). The last-SuperAdmin lockout guard IS a ` +
  `fixNow item — build it in AdminActionExecutor / the Staff & Roles path (refuse any revoke/deactivate ` +
  `leaving zero active SuperAdmins) with a regression test. Defer items get a Skip-annotated proof test so ` +
  `the suite stays green with the proof in code. Do NOT weaken any existing test or assertion; never edit ` +
  `domain-core contracts (consume the substrate). Keep 'dotnet build backend/Svac.sln' green. WRITE ` +
  `SECURITY_REVIEW_S5.md (mandated-verdicts + fixNow table each with its now-green test + defer table + ` +
  `documented residuals + verified-sound list, the SECURITY_REVIEW_S3.md format). Re-run the deterministic ` +
  `suite + the arch suite yourself and return the ACTUAL output. Report honestly anything you could not fix.` +
  `\n\nTRIAGE:\n${JSON.stringify(triage)}`,
  { label: 'remediate', phase: 'Remediate', model: EXEC },
  'remediate'
)

return { slice: 'S5', phase: '3', lenses: LENSES.length, findings: findings.length,
  note: 'orchestrator re-verifies THE HARDENED GATE + commits' }

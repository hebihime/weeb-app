// build/workflow-template.mjs — the per-slice pipeline as a Claude Code Workflow script.
//
// Usage: Workflow({ scriptPath: 'build/workflow-template.mjs', args: { slice: 'S14', contexts: ['matching'], ratified: false } })
// First run stops after Phase 0 (human ratification). Re-run with args.ratified = true (and
// resumeFromRunId to cache Phase 0) to execute Phases 1-3.
//
// Encodes the mechanics from SLICE_PLAYBOOK.md (each a real, paid-for failure):
//   - phases visible in /workflows
//   - INLINE agent prompts (custom agentTypes do NOT resolve in the workflow runtime) + explicit model:
//   - schema ONLY on small bounded outputs (triage); high-volume stages schema-free + parsed defensively
//   - null-guard + one retry on critical single-agent stages; resume-from-runId replays cached stages
//   - shared-wiring pre-step so parallel builders never collide; single module = sequential chain
//   - test-author front-loads the REAL end-to-end flow, SQL/stub bypasses banned
//   - findings ship a failing test; human checkpoint after Phase 0

export const meta = {
  name: 'weeb-slice-pipeline',
  description: 'One weeb-app vertical slice: design panel, scaffold, build, security; execution-gated',
  phases: [
    { title: 'Design' },
    { title: 'Scaffold' },
    { title: 'Build' },
    { title: 'Security' },
  ],
}

const JUDGMENT_MODEL = 'opus'    // bookends: no compiler checks design/security reasoning
const EXECUTION_MODEL = 'sonnet' // middle: tests/types/compose catch mistakes

const SLICE = args?.slice ?? 'S?'
const CONTRACT_PATH = `SLICE_${SLICE}_CONTRACT.md`
const RATIFIED = args?.ratified === true

// critical single-agent stage: null-guard + one retry
async function critical(prompt, opts, label) {
  let r = await agent(prompt, opts)
  if (r == null) { log(`${label}: null (transient) — retrying once`); r = await agent(prompt, opts) }
  if (r == null) throw new Error(`${label} died twice — stop, inspect, resume-from-runId`)
  return r
}

// ---------- Phase 0: design panel (judgment model, divergent biases) ----------
phase('Design')
const BIASES = [
  'Bias: SIMPLICITY. Fewest moving parts, smallest contract that finishes the actor journey.',
  'Bias: EXTRACTION-GRADE SEAMS. Module isolation (1A), opaque-id refs, one-interface-per-vendor buy seams, 3A/4A/9A/10A/13A entries complete.',
  'Bias: PRIVACY + i18n + RESIDENCY. Region/lawful-basis plumbing, special-category handling, silent-rejection unobservability, keyed strings x4 locales, absence law as server truth.',
]
const proposals = await parallel(BIASES.map((bias, i) => () =>
  agent(
    `You are slice architect #${i + 1} for weeb-app slice "${SLICE}". ${bias}\n` +
    `Read BUILD.md (the ledger row for ${SLICE}, §4 lifecycle cells, §9 seams), DESIGN.md, the relevant ` +
    `design/0X *.dc.html file(s), and the authoritative spec doc(s) named in the ledger row. The ratified ` +
    `rulings (1A-15A, T-series, ER-series, DR-series, R-series) are LOCKED constraints, never up for debate.\n` +
    `Produce ONE independent contract proposal: module API surface + OpenAPI delta, module-owned schema DDL ` +
    `(no cross-module joins), 4A policy entries per mutation, 9A config entries, 10A quota keys, 13A store ` +
    `registrations, notification taxonomy rows touched, the BUILD.md §9 seams made concrete, the dependency ` +
    `classification (swap-safe / seam-now / must-build-first) for every not-yet-built system it reads, and ` +
    `the outcome the slice moves. Return the proposal as text.`,
    { label: `architect-${i + 1}`, phase: 'Design', model: JUDGMENT_MODEL } // schema-free: large output
  )
)).then(a => a.filter(Boolean))

await critical(
  `You are the design judge for weeb-app slice "${SLICE}". Synthesize ONE locked contract from these ` +
  `${proposals.length} proposals, grafting the best of each and resolving conflicts with stated reasoning. ` +
  `A proposal contradicting a ratified ruling loses that point automatically; note the contradiction. ` +
  `Leave explicit OPEN QUESTIONS only where a call is genuinely Julien's. WRITE the contract to ` +
  `${CONTRACT_PATH} with the Write tool. Return only "WROTE ${CONTRACT_PATH}" plus a 5-line summary.\n\n` +
  proposals.map((p, i) => `=== PROPOSAL ${i + 1} ===\n${p}`).join('\n\n'),
  { label: 'judge', phase: 'Design', model: JUDGMENT_MODEL },
  'judge'
)

if (!RATIFIED) {
  log(`HUMAN CHECKPOINT: review ${CONTRACT_PATH}, resolve open questions, then re-run with ` +
      `args.ratified=true and resumeFromRunId to continue. Stopping here by design.`)
  return { slice: SLICE, contract: CONTRACT_PATH, status: 'awaiting-ratification' }
}

// ---------- Phase 1: scaffold (execution model) ----------
phase('Scaffold')
await agent(
  `Scaffold the empty-but-running skeleton for weeb-app slice "${SLICE}" per ${CONTRACT_PATH}: module ` +
  `project(s) with internal assemblies + public contract assembly, EF migrations, endpoint stubs WITH ` +
  `their 4A policy entries registered, 13A store registrations, compose wiring, OpenAPI emit, test ` +
  `harness. Migrations apply via the startup migration service under the advisory lock; stream consumers ` +
  `register AFTER it. Gate: dotnet build clean + trivial container test + compose health (/health 200 on ` +
  `all hosts, contract emits). Re-run the gate yourself; return its actual output, not a narrative.`,
  { label: 'scaffolder', phase: 'Scaffold', model: EXECUTION_MODEL }
)

// ---------- Phase 2: build ----------
phase('Build')
// 2a: shared-wiring pre-step owns entrypoint + cross-cutting files so builders never collide.
await agent(
  `Shared-wiring pre-step for weeb-app slice "${SLICE}": own the entrypoint + cross-cutting files ` +
  `(Program.cs/DI, policy+quota+config registrations, compose, OpenAPI regeneration) per ${CONTRACT_PATH}. ` +
  `Leave each module's endpoint file + migration as a stub for its builder. dotnet build clean.`,
  { label: 'shared-wiring', phase: 'Build', model: EXECUTION_MODEL }
)
// 2b: test-author FIRST (front-load the real end-to-end flow; bypasses banned), then builders.
await critical(
  `You are the test-author for weeb-app slice "${SLICE}" per ${CONTRACT_PATH} and BUILD.md §8. Write the ` +
  `hardest end-to-end piece FIRST as a RED live E2E committed to backend/e2e/ (+ Maestro flow where a ` +
  `client surface exists): real endpoints, real auth, real 4A authz composition, real Mailpit email ` +
  `fallback, real 3A events. SQL/stub bypasses are BANNED - never fake state with raw SQL, never assert a ` +
  `stub green. Assertions verify BEHAVIOR, never status codes. TWO API instances on one backplane for ` +
  `realtime. Every 3A stream consumer this slice adds gets a FOREIGN-event skip test. Negative proofs: ` +
  `anon mutation fails closed; smuggled trust field ignored; deny/void/exclusion/tier-floor unobservable; ` +
  `13A purge classes assert zero against seeded stores. Own the test tree only.`,
  { label: 'test-author', phase: 'Build', model: EXECUTION_MODEL },
  'test-author'
)
const CONTEXTS = args?.contexts ?? ['single-module'] // length 1 = sequential chain, no fan-out
const SEQUENTIAL = CONTEXTS.length === 1
const buildOne = ctx => agent(
  `Build module "${ctx}" for weeb-app slice "${SLICE}" test-first against ${CONTRACT_PATH}. Own ONLY ` +
  `${ctx}'s projects, its endpoint file, its migration - never Program.cs, never another module. Honor ` +
  `BUILD.md §9 seams: events in the same tx (3A), quotas via 10A Consume, tunables via 9A, model calls ` +
  `only through IAimlRouter, trust fields absent from request DTOs, idempotent-under-race writes, keyed ` +
  `strings x4 locales, absence-shaped responses (no leaking error codes). Make the test-author's red ` +
  `tests green. Return the build+test result you re-ran yourself.`,
  { label: `builder-${ctx}`, phase: 'Build', model: EXECUTION_MODEL }
)
if (SEQUENTIAL) { await buildOne(CONTEXTS[0]) }
else { await parallel(CONTEXTS.map(ctx => () => buildOne(ctx))) }
await agent(
  `Wire the client(s) for weeb-app slice "${SLICE}" per ${CONTRACT_PATH}: regenerate typed clients from ` +
  `the OpenAPI contract (read-only; contract changes go back to backend), swap seed to API, wire the ` +
  `authed flow, hold DESIGN.md token laws + the absence law + silent rejection + one limit surface + ` +
  `DR-6.1 a11y, keep BOTH brand flavors building. Return typecheck + tests + both-flavor build results.`,
  { label: 'integrator', phase: 'Build', model: EXECUTION_MODEL }
)
log('BUILD gate is THE HARDENED GATE (BUILD.md §8) — run it YOURSELF: build+tests both flavors, ' +
    'fresh-boot health (down -v, up, zero errors/restarts), live E2E + post-E2E log sweep on ALL ' +
    'instances, UX crawl vs design/*.dc.html, suite TWICE. The agents\' "green" is a lead, not proof.')

// ---------- Phase 3: security (judgment model; schema only on the small triage output) ----------
phase('Security')
const LENSES = [
  'auth/IDOR (4A refusal, topology-is-not-a-guard, encoded traversal)',
  'PII/residency + special-category (encryption, never-partner-visible, region/lawful-basis, blob contents)',
  'trust-boundary (DTO trust absence, money-doors fail closed, never-pay-to-rank invariants)',
  'concurrency (R5 supersession, quota Consume, both-confirm, messy-input bulk paths)',
  'silent-rejection leaks (deny/void/exclusion/tier-floor unobservable; no probe-able re-deal; no refund tells)',
  'purge completeness (13A classes vs seeded stores; derivatives inherit lifetime)',
  'minor-protection (L1-L4 stack; 18+ invariants incl. character metric gate)',
]
const findings = await parallel(LENSES.map(lens => () =>
  agent(
    `You are a skeptical adversary with ONE lens: ${lens}. Try to BREAK weeb-app slice "${SLICE}" per ` +
    `${CONTRACT_PATH} and the running code. Every finding ships a FAILING test in a lens-specific test ` +
    `file. Return findings as text (schema-free): severity, exact file:line, demonstrated break ` +
    `(inputs -> wrong result). If the lens does not apply to this slice, say so in one line.`,
    { label: `lens-${lens.split(' ')[0]}`, phase: 'Security', model: JUDGMENT_MODEL }
  )
)).then(f => f.filter(Boolean))
const triage = await critical(
  `Triage these findings for weeb-app slice "${SLICE}" into fixNow (high/critical + ALL cheap trust/` +
  `residency/minor-protection fixes) vs defer (expensive medium/low; each gets a Skip-annotated test). ` +
  `Return the routed list.\n\n${findings.join('\n\n---\n\n')}`,
  { label: 'triage', phase: 'Security', model: JUDGMENT_MODEL,
    schema: { type: 'object', properties: { fixNow: { type: 'array', items: { type: 'string' } },
      defer: { type: 'array', items: { type: 'string' } } }, required: ['fixNow', 'defer'] } },
  'triage'
)
await agent(
  `Remediate every fixNow item for weeb-app slice "${SLICE}", each proven by its now-green test. Defer ` +
  `items get [Fact(Skip="deferred: SECURITY_REVIEW finding")] so the suite stays green with the proof in ` +
  `code. Write SECURITY_REVIEW_${SLICE}.md. Re-run the full gate; return its actual result.\n\n` +
  JSON.stringify(triage),
  { label: 'remediate', phase: 'Security', model: EXECUTION_MODEL }
)
return { slice: SLICE, contract: CONTRACT_PATH, contexts: CONTEXTS, lenses: LENSES.length, findings: findings.length }

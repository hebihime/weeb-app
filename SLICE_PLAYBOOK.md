# SLICE_PLAYBOOK.md — the proven pattern for shipping one vertical slice

The repeatable path for every BUILD.md §7 ledger row. Do not re-derive it per slice. A slice ships one
actor's journey end to end (write path + read path + surface), on known-good main, ending at THE
HARDENED GATE (BUILD.md §8).

## The phase pipeline (each phase ends at a gate; human checkpoint between phases)

| Phase | Agents (model) | Output | Gate |
|---|---|---|---|
| **0 Design panel** | 3× slice-architect (fable, divergent biases: simplicity / extraction-grade seams / i18n+privacy+residency) + 1 design-judge (fable) | `SLICE_<N>_CONTRACT.md` | JULIEN reviews + resolves open questions; ratify before any code |
| **1 Scaffold** | 1× scaffolder (sonnet) | empty-but-running skeleton | `dotnet build` clean + trivial container test + compose health |
| **2 Build** | shared-wiring → parallel context builders → test-author + frontend-integrator → capped verify (sonnet) | working slice | THE HARDENED GATE (below) |
| **3 Security** | N adversarial lenses (fable) → triage → capped remediate | `SECURITY_REVIEW_<N>.md` | high/critical cleared, each via a now-green test |
| **4 Retro** | orchestrator | update this file + memory | next ledger row chosen |

**Model rule (scar L25):** judgment model = the BEST available model (fable today; re-check when a new
family ships) on the bookends, where no compiler checks the work — never a downgrade (CLAUDE.md);
execution model (sonnet) through the middle, where tests/types/compose catch mistakes; cheapest (haiku)
for pure boilerplate.

**Phase 0 must read, per slice:** BUILD.md (§4 lifecycle cells + §9 seams for this slice), DESIGN.md,
the relevant `design/0X *.dc.html` file(s), and the authoritative spec doc(s) named in the ledger row's
rulings column. The rulings are LOCKED; the contract makes them concrete, it does not relitigate them.

**Phase 3 standing lenses for this product** (add slice-specific ones): auth/IDOR · PII/residency +
special-category · trust-boundary (never-pay-to-rank, never-exposed-reputation, server-authoritative
verification) · concurrency · silent-rejection leaks (a deny, a void, an exclusion, or a tier floor must
be unobservable: no distinct error code, timing, or state diff) · purge completeness (13A: seed the
store, run every purge class, assert zero) · minor-protection (every new surface checked against the
L1–L4 stack and the 18+ invariant).

## THE HARDENED GATE (non-negotiable; each clause a real scar; full text BUILD.md §8)

1. Build + tests clean on every layer (backend arch suite included; both client flavors).
2. Compose stack healthy + **fresh-boot health**: zero startup errors, zero restarts, on a clean
   `down -v` → `up`, on EVERY instance. Read the logs; self-healing races pass the E2E while erroring.
3. Web/prerendered surfaces asserted against the **prod build artifact**, never dev-mode SSR (skip with
   a note on native-only slices).
4. **Live end-to-end UI→API flow**, committed (`backend/e2e/*.mjs` + Maestro flows); TWO API instances on
   one backplane with clients on different instances for realtime slices; **post-E2E log sweep on all
   instances = zero exceptions**; no hollow assertions; poll eventual consistency; stable seed ids.
5. **UX-coherence crawl**: design-DISPLAY vs actual-UI reconciliation against `design/*.dc.html`; no dead
   links; no leaked brief labels; absence law + silent rejection + one limit surface verified by eye.
6. **No flaky tests under parallel load**: cap parallelism (4 to start, re-measure), run the suite TWICE.
7. **Shared streams + merge gate**: every 3A stream consumer has a FOREIGN-event skip test; a parallel-
   lane wave merges serially `--no-ff`, then the FULL suite on the combined tree AND all lanes' E2Es on
   ONE shared stack.

Two oracles: assertions AND logs. Re-run the gate yourself; read the diff; never trust an agent's "green."

## Design-now seams (bake into every Phase-0 contract; the full mapping is BUILD.md §9)

Module-boundary isolation (1A arch tests) · region/lawful-basis on every PII row · events on the 3A
substrate in the same tx · trust fields absent from request DTOs · keyed strings ×4 locales ·
SEO boundary (web) · metrics verified received · consent written by a surface · one interface per vendor
(DevSeams-gated seed impls, never DI-registered in prod) · 4A policy entry per mutation · edge guard vs
encoded traversal · money-door secrets fail closed · rank by attestation only · messy-input quarantine on
bulk paths · content-scan blobs · real-or-honestly-dark, never fabricated · idempotent-under-race writes ·
deterministic math in pure libs with golden vectors, no LLM.

## Required Phase-0 outputs

- The slice contract: module API surface + OpenAPI delta, schema DDL (module-owned), 4A policy entries,
  9A config entries, quota keys (10A), 13A store registrations, notification taxonomy rows touched
  (closure rule), the seams above made concrete.
- **Dependency classification** for every not-yet-built system the slice reads: swap-safe / seam-now /
  must-build-first. Keeps spoofed trust structurally un-shippable.
- The outcome the slice moves (its ledger-row metric) and where it renders on the Metrics & Ops desk.
- Open questions ONLY where a call is genuinely Julien's (a value, a permanent ruling). Do not paper over
  a real fork with a default (Confusion Protocol).

## Workflow mechanics (don't relearn; each cost real tokens)

- Run EVERY phase as a Workflow (visible in /workflows), even single-agent phases.
- Custom agentTypes do NOT resolve inside the Workflow runtime (L26): inline the role's prompt from
  `.claude/agents/*.md` and pass `model:` explicitly. The agent files still serve the interactive Agent tool.
- Shared-wiring pre-step owns the entrypoint + cross-cutting files (Program.cs, DI, policy/quota/config
  registrations, compose); builders own DISJOINT modules. A single module cannot be built by parallel
  agents: run it as a sequential chain, clients in parallel (separate trees).
- Do NOT put `schema:` on high-volume agent stages (L27: uncatchable retry-cap crash, ~2M tokens wasted
  once): schema-free text + defensive parse. Reserve schema for small bounded outputs (triage routing).
- Guard every `agent()` result against null + one retry (L28); resume-from-runId replays cached stages.
- Workflow `args` can arrive JSON-STRINGIFIED (L32, cost one full design panel): on a string,
  `args?.slice` is `String.prototype.slice` — a function, non-nullish, so `??` fallbacks never fire and
  agents launch with garbage interpolated into every prompt and filename. Parse defensively
  (`typeof args === 'string' ? JSON.parse(args) : args`) and FAIL FAST validating every arg against its
  expected shape before the first `agent()` call.
- A stage producing a large artifact WRITES the file and returns a pointer, never emits it as text.
- **Front-load the real end-to-end flow (L30):** the test-author writes the hardest path FIRST (real
  endpoints + real auth + Mailpit email + real 3A events), SQL/stub bypasses explicitly banned. Assume a
  "done" report on a complex slice hides a stubbed end-to-end piece until the live E2E proves otherwise.
- Findings ship a failing test; high/critical stay active; deferred medium/low get a
  `[Fact(Skip="deferred: SECURITY_REVIEW finding")]`. Never defer cheap trust/residency/minor-protection
  fixes.
- Split Phase 2 when a slice mutates a DONE module: 2a mutation + byte-identical-behavior proof + rerun
  of the mutated module's existing E2E, then 2b builders fan out.
- The CONTRACT beats the TEMPLATE, by instruction not by agent judgment (L33, cost a kill+cleanup at
  S0): the template's generic stage prompts describe a typical slice; when a contract scopes a stage to
  "None", an agent following the generic wording builds a LATER slice's units off-contract (shared-wiring
  built S1's whole domain substrate during S0). Every execution-stage prompt now appends an explicit
  contract-overrides clause; keep it when editing prompts, and quarantine-don't-trust any off-contract
  output an agent produces anyway.
- Parallel-lane waves (lanes A–F only): each lane in its own worktree with a port-isolated compose stack;
  the coordinator owns ALL git; the merge gate is its own stage (BUILD.md §8 clause 7).

---

## Per-slice retros (the compounding record; fill one per slice)

### S<N> retro (<name> — status)
**Shipped:** ...
**Green:** (exact numbers: arch + integration + client tests + live E2E + log sweep + security)
**What worked:** ...
**What we learned / changed:** ... (fold new gate/seam lessons UP into BUILD.md §8/§9 so the next slice inherits them)

### S0 retro (repo-ci-iac — DONE, 2026-07-10)
**Shipped:** ratified contract (SLICE_S0_CONTRACT.md) → 7 per-unit workflows (guarded activation, green
on empty solution), infra/ Bicep ×9 files + 3 param sets (edge-guard L17 with UrlDecode-hardened rules,
residency assertions), brands/*.json + drift gate, i18n/locales.json, 5 deterministic lint tools +
secret-scan + compose-smoke, empty Svac.sln + Directory.Build.props, compose dev stack, CODEOWNERS,
SECURITY_REVIEW_S0.md (15 fixNow remediated, 7 deferred with Skip-annotated proof tests, all lens tests
wired into CI). B18 dead: flavorless first client commit is red by construction.
**Green:** 126 node tests (119 pass, 7 deliberate skips = deferrals) ×2 runs · dotnet build 0 errors ·
brand-gate OK · actionlint 0 · bicep 12/12 · compose fresh-boot ×3 clean (zero restarts/errors/warnings)
· GitHub CI: six workflows green twice (push set + dispatched set); release lane proven fail-closed
locally, un-greenable until OQ-1/OQ-3 by design.
**What worked:** the contract's guarded-activation pattern (every gate green on the empty repo, arming
itself as its consumer lands); the scaffolder explicitly deferring to the LOCKED contract over its
generic prompt; adversarial lenses on pure tooling found 22 real breaks (edge-guard double-encoding
bypass, ddl-lint blind to schema-qualified tables, opt-in lint markers = zero pressure); null-guard +
retry absorbed two real 529 agent deaths (L28 validated).
**What we learned / changed:** (1) L32 stringified Workflow args — parse + fail-fast now in the
template. (2) L33 contract-beats-template — override clause appended to every execution-stage prompt
after shared-wiring built S1's whole substrate during S0; quarantine-don't-trust any off-contract agent
output. (3) Secret-scan shipped two real bugs: grep -q + pipefail FAILS OPEN on large diffs (SIGPIPE
turns a match into pipeline failure), and inline pattern text self-matches the commit adding it — one
shared tested scanner (secret-scan.mjs + secret-patterns.txt, exempting exactly the pattern file)
replaced both inline scans; test vectors must be concatenation-built or they re-trip the scan. (4)
Judgment-model pins in the build kit went stale (opus-era); playbook now says BEST AVAILABLE, re-check
each model family. (5) Verify agent-claimed tool versions: the scaffolder's bicep validated in its
sandbox but wasn't on the machine; the gate re-run caught it.

### S1 retro (domain-substrate — DONE, 2026-07-10)
**Shipped:** the whole domain core in one slice — 3A event substrate (6 typed streams, append-only
in-DB trigger, GlobalSeq replay), 4A policy engine (closed deny-mode union, startup boot-refusal +
request-time refusal), 9A config registry (typed, manifest-seeded at boot, dead-tunable lint), 10A quota
(atomic UPSERT-where, one LimitReached shape), 13A purge registry + CI gate (closed class taxonomy,
CryptoShred verb, custody_hold), ledger (quest-ready, xp=points CHECK, summation-only balances),
region/lawful-basis on all six streams, field encryption (per-subject keys, Key Vault seam), IPaymentService
stub (B12), behavioral stream, one minimal `Svac.PublicApi` host, `contracts/openapi.v0.json` +
`message-keys.json` (B17), non-vacuous arch suite (every rule red-fixture-proven). Kills B1 B2 B3 B12 B17.
**Green (verified by me, twice, never on agent word):** dotnet build 0/0 · Architecture 108 pass / 3 skip
(skips == the 3 legit defers) · DomainCore 25/0 · node 126 (119 pass / 7 S0-defer skips) · ef-gate
idempotent (2 migrations apply + no-op re-apply) · compose fresh-boot ×2 clean (zero restarts, one
exact-text-excluded benign EF migration-history probe) · live substrate E2E ×2 green incl. behavioral
read-back · OpenAPI + purge-registry drift byte-identical. Security: 5 CRITICAL + 10 HIGH the build
agents shipped, all remediated to now-green tests (crypto-shred blast radius, replay cross-subject data
loss, pseudonymize never re-keying stream_id, ledger balance lost-update, Reverse violating the points
CHECK). 3 defers (Auth-F3 IDOR / Concurrency-F5 quota-tx / SilentRej-L4 timing) — none reachable at S1,
each carried to the slice that first adds its surface (S2, S14, first policy-gated read).
**What worked:** the security phase paid for itself many times over on pure substrate; the contract's
guarded-activation + "mechanism not product" discipline held (zero feature modules, no scope creep);
the design panel's minimal-host scope call was correct and provable.
**What we learned / changed:** (1) RUN THE LIVE E2E FOR REAL, every slice — it caught the §10.4
behavioral-emit gap the whole "done" report hid (handler never Emit'd; the seam was a stub). L30
validated hard. Wiring it needed `[FromServices]` so the DB-free OpenAPI emitter still generates. (2)
Don't defer a REACHABLE cheap fix: Concurrency-F6 (config-seed boot race) was triaged to defer but the
host seeds config at boot and two instances race it — pulled forward, fixed, un-skipped. (3) Verify the
NEGATIVE: a `grep --include=*.cs` that errored under zsh returned "no matches", nearly producing a wrong
"F6 unreachable" call; the fresh-boot logs disproved it. A grep that errored is not evidence of absence.
(4) `dotnet-ef` was installed but off the gate's inherited PATH, and ef-gate.sh misreported the missing
tool as "model drift" — added a preflight that BLOCKS with the real reason + remediation (tool-missing ≠
drift). Same agent-claimed-tool-not-on-machine class as S0's bicep. (5) Agents committed AND pushed to
master directly again — reviewed every commit rather than trusting; branch protection (contract §13,
Julien's action) is what makes this structurally impossible going forward. (6) LOCAL GATE GREEN != CI
GREEN — always confirm CI on the ACTUAL pushed commit before DONE. The pipeline's "done" hid a backend
CI that was red on multiple counts; CI then caught THREE things the local gate could not: (a) the DDL
drift script was generated after only the 1st migration, so it silently omitted the 2nd (ddl-lint was
linting SQL that wasn't what EF applies) — the CI regen-and-diff caught it; (b) backend.yml + emit-ddl
-script.sh installed dotnet-ef 9.* against an EF Core 10 project (regenerates differently than local
v10 → drift gate never converges) — pin the tool to the exact package version; (c) the ef-gate CI job
ran on a fresh runner without building, so dotnet ef hit NETSDK1004 and ef-gate misreported it as model
drift — a per-job runner shares no build output (`needs:` is ordering, not artifacts); the gate's caller
must build first. Meta: run the deterministic suite AFTER every script edit (my ef-gate preflight broke
its own fixture ordering because I ran the script's behavior by hand but not `node --test ef-gate.test
.mjs` post-edit), and a path-filtered workflow won't re-run on an out-of-path fix — dispatch it.

### S2 retro — aiml-router (G0) — DONE (CI green on c2ad189)

**Shipped:** `backend/modules/AimlRouter` — the first `backend/modules/*` occupant (sets the 1A module
template). `IAimlRouter.InvokeAsync` single verb; closed `AimlTaskKind`/`AimlFailure` unions; pure IO-free
`Resolver` (allowlist ∩ registered, region/residency, failover, explicit-pin refusal, fail-closed empty
chain); `anthropic` provider with two transports (`AnthropicApiTransport` API + `AnthropicLocalTransport`
local CLI, `[DevSeamsOnly]`) + `SeedProvider` (test DI only); `IVendorEgressAuthorizer` refuse-all-
special-category; one `aiml.route_decided` audit event per invoke (metadata-only, content NEVER in
payload); `quota.aiml.call.daily.cap` circuit breaker; ProviderSdkArchTest ARMED (provider SDK legal only
inside `Svac.AimlRouter`, red-fixture both directions). Zero DDL, zero OpenAPI paths, zero 13A rows — all
drift-gate-asserted. Kills B4.
**Green (verified by me, twice, never on agent word):** dotnet build 0/0 · AimlRouter 49 pass / 2 skip ·
Architecture 130 pass / 4 skip · DomainCore 25/0 · node lints 99 pass / 5 skip · suite run TWICE
identical (no flakiness) · ef-gate OK (zero schema delta, chain idempotent) · compose fresh-boot clean
(zero restarts, zero error logs) · live substrate E2E green incl. behavioral read-back. Ratification
Correction 1 (opus-4-8 default, no fable downgrade) and the budget-key fix both verified present in the
shipped manifest. Security: 17 fixNow remediated to now-green tests (set-time config bounds didn't exist;
resolver region-blind + un-deserializable residency override; subject-region not inherited on the audit
row; model-door guard flag-collapse regressing S1 Trust-F1; keyless-prod guard fired at first-resolution
not at build; caller-cancellation misrouted as failover = extra paid egress after the caller gave up;
audit append killable by caller token = the "one event per invoke" law silently broken; **subject-bearing
audit rows keyed by invocation-id so EVERY purge verb missed them — raw subject id + content hash survived
forever while the purge receipt reported rowsAffected=0**). 3 defers (S2-A `aiml.invoke` not yet in the
enforced PolicyTable / CONC-S2-4a quota+audit not one tx / CONC-S2-5 torn two-key config read) — none
reachable at S2 (zero consumers), each with a Skip-annotated proof test.
**What worked:** the live E2E (paid, real local CLI) surfaced FINDING 1 — a real shipped bug where the
budget cap resolved a 9A key the manifest never seeded (`aiml.daily_call_ceiling` vs QuotaService's real
`quota.aiml.call.daily.cap`), so every InvokeAsync threw KeyNotFoundException. Fixed + pinned by a fast
deterministic regression test. The security phase again paid for itself: the purge-key finding is a GDPR
Art.17 hole that only an adversarial purge-completeness lens finds.
**What we learned / changed:** (1) A "green" security-review §3 that lists the gate lane is NOT the
HARDENED GATE — the agents deliberately skip compose fresh-boot, the live E2E, and the suite-twice flaky
check (correctly, as the paid/periodic lane) but that means I MUST run them; I did, all green. (2) DI's
`ValidateOnBuild` proves constructor-graph resolvability only — it never invokes a factory lambda or a
throwing constructor body, so a fail-closed guard MUST be modeled as an unresolvable typed dependency to
fire at boot, not inside a factory (TRUST-BREAK-3). (3) The paid live-CLI E2E can't run under an OAuth-only
local login once `CLAUDE_CONFIG_DIR` is isolated (PII-S2-F4 fix) — it needs `ANTHROPIC_API_KEY`; this
session had neither, so the canary is deferred to the nightly/periodic lane (already validated live by the
test-author — that's how FINDING 1 surfaced). (4) **CI billing block:** backend.yml + lints.yml went red
mid-slice — not code, "recent account payments have failed / spending limit"; jobs never started. Local
gate is the only signal until Julien fixes GitHub Billing & plans, then re-run both workflows on 6eb7bf7.
**RESOLVED (c2ad189):** billing cleared; re-ran backend.yml + lints.yml. lints went green on 6eb7bf7 but
backend went RED — NOT billing, a real CI-only test flake: `PiiResidencyLensS2ArgvTests` threw
`IOException: Broken pipe`. Root cause: `AnthropicLocalTransport` writes the user turn to the child's stdin
then closes it (PII-S2-F4), but the test stub read only argv and exited without draining stdin. A short
prompt fits the OS pipe buffer so the write wins the race locally; CI scheduling let the child close its
stdin read-end first. Fixed two ways — transport now guards the stdin write against a broken pipe (the exit
code + stderr remain the oracle, so an early nonzero exit surfaces its real stderr instead of a masking
pipe error) and the stub now drains stdin like a real `claude -p`. Two new regression tests force the race
deterministically with a 256KB prompt (verified RED on the un-guarded transport, GREEN with it; 0 fails in
20 stress runs). Backend CI fully green on c2ad189 (test + compose fresh-boot e2e + arch + EF + 13A gates),
lints green. AimlRouter now 51 pass / 2 skip. **S2 is DONE, no concerns.**
(5) **Local HARDENED GATE != CI for subprocess pipe timing.** A test double that spawns a process and
receives stdin MUST drain stdin, and any transport writing to a child's stdin MUST tolerate a broken pipe —
else it's a CI-vs-local timing flake the local run masks.

### Deferred-findings ledger (carry into the named slice's Phase-0 contract)

| Finding | From | Severity | Carry to |
|---|---|---|---|
| Auth-F3 — 4A chokepoint can't convey the target resource id (IDOR) | S1 | MED | first client-reachable resource-scoped 4A slice |
| Concurrency-F5 — quota Consume + guarded action not one tx | S1 | MED | S14 |
| SilentRej-L4 — excluded-read vs absent-read timing channel | S1 | LOW | first policy-gated consumer read |
| S2-A — `aiml.invoke` not spliced into the enforced PolicyTable (+ S1 catch-all-Map boot-refusal gap) | S2 | LOW | S12 (first router consumer) |
| CONC-S2-4a — quota Consume + audit append are two unrelated txns | S2 | MED | first router consumer under real load (transactional-outbox shape) |
| CONC-S2-5 — torn two-key config read (allowlist + routing policy) | S2 | LOW | when 9A gains an atomic multi-key snapshot read |

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

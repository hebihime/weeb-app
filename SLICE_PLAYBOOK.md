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

### S7 retro — client-skeletons (S7i ios + S7a android) (G0) — Phase 2 build DONE (HARDENED GATE green on be07774)

**Shipped:** the whole client half of the app, both native shells × both brand flavors, against S1's
frozen contract v0 with ZERO backend delta (drift-gate-asserted). Shared foundation (coordinator-built,
locally verified): `design/tokens.v1.json` (DESIGN.md mirror), `tools/token-lint` (manifest↔DESIGN.md
tables + platform layers↔manifest + brand-delta↔brands + forbidden-group laws), `tools/egress-lint`
(zero third-party egress; brand-domain/loopback allowlist + tracker denylist; Package.swift/*.gradle.kts
are manifests not runtime source), `brand-gate` xcconfig+properties flavor parsers + token-layer leak
allowlist, `tools/i18n-lint` ARMED (.xcstrings + strings.xml parsers, key-parity ×4 + message-key
coverage ×4, dotted→underscored Android names), `maestro/` 14A ×4 brand-smoke harness (accessibility-ID
contract both shells implement identically), graduated ios.yml/android.yml four jobs + lints token/egress.
iOS (`ios/`): Swift6/SwiftUI, XcodeGen, 6 SPM packages (DesignKit/Strings/ApiKit/AppShell/Signup +
DependencyDirectionTests), swift-openapi-generator, custom always-visible tab bar, StateView + 23→18-state
catalog (named Phase-1 checkpoint), UnavailableSignupGateway (no success case at all), 18+/under-13
validation, PrivacyInfo, ATS, debug-gated locale override. Android (`android/`): Kotlin2.0/Compose,
Gradle flavors loading brand.properties, 5 modules, OpenAPI-generator kotlin client, DebugSurface runtime
hook, Play Data Safety, Roborazzi. Kills the client half of B17; B18 stays dead by construction.

**Green (verified by me, on the pushed commit be07774, never on agent word):** CI all three workflows
green — **ios** (xcodebuild test weeb+friki, codegen, string-catalog, **Maestro brand-smoke weeb+friki**),
**android** (gradlew test weeb+friki, codegen, string-catalog, **Maestro brand-smoke weeb+friki**),
**lints** (13 jobs incl. token-lint/egress-lint/i18n-lint armed/brand-gate). **Maestro ×4 all green on
one commit = the ledger acceptance.** Locally by me: node 164 (157 pass / 7 skip / 0 fail, every new rule
red-fixture-proven); Android clean cache-free `testWeeb/FrikiDebugUnitTest` = 140 executions / 0 failures,
both release variants compile; iOS `xcodebuild build` Weeb+Friki = SUCCEEDED. Zero backend/contract/DDL
drift (byte-identical). Deferred-findings unchanged (none originate in S7, client-only).

**What worked:** the shared-wiring-first ordering (lock every node-gate contract + Maestro ID contract,
verify locally, THEN fan out the two native lanes against it) — the deterministic layer was green on CI
first try and never regressed. Fan-out of S7i/S7a as parallel agents against the locked contracts. Adding
a real local Android toolchain (JDK17 + cmdline SDK) mid-slice converted 20-min blind CI cycles into
~2-min local iterations — decisive for root-causing the compileSdk wall. The iOS agent's XCUITest proxy
(Maestro CLI won't install in-sandbox) caught the StateView id-override regression RED-first.

**What we learned / changed (fold UP into BUILD.md §8 for the next native slice):**
(1) **The `[Dd]ebug/` / `[Rr]elease/` .gitignore trap.** An unanchored .NET build-output pattern silently
matched `android/app/src/debug/` — the ENTIRE Android debug source set went uncommitted and absent on
every CI run (locally present + untracked, so local built fine, CI could not resolve `app.client.debug`).
Root .gitignore build-output patterns MUST be anchored (bin/ + obj/ already cover .NET). Add a first-CI
`git ls-files <unit>/src` sanity check for any new native unit. THE lesson of the slice.
(2) **Local ≠ CI is far wider for native than for .NET/node.** Gradle build-cache + Kotlin daemon
masked a systemic failure across `clean`/`--no-build-cache`; only clearing `~/.gradle/caches/build-cache-1`
+ `--stop` reproduced CI. A byte-clean `android {}` block that "worked" locally failed on CI; a clean
rewrite fixed it (exact original trigger never isolated — recorded honestly, not papered over). Always
reproduce native CI failures against a truly cold local build before trusting a fix.
(3) **main must never import a build-type source set.** MainActivity importing `app.client.debug.*`
(src/debug only) is fragile: decouple via a main-owned registry (`DebugSurface`) wired by a debug-only
ContentProvider. Same class as iOS: a container `.accessibilityIdentifier` PROPAGATES DOWN and OVERRIDES
children — the crews CTA carried the container's id; put ids on leaves.
(4) **Maestro/UI-test harness realities.** iOS: `.safeAreaInset` chrome has cold-launch a11y-exposure
timing quirks — keep the always-present brand chrome a root sibling; a secondary CTA under a hero needs
`scrollUntilVisible`, not `assertVisible`; Compose testTags are invisible to Maestro without
`testTagsAsResourceId = true` on the root. Cold-launch + clearState reinstall races the first frame —
use `extendedWaitUntil` + a `waitForAnimationToEnd` settle, never a bare post-launch `assertVisible`
(clause 6). The emulator-runner action's script has an unreliable CWD/$GITHUB_WORKSPACE and mangles `\`
line-continuations — resolve paths in a prior step, pass via `$GITHUB_ENV`, keep each command on one line.
(5) **CI simulator/SDK specifics:** don't hardcode a simulator name (resolve the first available iPhone
at runtime); `swift-openapi-generator` needs `-skipPackagePluginValidation`; never pipe `xcodebuild`
through an uninstalled formatter (pipe-masking); `android-actions/setup-android@v3` installs NO platform
(install `platforms;android-34` explicitly); set `gradle setup-gradle cache-read-only` across a parallel
matrix to avoid cache-save contention.
(6) **Roborazzi baselines** ship `record=true` with locally-recorded images gitignored — REQUIRED
follow-up: after first green CI, commit the CI-recorded PNG baselines and flip `record=false`.

**Phase 3 (security review) NOT yet run** — the standing client lenses (auth/IDOR CONFIRM zero
client-side entitlement logic; PII CONFIRM zero device identifiers + egress + truthful manifests;
minor-protection CONFIRM the 18+ floor + dignity states) are largely gate-enforced already (egress-lint,
zero-persistence tests, privacy-manifest tests, 18+ validation tests, no-hand-rolled-request-model,
404-uniformity) and green on CI, but the formal SECURITY_REVIEW_S7.md is the next phase, not part of this
build phase. S7 build phase stops here for Julien (stop-after-slice).

### S3 retro — identity-accounts (G0) — DONE (HARDENED GATE green; CI green on the wave/s3-identity PR)

**Shipped:** the whole consumer identity slice, the template every later PII module copies. Phase-2a
substrate mutation (ONE combined surgery for S3+S5+S6: async 3-axis 4A engine + `IPolicyTableSource`
union + target-binding boot refusal + `IBearerAuthenticator` seam + `IEmailSender`/`IExportContributor`/
`IConsentLedgerWriter` Contracts seams + `AgeMath`/`HandleRules`/`HatFor` Deterministic + `FieldEncryptionPurpose.AnimeAnswers`).
Then the module: 13-table `identity` schema (region+lawful_basis on every PII row), passwordless
email-code auth (one keyed-HMAC challenge machine), opaque revocable sessions + refresh rotation with
family reuse-detection, `/v1/me/*` account management, statutory data EXPORT (registry-driven, the mirror
of 13A, with the build-failing export⋈purge cross-gate), two-phase rights-preserving DELETION + the purge
orchestrator (crypto-shred, tombstone, retired-handle, `events_heatmap_provenance` first-caller). Kills:
Auth-F3 + SilentRej-L4 RETIRED (both S1 lens tests un-skipped and green). ~20 endpoints, all 4A-gated.

**Green (verified by me, on the pushed commit, never on agent word):** build 0/0; DomainCore 51/0 +
AimlRouter 51/2 byte-identical (the substrate mutation + CONC-3's `PostgresEventStore` advisory-lock
preserved S1/S2 exactly); Architecture 172 pass / 3 skip (Auth-F3/SilentRej-L4 retired; +cross-gate,
+worker-race, +crypto-shred-order, +grace-takeover, +append-race, +config-bounds tests; 3 skips =
S12/S14/S5 defers); Identity 87 pass / 5 skip (5 = Skip-annotated deferred-finding proofs); ef-gate
idempotent (2 identity migrations); every drift/contract/i18n/ddl gate clean; compose fresh-boot clean;
`identity.e2e.mjs` green live TWICE — the full `signup→verified→delete` ledger acceptance incl. real
Mailpit, live IDOR drills, export download, deletion grace-law + cancel + sweep + purge-readback; post-E2E
log sweep zero exceptions. Security: 1 CRITICAL + 7 HIGH + 6 MEDIUM remediated each via a now-green test.

**What worked:** (1) The ONE combined Phase-2a surgery (designed against all three LOCKED contracts,
reconciled to no-fork) meant the shared substrate was mutated exactly once; S5/S6 rebase onto it. (2)
Front-loaded live E2E (L30) caught real shipped bugs the unit gate hid every pass (the scoped-authenticator
boot crash, the SMTP container host, the missing config seed, the under-13 flush). (3) The adversarial
security phase paid for itself many times over — the CRITICAL cancel-vs-worker data-destruction race, the
mail-timing enumeration oracle (Mailpit masks it in dev/CI/E2E — only a prod relay opens it), the
crypto-shred-runs-first ordering, the append seq-race suppressing the theft alarm, and the grace-window
takeover destroying the cancel right were ALL adversarial-only finds that would have shipped.

**What we learned / changed (folded UP for the next slice):**
(1) **A large single module exceeds one agent.** The non-statutory half BLOCKED an agent at the foundation
(~400k tokens); the right shape is sequential sub-passes (foundation → auth → /v1/me → export → deletion),
each ending build-green + committed as a checkpoint, a FRESH agent per pass (full runway beats resuming a
near-exhausted one). Single module = sequential chain (never parallel agents on one module) held.
(2) **A transient API rate-limit can kill an agent mid-pass** with compiling-but-incomplete on-disk state
(2 failing tests + a missing E2E leg). Assess the disk (build + tests) before continuing; a fresh
"finisher" scoped to the precise remaining delta is robust. Guard + honest-BLOCKED reporting made this
recoverable, not a silent hole.
(3) **CI ef-gate: build the SOLUTION, not one context project** — the S1 NETSDK1004 scar RECURS for every
new module DbContext (`needs:` is ordering, not artifacts). `backend.yml` ef-gate now `dotnet build
backend/Svac.sln`. Any CI job running `dotnet ef`/per-context emit-scripts inherits this. (Fold to BUILD.md §8.)
(4) **A scoped Hosting-seam dependency MUST be resolved per-request (`RequestServices`), never
constructor-injected into `UseMiddleware<T>`** — the singleton middleware can't resolve a scoped service
under `ValidateScopes` (Development/compose), a clean boot-crash. Unit tests + `ValidateOnBuild` don't
catch it; **run compose fresh-boot on a foundation before building on it** (the foundation commit shipped
this latent crash; the next pass's E2E caught it). (Fold to BUILD.md §9.)
(5) **`TimingFloor` is a floor, not a ceiling; in-band I/O (mail) defeats it** → an existence timing
oracle that Mailpit hides in dev/CI/E2E. The fix pattern (inherited by S4): outbound side-effects go OFF
the request path (`Channel` + `BackgroundService` outbox); the request enqueues and returns; work-equalize
the backed-vs-unbacked branches so the floored path is timing-flat.
(6) **Heatmap retention (PII-4) is a FOUNDER ESCALATION carried to S9/S14** (see ledger): the S1-ratified
§1c "retain full-history on account_deletion" likely conflicts with Art.17; not exploitable at S3 (zero
writers); must be ruled on before the first heatmap write. Do NOT unilaterally overturn a ratified founder ruling.

### S5 retro — admin-foundation (G0) — DONE (HARDENED GATE green; on `wave/s5-admin-desk`)

**Shipped:** the staff/admin trust boundary as a Blazor Server host (`backend/admin-host`, LANE F) over S1's
domain core — hand-built (no framework), static SSR + enhanced form posts, QuickGrid, token CSS. Two-layer
auth (Entra authenticates via `Microsoft.Identity.Web` / Svac authorizes; DevSeams transport is the dev
backend of the SAME pipeline; MFA enforced in OUR claims check; JIT refused; roles from OUR grants table;
`ProdStaffAuthGuard` fail-closed). The `AdminActionExecutor` §1c chokepoint (7-step, ONE Postgres tx
spanning AdminDbContext + CoreDbContext via a shared connection). Five surfaces: Dashboard tiles
(`IMetricsTileSource`), Config Registry editor (the LEDGER HEADLINE — full 41-key v0 batch seeded, founder
interstitial, ops inline reason, set display-only, bounds via ConfigBounds only), User Search
(`IUserSearchSource`+Empty, audited+quota'd even on empty), Audit Trail (`IAuditReader`), Staff & Roles
(provision/deactivate/reactivate/grant/revoke, all through the executor). Two `admin` tables, `admin.*`
policy rows via `AdminPolicyTableSource`, the two `events_audit` read indexes, staff-pseudonymize purge.
Domain-core touched additively only (config.set {hat,roles_held} enrichment + the indexes), byte-identical
proof. Kills B6 (partial: the desk chassis every later desk registers into).

**Green (verified by me, on the pushed commits, never on agent word):** build Debug+Release 0/0; suite
**473 pass / 0 fail / 19 skip** (DomainCore 51/0, AimlRouter 51/2, Identity 87/5 all byte-identical;
AdminHost 100/5; Architecture 187/5 with new chokepoint + no-DELETE-lifecycle + DataProtection-key-protection
arch rules); ef-gate idempotent ×3 contexts; live `admin-host.e2e.mjs` **20/20 GREEN on two independent
fresh boots**, exit 0, incl. the ledger outcome observed live; 0 restarts; clean log sweep. Security: 1
CRITICAL (last-SuperAdmin lockout) + 3 HIGH + 1 LOW fixed, then 3 more pulled forward in round 2 (antiforgery,
empty-search audit/quota, HandleConfirm scope recheck); 6 deferred with Skip proofs.

**What worked:** (1) The Phase-2a substrate landing with S3 meant S5 only owned 3 small domain-core gaps —
verified by an inventory pass against the real code, not the retro prose, before scoping. (2) Splitting
Phase 2 into a tiny domain-core sub-pass (I did it myself for full understanding) + a large host build
(fresh-agent sequential passes) held the S3 "a large module exceeds one agent" lesson. (3) The adversarial
security phase paid for itself: the CRITICAL founder self-lockout, the ungated `GET /config` registry leak,
and the plaintext DataProtection key ring were all adversarial-only finds.

**What we learned / changed (fold UP):**
(1) **Security remediation MUST re-run the LIVE E2E, not just the deterministic suite.** The remediate agent
ran `dotnet test` (green) but not `admin-host.e2e.mjs`, so the S5-01 config-read gate silently broke the
revoke stage (a role-less actor can no longer READ /config to reach the edit form) — a deterministic-green,
live-red regression I caught only by running the E2E myself. L30 corollary: EVERY phase that changes request
handling re-runs the live E2E before "green." (Fold to BUILD.md §8.)
(2) **A Blazor host needs two seams baked in day one, or it ships latent prod breakage:** DataProtection
keys persisted+encrypted (a scoped default breaks cookies/antiforgery across restart+instances; a plaintext
ring is a cookie-forgery hole), and a scope-isolated DbContext per render path (Blazor static SSR shares one
scoped DbContext across concurrent component reads → EF "second operation" races; `AddDbContextFactory` +
per-read scope is the fix). (Fold to BUILD.md §9 for any future host.)
(3) **The pre-commit gate lane is now ~4 min, not "<2s".** `Svac.Tests.Architecture` grew DB-backed
Testcontainers tests over S3–S5, so the hook (contract: "seconds not minutes, never flaky") now runs a
multi-minute DB suite. It is also a flake surface (a Testcontainers connection timeout under container
contention appeared once). Fix owed: split the DB-backed arch tests out of the pre-commit lane into the
periodic/integration lane, leaving the hook a true fast deterministic gate. (Tracked; see TODOS.)
(4) **Agents self-commit+push each pass, still.** Every 2b pass and the finisher committed+pushed to the
feature branch unprompted (the S1 scar). Harmless on a feature branch + fully re-verified, but branch
protection remains the only structural fix. Reviewed every commit; trusted none.
(5) **Concurrency tests must isolate the mechanism under test.** The S5-03 lockout guard's first concurrency
test used mutual revoke (A↔B), where the loser is denied by ordinary Authorize, not the guard — self-action
isolates the guard. Caught by actually running the test (it failed) before claiming green.

### Deferred-findings ledger (carry into the named slice's Phase-0 contract)

| Finding | From | Severity | Carry to |
|---|---|---|---|
| ~~Auth-F3 — 4A chokepoint can't convey the target resource id~~ | S1 | — | **RETIRED at S3** (target-binding + ownership resolvers + boot refusal) |
| ~~SilentRej-L4 — excluded-read vs absent-read timing channel~~ | S1 | — | **RETIRED at S3** (predicate-folded ownership; both lens tests green) |
| Concurrency-F5 — quota Consume + guarded action not one tx | S1 | MED | S14 |
| S2-A — `aiml.invoke` not spliced into the enforced PolicyTable (+ S1 catch-all-Map boot-refusal gap) | S2 | LOW | S12 (first router consumer) |
| CONC-S2-4a — quota Consume + audit append are two unrelated txns | S2 | MED | first router consumer under real load (transactional-outbox shape) |
| CONC-S2-5 — torn two-key config read (allowlist + routing policy) | S2 | LOW | when 9A gains an atomic multi-key snapshot read |
| **PII-4 (heatmap retention) — account_deletion retains identifiable location provenance the user can't see (Art.15) or erase (Art.17); S1 §1c ruling likely conflicts with GDPR. FOUNDER ruling required** | S3 | HIGH (legal) | **S9/S14** — before the first `events_heatmap_provenance` write |
| CONC-5 — session-cap eviction is check-then-act (transient over-cap, no privilege gain) | S3 | MED | S14 |
| ~~OPS-4 — `ConfigRegistry.SetValue` doesn't enforce `row.Scope`~~ | S3 | MED | **SUBSTANTIVELY RESOLVED at S5** — founder-key-writable-at-ops-authority is closed by the typed policy rows (`core.config.set.founder`={SuperAdmin}) + interstitial routing; residual = the domain-core `SetValue` scope assert (= S5-12 below) |
| OPS-5 — config dual-key divergence (human `identity.*_cap` vs enforced `quota.*.cap`) | S3 | LOW | **NOT addressed at S5** (editor renders both keys; collapse-to-one still owed) → re-carry to the next config-manifest slice |
| AUTH-4 — logout doesn't clear the device push token; sessions minted `device_id=null` | S3 | LOW | S4 (notification delivery) |
| CONC-6/7 — deletion/export recovery-path 500s (`effectiveAt!.Value`, export loser `FirstAsync`) | S3 | LOW | S12 |
| S5-06 / S5-07 — `AdminActionChokepointArchTests` scan blind spots (decoy `.Execute(` receiver; nested-`/Auth/` path allowlist) | S5 | MED | next touch of `AdminActionChokepointArchTests.cs` |
| S5-08 — four-eyes exemption keys on computed hat, not "holds SuperAdmin" (over-refuses a dual-role SuperAdmin, fail-closed) | S5 | MED | next slice revisiting the executor four-eyes step |
| S5-09 — null-`StaffRoles` + null-hat compose to ungate a hypothetical future `RequiresReason` row (no shipped row hits it) | S5 | LOW | first policy row wanting `StaffRoles=null` AND `RequiresReason=true` |
| S5-10 — `IsFourEyesArmed` swallows a missing key → `false` (fail-open only if a seeded key is later dropped) | S5 | LOW | ops-desk slice touching `admin.four_eyes_required` |
| S5-12 (domain-core half) — `ConfigRegistry.SetValue` has no `Scope!="set"` assert (admin-host half fixed at S5 round 2) | S5 | LOW | domain-core slice next touching `SetValue` |
| S5-13 — C# `PendingConsumerSliceLint` lacks the `doneSlices` check the node mirror has; node's `\bDONE\b` match has no negation handling | S5 | LOW | next config-manifest slice |

# CLAUDE.md

## How to work (high-level mindset)

**This section is non-negotiable and must never be removed.**

The marginal cost of completeness is near zero with AI. Do the whole thing. Do it right. Do it with tests. Do it with documentation. Do it so well that Julien is genuinely impressed — not politely satisfied, actually impressed. Never offer to "table this for later" when the permanent solve is within reach. Never leave a dangling thread when tying it off takes five more minutes. Never present a workaround when the real fix exists. The standard isn't "good enough" — it's "holy shit, that's done."

Search before building. Test before shipping. Ship the complete thing. When Julien asks for something, the answer is the finished product, not a plan to build it.

Time is not an excuse. Fatigue is not an excuse. Complexity is not an excuse. Boil the ocean. This is how we think about shipping.

You can outsource the typing. You cannot outsource the understanding. Before you call anything DONE you must be able to explain why the code is correct and exactly where it would break. Tests passing is not understanding. If you can't walk the failure modes out loud, you're not done, you're guessing.

## The two machine spaces — read this before doing anything

Every piece of work you do belongs to one of two spaces. Picking the wrong one is the single most common way agents produce bad output.

**Latent space = LLM work.** Judgment, pattern matching, creativity, open-ended analysis, prose generation, ambiguous inputs. Cost: model tokens. Variability: high. Inspectability: none. Use when the task genuinely requires reasoning.

**Deterministic space = code.** Precision, reproducibility, speed, zero cost per run, testable. Cost: one-time write. Variability: zero. Inspectability: total. Use when the task is same-input-same-output.

**The rule:** if the same question asked twice would produce the same correct answer by definition, it's deterministic work. Do NOT do it in latent space. Write the script. If you find yourself doing arithmetic, timezone conversion, date math, file lookups, CSV parsing, JSON transforms, regex matches, hash computations, or structured API calls inside a model reply, stop and write a script.

**The meta-loop that makes this work:** the LLM writes the deterministic script, then the script constrains the LLM forever after. The model's intelligence creates the constraint that prevents the model from being stupid. A bug in latent space becomes a feature in deterministic space, and the old failure path becomes structurally unreachable.

Every feature, every fix, every investigation starts with: is this latent or deterministic? If the answer is "both," split it. The deterministic piece becomes a script + tests. The latent piece becomes a prompt + eval.

## The context window is the lever

The context window is your only control surface over the model. Treat it as a deliberate input, not a dumping ground. Load the spec, the contract, the relevant files, and concrete examples. Leave the noise out. A vague or bloated context produces vague or bloated output, every time. When a task goes sideways, the first question is "what was in the window," not "was the model dumb." Curate before you prompt.

## Non-negotiable rules

### Design System

- Always read DESIGN.md before making any visual or UI decisions.
- All font choices, colors, spacing, and aesthetic direction are defined there.
- Do not deviate without explicit user approval.
- In QA mode, flag any code that doesn't match DESIGN.md.

### Tests and evals — every time, no exceptions

- Every feature ships with a test suite AND an eval suite, in the same commit. Not the next PR.
- Every bug fix ships with a test AND an eval that would have caught the bug. The regression test is the proof the bug is fixed. The eval is the proof the fix generalizes.
- Every failure gets skillified (the 10 steps). Same day. Same session when possible.
- "I'll add tests later" is banned. If the tests/evals aren't in the diff, the work isn't done.
- Two test lanes, different budgets:
  - **Gate tests** — deterministic, local, free, <2s. Run on every commit via pre-commit hook. Never flaky. Hook lives at `.githooks/pre-commit`; enable once per clone with `git config core.hooksPath .githooks` (see the hook's own header for exactly what it checks and its measured runtime).
  - **Periodic evals** — paid (LLM calls), slower, quality-measuring. Run before ship and nightly. Allowed to be non-deterministic but must have a pass threshold.

### Tie every change to a measurable outcome

- Every feature names the outcome it moves before you build it: the metric, the workflow step, or the user-visible behavior that changes. "It works" is not an outcome.
- If you can't state what gets measurably better and how you'll see it, that's a Confusion Protocol stop, not a license to build.
- Wire in the trace. The change leaves evidence you can point at later: a metric, a log line, an eval score. Compute that produces no measurable, traceable result is theater.

### AI/ML access — one router, Claude by default

- **All AI/ML calls go through one service: the AI/ML Router (`Svac.AimlRouter` module, contract `IAimlRouter`, impl `AimlRouterService`).** No module, host, or client ever calls a provider SDK or inference endpoint directly. The router is the single egress for anything that hits a model — moderation (T9), scoring, translation, any future consumer. Enforced like the 4A policy engine: an architecture test fails the build if a provider client is referenced outside the router.
- **A call selects a provider two ways, nothing else:**
  1. **Automatic** — a deterministic routing policy picks from the configured provider set. The policy is a config-registry entry (9A): same inputs → same provider every time, audited on the event substrate, tunable from the Metrics & Ops desk. The routing decision is deterministic-space work (a policy table), never a latent judgment; the inference behind it is the only latent part.
  2. **Explicit** — the invoking call names a specific provider/model and the router honors it verbatim (no policy override). For tasks deliberately pinned to one provider.
- **Claude is the default provider and the default across the policy.** The provider set is a configured allowlist; non-Claude providers are reachable only when the policy or an explicit call selects them — never the default, never added or proposed unprompted. Always use the best available model by default; no silent downgrades to a cheaper or smaller model for cost.
- **Same router, two backends.** In dev / non-hot-path work, local Claude Code is a valid router backend — no API key, no per-call cost. Production hot paths (e.g. T9 moderation at report scale) call provider APIs through the same router; keys live in Key Vault (2A), never in the repo. Build the router first if it doesn't exist — with its own contract, tests, and evals — before any second consumer needs a model.

### Tech choice — vanilla by default

- Simplest vanilla tech wins. No framework-of-the-month. No clever abstractions for hypothetical reuse.
- Do not recreate what already exists. Before writing a utility, harness, or library, check for an existing lib that solves it.
- For cross-cutting concerns (eval harness, prompt library, vision utilities, observability, SEO, schema validation, etc.) grep GitHub in parallel for top candidates. Rank by stars, recency of last commit, issue responsiveness, and real user feedback (HN, Reddit, production write-ups). Return the best option with reasoning, not a list. Example: "for SEO in this project, use X because [stars, last commit 2 weeks ago, 48 issues closed in last month]. Second choice Y. Rejected Z because [last commit 14 months ago]."
- If two options are equally viable, name the trade-off explicitly and ask Julien. Confusion Protocol applies.

### Search before building

Three layers, in order:

1. **Tried-and-true.** Is there a standard library or pattern that does this? Use it.
2. **New-and-popular.** Is there a newer library with real traction? Evaluate it.
3. **First-principles.** Does the conventional approach actually apply here? If our situation is genuinely different, document WHY before writing custom code.

Most of the time Layer 1 wins. Default to that. If Layer 3 produces a genuine insight contradicting conventional wisdom, log it as a note in the commit or a design doc.

### Check for skills

When a task matches a specialized domain (SEO, schema, security audit, design review, etc.), use the installed Claude Code skill. Don't reinvent what gstack or a community skill already does well. Invoke via the Skill tool, not by re-implementing.

### Skillify repeated success, not just failure

Failures get skillified — that rule already stands. So does repeated success. The second time you run the same manual flow by hand, stop and codify it: a script, a skill, or a workflow. One-off prompts don't compound; reusable flows do. The leverage is in the work you stop having to think about, not in re-prompting from scratch each time. Done it twice by hand? The third time is a command.

## Architecture — bounded units, parallel-friendly

Build everything as independent, self-contained units with sharp boundaries. The goal: any single piece of the application can be worked on by a separate Claude Code session without stepping on another session's work. "Unit" means a service, a bounded context, or a self-contained module — whatever the framework's idiom calls it. These rules are about boundaries, not folder names.

**Layout follows the framework, not a fixed `services/` shape.** Use each stack's idiomatic layout — that is the "vanilla by default" rule applied to structure. Do NOT impose a literal top-level `services/<name>/` tree on a framework that has its own convention; forcing it fights the toolchain for zero gain. Our canonical structure (ratified by the 2026-07-08 eng review, rulings 1A/2A/7A), which already satisfies every rule below:
  - **Clients — three units, all greenfield, each a Weeb/Friki build flavor from first commit (7A):**
    - **iOS** — native Swift at `ios/`.
    - **Android** — native Kotlin at `android/`.
    - **Web funnel** — `web/` (weeb.app / friki.app; the waitlist + anime-test surface, the only under-18-visible surface). No shared app-shell framework across clients; the native MVP is a reference implementation, not a foundation.
  - **Backend — one deployable .NET modular monolith (1A):** a single domain core under `backend/` with strict module boundaries enforced by internal assemblies + architecture tests (`backend/tests/Svac.Tests.Architecture`). Three ASP.NET hosts over that core — public API, admin API, partner API — plus a Blazor admin host; background workers in-solution. Modules live under `backend/modules` (profiles, matching, conversations, integrity, …); shared substrate under `backend/domain-core`. Root namespace `Svac.*`. Services are extracted only when one earns it — do not pre-split into microservices. The domain core stands on four named pillars every module rides and never reinvents:
    - **Event substrate (3A)** — one append/reverse/tombstone/replay store, typed streams (ledger, reputation, consent, behavioral, audit, heatmap provenance); all purge classes run one pipeline over the store registry.
    - **Policy engine (4A)** — the declarative entitlement gate table; `Authorize(actor, action, target)` is the mutation chokepoint; middleware refuses any mutation endpoint with no policy entry.
    - **Config registry (9A)** — one typed table `{key, type, value, scope, gate, bounds, requires_reason}`; every tunable resolves through it; changes are audited events on the event substrate.
    - **Quota service (10A)** — `Consume(actor, quota_key, context)`; caps resolved from the config registry × Premium × reputation × mode; one deny shape → the single limit-reached UI.
  - **Infra (2A):** Azure Container Apps, Azure Database for PostgreSQL/PostGIS, Azure SignalR, Key Vault (field-encryption keys), Blob+CDN; IaC as Bicep under `infra/`, CI/CD under `.github/`.

The boundary rules, which apply regardless of layout:

- **One concern, one unit.** Each unit owns its code, tests, evals, and config. No shared mutable state across units beyond well-defined contracts.
- **Contracts at the boundary.** Units communicate via typed interfaces (HTTP/OpenAPI, message bus, or a shared schema package). The contract lives in a place both sides import — an OpenAPI doc, a module's public contract assembly, a `contracts/`/`schemas/` dir — never by reaching into another unit's internals. The **OpenAPI contract v0**, owned by the backend and consumed read-only by all three clients (each generating its own typed client), is the canonical shared artifact; between backend modules the boundary is the module's public assembly, not another module's internals.
- **Independent test + eval suites.** Each unit has its own gate tests and periodic evals. A change in one unit must not require running another unit's full suite to validate.
- **Deploy units, per ruling 1A.** The backend modular monolith is ONE deploy unit — its modules build and test independently but ship together; do not treat a module as a separately deployable service. Each client (iOS, Android, web funnel) is its own deploy unit, and each brand (Weeb/Friki) is a build flavor of it. So: one backend release, three client release trains × two flavors — never a single lockstep release across backend and clients.
- **Parallel-session safe.** Two Claude sessions working in different units (e.g. `ios/` and a `backend/modules` module, or `backend/domain-core` and `web/`) should never collide. If a change requires coordinated edits across the boundary, that's a contract change — version it, update both sides, and call it out explicitly.
- **Top-level holds glue and units, not loose business logic.** Root holds the unit roots (`ios/`, `android/`, `web/`, `backend/`), IaC (`infra/`), CI (`.github/`), orchestration scripts, shared config, contracts, and docs. No business logic floating at the root.

When in doubt, lean toward more units with sharper boundaries rather than fewer units with fuzzy ones.

**Fan out by default.** The bounded-unit layout exists so work runs in parallel. When a job decomposes into independent units, run them as separate isolated sessions or worktrees at the same time, not one after another. Serial work on parallelizable units is wasted wall-clock. Coordinate at the contract boundary, merge each unit when it's green.

## Completion status protocol

At the end of every task, report one of:

- **DONE** — All steps completed. Evidence provided for every claim. Tests + evals in the diff. Skillify checklist green if a failure was promoted. Ready to merge.
- **DONE_WITH_CONCERNS** — Completed, but with issues Julien should know about. List each concern with severity and a proposed follow-up.
- **BLOCKED** — Cannot proceed. State what's blocking and what was already tried.
- **NEEDS_CONTEXT** — Missing information required to continue. State exactly what's needed.

"Partially done" is not a status. Either the feature ships (DONE) or it doesn't (BLOCKED / NEEDS_CONTEXT). Honesty about incompleteness beats pretending.

## After every task — commit, push, restart

Once a task is done, two things happen, no exceptions:

1. **Commit and push.** Stage the work, write a clear commit message, push to GitHub. Don't wait to be asked. Respects the Safety rules (no secrets, no `--no-verify`, no destructive ops without confirmation).
2. **Report what to restart.** Tell Julien exactly which service / system / program needs to be restarted for the change to take effect, with the full list of commands to run. If nothing needs restarting, say so explicitly.

For restart commands that need `sudo`: never run them yourself. List them for Julien to run, clearly marked as his to execute.

## Background jobs and backfills

Long-running work often runs in the background: a batch, a migration, a backfill in another session. Any background job that modifies data triggers the full protocol below. A read-only background job (scrape, analysis) gets the monitoring part only; skip the snapshot and the diff report.

**Monitor it, don't fire-and-forget.** While the job runs, post a progress update at least every 5 minutes. Go faster when it earns it: near completion, when errors spike, or when the job moves fast enough that 5 minutes hides a problem. Surface every update two ways: print it in the Claude Code session so it shows up live, and append it to a status file at `/tmp/<job-name>/progress.log`, timestamped. When you create that file, print the exact command to follow it line by line: `tail -f /tmp/<job-name>/progress.log`. Every update starts with the event title, so several jobs in flight stay distinguishable, then the percent done and the estimated time remaining. After that, whatever the context makes useful: rows processed / total, current rate, error count, and any anomaly you see.

Progress percent, rate, and ETA are deterministic. Do not eyeball them in latent space. Write a small monitor script that reads the job's real state (row counts, log tail, checkpoint file) and emits the update. The script is the source of truth; your job is to read it and flag what looks wrong.

**Snapshot before you touch anything.** By default, save every row the backfill will modify to `/tmp/` before it runs. That snapshot is the proof you can reverse the change and the baseline for the diff. If the snapshot would exceed 100k rows or 100MB, stop and ask Julien for permission before snapshotting; do not start the job until he answers.

**On completion, produce the report.** Every backfill ends with a written report on what changed:

- A verdict: did the backfill work? State it plainly, with evidence.
- Whether it needs to be better, and if so why and how. No vague "could be improved": name the specific gap and the fix.
- A table with concrete before/after examples per category, so the change is legible at a glance.
- A full before/after CSV written to `/tmp/`. Print the exact path in your final report.

Everything for the job (status log, snapshot, report, CSV) lives under `/tmp/`. Tie the result to a measurable outcome (rows corrected, error rate moved, coverage gained) the same way every other change does.

## Confusion protocol

When you hit high-stakes ambiguity:

- Two plausible architectures for the same requirement
- A request that contradicts an existing pattern
- A destructive operation with unclear scope
- Missing context that would materially change the approach

STOP. Name the ambiguity in one sentence. Present 2-3 options with real trade-offs (not a fake spread). Ask Julien. Do not guess on architectural decisions. Does not apply to routine coding, small features, or obvious changes.

## Safety

- Never commit secrets. If `.env` is touched, verify `.gitignore` before any commit.
- Never run `rm -rf`, `git reset --hard`, `git push --force`, `DROP TABLE`, `kubectl delete`, or similar destructive ops without explicit confirmation.
- Never skip pre-commit hooks with `--no-verify`. If a hook fails, fix the underlying issue.
- Never commit binaries, compiled outputs, or model weights to the repo. Use Git LFS or cloud storage with a pointer.
- Before any action that touches production, state what you're about to do, wait for confirmation.

## How Julien wants to be talked to

- Direct. Short. Concrete. No preamble.
- Specific file names, function names, line numbers. Not "there's an issue in the classifier" — it's `food_vision/classifier.py:47`.
- No em dashes. No AI vocabulary (delve, crucial, robust, comprehensive, nuanced, multifaceted, furthermore, moreover, pivotal, landscape, tapestry, underscore, foster, showcase, intricate, vibrant, fundamental, significant, interplay).
- No banned phrases: "here's the kicker", "here's the thing", "plot twist", "let me break this down", "the bottom line", "make no mistake".
- If something is broken, say so plainly.
- End responses with the next action, not a recap of what was just done.

When Julien asks for something, the answer is the finished product — not a plan. Tests included. Evals included. Docs included.

## gstack

Use the `/browse` skill from gstack for all web browsing. Never use `mcp__claude-in-chrome__*` tools.

Available gstack skills: `/office-hours`, `/plan-ceo-review`, `/plan-eng-review`, `/plan-design-review`, `/design-consultation`, `/design-shotgun`, `/design-html`, `/review`, `/ship`, `/land-and-deploy`, `/canary`, `/benchmark`, `/browse`, `/connect-chrome`, `/qa`, `/qa-only`, `/design-review`, `/setup-browser-cookies`, `/setup-deploy`, `/setup-gbrain`, `/retro`, `/investigate`, `/document-release`, `/document-generate`, `/codex`, `/cso`, `/autoplan`, `/plan-devex-review`, `/devex-review`, `/careful`, `/freeze`, `/guard`, `/unfreeze`, `/gstack-upgrade`, `/learn`.

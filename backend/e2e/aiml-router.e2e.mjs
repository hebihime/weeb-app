#!/usr/bin/env node
// backend/e2e/aiml-router.e2e.mjs — SLICE_S2_CONTRACT.md §10.3.
//
// Live E2E against real Postgres, real IEventStore/IConfigRegistry/IQuotaService, and the real local
// Claude Code CLI transport — no SQL/stub bypass, no mocked provider, no faked audit rows. Exercises
// Svac.AimlRouter.AimlRouterService.InvokeAsync through a "test-host diagnostic canary" (this file's own
// header explains the design; see backend/e2e/aiml-router-diagnostic-host/'s own headers for the host
// itself), never through Svac.PublicApi — S2 ships ZERO HTTP endpoints in the real product (§0), and this
// diagnostic host is how the test drives the module without adding a single line to Svac.PublicApi/
// Program.cs or Endpoints.cs. Everything this file and its diagnostic host touch lives under
// backend/e2e/ (test-author's owned tree, SLICE_PLAYBOOK.md Phase 2) — no production wiring anywhere.
//
// WHY A SEPARATE TEST-OWNED HOST, NOT Svac.PublicApi: S2's own scaffold commit left AddAimlRouter
// entirely uncalled ("Nothing calls this yet: S2 ships zero consumers" —
// backend/modules/AimlRouter/Svac.AimlRouter/DependencyInjection/AimlRouterServiceCollectionExtensions.cs's
// own doc comment) and Svac.PublicApi/Endpoints.cs maps no route that reaches IAimlRouter at all. Adding
// either is shared-wiring/builder territory (production Program.cs/Endpoints.cs), not test-authoring.
// Building the diagnostic host itself IS squarely test-authoring: it is pure test tooling (no Dockerfile,
// never in Svac.sln's production hosts, never a release train) that calls IAimlRouter.InvokeAsync
// in-process exactly the way a real future consumer module (T9/S12, T8/S13, ...) will — the same shape
// every one of those slices needs anyway.
//
// REAL COST WARNING: under DevSeams (always, for this host), AddAimlRouter registers
// AnthropicLocalTransport — the real local `claude` CLI, no API key, but NOT necessarily $0: verified
// empirically while writing this file, one real completion via `claude -p ... --model claude-opus-4-8`
// reported total_cost_usd ~0.15 (billed against whatever account the CLI session already uses). A full
// run of this file makes exactly TWO real completions (~$0.30 total) plus several $0 real-failure/
// real-deny calls (see DRILLS below for the exact accounting). This is why this file, despite living
// under backend/e2e/ per the contract's own file path, behaves like BUILD.md's PERIODIC lane (paid,
// not free, not run on every commit) rather than a <2s gate test — it is guarded behind an explicit
// opt-in (AIML_ROUTER_E2E_RUN=1), never wired into the pre-commit hook, exactly like the module's own
// evals (backend/modules/AimlRouter/evals/, `--filter Category=Eval`).
//
// Usage:
//   AIML_ROUTER_E2E_RUN=1 node backend/e2e/aiml-router.e2e.mjs
//   (unset -> SKIP, not a lie, printed and exit 0 — mirrors backend/e2e/substrate.e2e.mjs's own pattern)
// Requires: `docker compose up -d postgres` from this repo's docker-compose.yml (the diagnostic host
// opens its OWN isolated database, `svac_aiml_e2e_diag`, on that SAME Postgres server — zero crosstalk
// with the `svac` database Svac.PublicApi reads); `dotnet` on PATH; the local `claude` CLI on PATH and
// authenticated (EvalProbeConformanceTests.cs's own prerequisite, verbatim).
//
// ============================================================================================
// REAL FINDINGS THIS FILE'S EMPIRICAL VALIDATION SURFACED WHILE BEING WRITTEN (all reproduced live
// against real Postgres + the real local CLI, not read off the code alone) — each is asserted below,
// never silently routed around:
//
// FINDING 1 (a real, currently-shipped BUG, not a test-authoring gap): Svac.DomainCore.Quota.
// QuotaService.Consume resolves its cap from 9A key `quota.<quotaKey>.cap` (QuotaService.cs:24), and
// AimlRouterQuotaKeys.CallDaily = "aiml.call.daily" (SLICE_S2_CONTRACT.md §5), so the REAL runtime key is
// `quota.aiml.call.daily.cap`. The shipped backend/modules/AimlRouter/config/aiml-router.config.json
// seeds `aiml.daily_call_ceiling` instead — a key QuotaService.Consume NEVER reads. Reproduced live: every
// single InvokeAsync call against the real manifest throws an unhandled
// KeyNotFoundException("9A config key \"quota.aiml.call.daily.cap\" is not registered") straight out of
// AimlRouterService.ConsumeDailyBudget (line ~137), on EVERY call, success or failure path alike, because
// the budget check runs before resolve/dispatch. No existing unit test catches this: AimlRouterServiceTests.
// cs injects FakeQuotaService, which never replicates QuotaService's real `quota.<key>.cap` convention.
// Pinned as a permanent, fast (<2s, no DB), deterministic regression test:
// backend/tests/Svac.Tests.AimlRouter/BudgetCapConfigKeyWiringTests.cs. This E2E's diagnostic host seeds
// the CORRECT key in its OWN isolated fixture (backend/e2e/aiml-router-diagnostic-host/config/
// diagnostic.config.json) so the drills below can still exercise everything downstream of the budget gate
// while that real bug in the SHIPPED manifest awaits a fix.
//
// FINDING 2 (a real CONTRACT-VS-IMPLEMENTATION mismatch, documented not silently resolved): §10.3's own
// text names the drill "Personal payload vs pseudonymous ceiling ⇒ RefusedPrivacyFloor". Reproduced live:
// the actual outcome is NoRouteConfigured, not RefusedPrivacyFloor. Root cause, traced in code:
// IVendorEgressAuthorizer/RefuseAllSpecialCategoryAuthorizer.cs only ever refuses PayloadClass.
// SpecialCategory (§1b: "SpecialCategory ⇒ Refused, always") — Personal never reaches it as a refusal.
// The ceiling check that DOES apply to Personal lives in Resolver.cs's ExceedsCeiling (line 51: `payloadClass
// > ceiling`, skip not tried) — a resolver-level SKIP that empties the chain, which AimlRouterService.cs's
// cause-selection (`req.ExplicitPin is not null ? NotAllowlisted : NoRouteConfigured`) maps to
// NoRouteConfigured on the Automatic path, never to RefusedPrivacyFloor. DRILL 4 below asserts the
// CONTRACT's literal text (RefusedPrivacyFloor) and — since that is what §10.3 actually calls for — reports
// a precise, informative failure with this exact root-cause trace when the real outcome doesn't match,
// rather than silently asserting whatever the code happens to return. Build phase/Julien's call: either
// AimlFailure's mapping for a ceiling-only-caused empty chain should become RefusedPrivacyFloor, or §10.3's
// drill description should be corrected to NoRouteConfigured — not this test-author's call to make
// unilaterally.
//
// FINDING 3 (a real DESIGN OBSERVATION, not obviously a bug): §10.3 asks for a "failover drill (primary
// transport killed, chain serves, both hops audited)" — read as "both hops get their own audit entry".
// Reproduced live: AimlRouterService.AppendDecision is called AT MOST ONCE per InvokeAsync (on the
// terminal outcome only); a failed hop inside the chain-walk loop never gets its own independent
// `aiml.route_decided` row — it is recorded ONLY as the `failover_from` field on the terminal (winning or
// exhausted) event. DRILL 2 below asserts exactly this real shape (ONE event, `failover_from` populated)
// and flags the "both hops audited" phrasing as arguably satisfied only in the sense that the terminal
// event's metadata names the failed hop, never as two independent rows — a documented ambiguity, not a
// hidden pass.
// ============================================================================================

import { spawn, execFile } from "node:child_process";
import { promisify } from "node:util";
import { setTimeout as sleep } from "node:timers/promises";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const execFileAsync = promisify(execFile);
const __dirname = dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = join(__dirname, "..", "..");
const HOST_PROJECT = join(__dirname, "aiml-router-diagnostic-host", "Svac.AimlRouterDiagnosticHost.csproj");

const PORT = process.env.AIML_ROUTER_E2E_PORT ?? "5299";
const BASE_URL = `http://localhost:${PORT}`;
const DB_NAME = process.env.AIML_E2E_DBNAME ?? "svac_aiml_e2e_diag";
const HOST_READY_TIMEOUT_MS = 120_000;
const CLI_CALL_TIMEOUT_MS = 90_000; // real local CLI round-trips observed at ~2-8s; generous margin for a cold model/network hiccup.

async function main() {
  if (process.env.AIML_ROUTER_E2E_RUN !== "1") {
    console.log(
      "aiml-router e2e SKIP: AIML_ROUTER_E2E_RUN not set to \"1\" — this drill makes REAL local Claude " +
        "Code CLI calls (real $, see this file's own header) and is not part of the free gate lane. " +
        "Opt in explicitly: AIML_ROUTER_E2E_RUN=1 node backend/e2e/aiml-router.e2e.mjs. This is a " +
        "documented skip, not a pass."
    );
    return;
  }

  const failures = [];
  const record = (label, err) => {
    console.error(`  [FAIL] ${label}: ${err.message ?? err}`);
    failures.push(`${label}: ${err.message ?? err}`);
  };

  await assertPrerequisitesOrThrow();

  let hostProcess;
  try {
    hostProcess = await bootDiagnosticHostFresh();

    // ---- DRILL 1: happy path, Automatic, no failover (SLICE_S2_CONTRACT.md §10.3 item 1) --------------
    // task=Generate deliberately (not EvalProbe): Generate has NO task_chains override in the diagnostic
    // fixture, so Resolver.ChainFor falls through to default_chain — ONE real hop, no sentinel, isolating
    // "does a plain Automatic Success round-trip and audit for real" from the failover mechanics DRILL 2
    // covers on purpose. This call also IS the fresh-boot clause's proof for this module (BUILD.md §8
    // clause 2 applied here): it runs immediately after bootDiagnosticHostFresh() dropped+recreated the
    // isolated database from scratch, migrated it, and re-seeded it — a real from-zero boot, not a warm
    // reused one.
    let happyReceipt;
    try {
      happyReceipt = await drillHappyPath();
      console.log(`  [ok] DRILL 1 happy path -> ${JSON.stringify(happyReceipt)}`);
    } catch (err) {
      record("DRILL 1 (happy path, Automatic, Success, audit read-back, fresh-boot proof)", err);
    }

    // ---- DRILL 2: failover (SLICE_S2_CONTRACT.md §10.3 item 1, "failover drill") ------------------------
    try {
      await drillFailover();
      console.log("  [ok] DRILL 2 failover (real sentinel-model hop failure -> real second-hop success)");
    } catch (err) {
      record("DRILL 2 (failover: primary hop killed for real, chain serves, terminal event audited)", err);
    }

    // ---- DRILL 3: SpecialCategory refusal (the refuse-all-special-category lock, §1b/§4) ---------------
    try {
      await drillSpecialCategoryRefusal();
      console.log("  [ok] DRILL 3 SpecialCategory -> RefusedPrivacyFloor, unobservable (HTTP 200)");
    } catch (err) {
      record("DRILL 3 (SpecialCategory egress refusal)", err);
    }

    // ---- DRILL 4: Personal vs pseudonymous ceiling (§10.3 item 1 literal text; see FINDING 2) ----------
    try {
      await drillPersonalVsCeiling();
      console.log("  [ok] DRILL 4 Personal-vs-ceiling matches §10.3's literal RefusedPrivacyFloor text");
    } catch (err) {
      record("DRILL 4 (Personal payload vs pseudonymous ceiling — see FINDING 2 in this file's header)", err);
    }

    // ---- DRILL 5: budget denial (§10.3 item 1 "budget drill") -------------------------------------------
    // The diagnostic fixture's daily_call_ceiling analogue (quota.aiml.call.daily.cap) is 3. Drills 1, 2,
    // and 4 each consumed exactly one real Consume() (Drill 3's SpecialCategory refusal happens BEFORE
    // the budget check per AimlRouterService's own ordering and consumes zero) — so this is the 4th
    // budget-consuming call of the run and must be denied. $0: BudgetDenied is returned before any
    // provider is ever reached.
    try {
      await drillBudgetDenied();
      console.log("  [ok] DRILL 5 budget exhausted for real -> BudgetDenied, unobservable (HTTP 200)");
    } catch (err) {
      record("DRILL 5 (budget circuit breaker, real Consume calls against real Postgres)", err);
    }

    // ---- Zero-exception log sweep (BUILD.md §8 clause 4 "post-E2E log sweep = zero exceptions") --------
    try {
      await assertZeroExceptionLogSweep(hostProcess);
      console.log("  [ok] log sweep: zero unhandled-exception lines (excluding the documented benign EF probe)");
    } catch (err) {
      record("post-E2E log sweep", err);
    }
  } finally {
    await teardownDiagnosticHost(hostProcess);
  }

  if (failures.length > 0) {
    console.error(`aiml-router e2e FAILED (${failures.length} of 6 assertions):`);
    for (const f of failures) console.error(`  - ${f}`);
    process.exitCode = 1;
    return;
  }
  console.log("aiml-router e2e OK: 6/6 assertions green against real Postgres + the real local Claude Code CLI.");
}

// ================================================================================================
// Prerequisites
// ================================================================================================

async function assertPrerequisitesOrThrow() {
  const missing = [];
  for (const [bin, args] of [["docker", ["--version"]], ["dotnet", ["--version"]], ["claude", ["--version"]]]) {
    try {
      await execFileAsync(bin, args);
    } catch {
      missing.push(bin);
    }
  }
  if (missing.length > 0) {
    throw new Error(
      `missing prerequisite executable(s) on PATH: ${missing.join(", ")}. docker+dotnet are required to ` +
        `boot the diagnostic host; the local \`claude\` CLI is required for every real drill in this file ` +
        `(EvalProbeConformanceTests.cs's own prerequisite, verbatim: "install it or run this suite on a " +
        "dev box that has it").`
    );
  }

  const { stdout } = await execFileAsync("docker", ["compose", "ps", "postgres", "--format", "{{.Health}}"], { cwd: REPO_ROOT });
  if (stdout.trim() !== "healthy") {
    throw new Error(
      `docker compose's "postgres" service is not healthy (saw "${stdout.trim()}"). Run ` +
        `"docker compose up -d postgres" from the repo root first — this E2E opens its OWN isolated ` +
        `database (${DB_NAME}) on that same server, never the shared "svac" database.`
    );
  }
}

// ================================================================================================
// Diagnostic host lifecycle
// ================================================================================================

async function bootDiagnosticHostFresh() {
  const child = spawn("dotnet", ["run", "--project", HOST_PROJECT, "-c", "Release"], {
    cwd: REPO_ROOT,
    env: { ...process.env, AIML_ROUTER_E2E_FRESH: "1", AIML_E2E_PORT: PORT, AIML_E2E_DBNAME: DB_NAME },
  });

  child.stdoutBuf = "";
  child.stderrBuf = "";
  child.stdout.on("data", (d) => { child.stdoutBuf += d.toString(); });
  child.stderr.on("data", (d) => { child.stderrBuf += d.toString(); });

  const deadline = Date.now() + HOST_READY_TIMEOUT_MS;
  while (Date.now() < deadline) {
    if (child.exitCode !== null) {
      throw new Error(
        `diagnostic host exited early (code ${child.exitCode}) before becoming ready. stderr tail:\n${child.stderrBuf.slice(-2000)}`
      );
    }
    try {
      const res = await fetch(`${BASE_URL}/health`);
      if (res.status === 200) {
        return child;
      }
    } catch {
      // connection refused while the host is still migrating/seeding — expected, keep polling.
    }
    await sleep(1000);
  }
  child.kill("SIGKILL");
  throw new Error(
    `diagnostic host never reported healthy within ${HOST_READY_TIMEOUT_MS}ms. stderr tail:\n${child.stderrBuf.slice(-2000)}`
  );
}

async function teardownDiagnosticHost(child) {
  if (!child) return;
  child.kill("SIGTERM");
  await Promise.race([
    new Promise((resolve) => child.once("exit", resolve)),
    sleep(5000),
  ]);
  if (child.exitCode === null) {
    child.kill("SIGKILL");
  }

  // Best-effort: drop the isolated diagnostic database so the NEXT run's fresh-boot clause has a truly
  // clean slate and no orphaned database accumulates across repeated local runs. Never touches `svac`.
  try {
    await execFileAsync(
      "docker",
      ["compose", "exec", "-T", "postgres", "psql", "-U", "svac", "-d", "postgres", "-c",
        `DROP DATABASE IF EXISTS "${DB_NAME}" WITH (FORCE);`],
      { cwd: REPO_ROOT }
    );
  } catch (err) {
    console.error(`  [note] best-effort diagnostic-database cleanup failed (non-fatal): ${err.message}`);
  }
}

// ================================================================================================
// Drills
// ================================================================================================

async function invoke(body) {
  const res = await fetch(`${BASE_URL}/invoke`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    signal: AbortSignal.timeout(CLI_CALL_TIMEOUT_MS),
    body: JSON.stringify(body),
  });
  // BEHAVIOR, not status codes (never assert only res.status): the diagnostic route always returns 200
  // regardless of Success/Failure — that uniformity IS part of what DRILL 3/4/5 assert (failure
  // unobservability, SLICE_S2_CONTRACT.md §1b). A non-200 here means the diagnostic host itself broke
  // (e.g. an unhandled exception, exactly the class of bug FINDING 1 was), never a router-level outcome.
  if (res.status !== 200) {
    const text = await res.text();
    throw new Error(`POST /invoke -> HTTP ${res.status} (expected 200 always; the router's own Success/Failure lives in the body, never the transport). Body: ${text}`);
  }
  return res.json();
}

async function drillHappyPath() {
  const before = new Date();
  const body = await invoke({ task: "Generate", caller: "System", payloadClass: "NonPersonal", userText: "Reply with exactly: PONG" });

  if (body.outcome !== "Success") {
    throw new Error(`expected outcome "Success" on the plain Automatic happy path, got ${JSON.stringify(body)}.`);
  }
  if (body.provider !== "anthropic" || body.model !== "claude-opus-4-8") {
    throw new Error(`expected provider/model "anthropic"/"claude-opus-4-8" (the v0 default_chain — CORRECTION 1's best-available Claude), got provider=${body.provider} model=${body.model}.`);
  }
  if (body.decisionSource !== "Policy" || body.fallbackDepth !== 0 || body.failoverFrom !== null) {
    throw new Error(`expected a CLEAN single-hop Automatic decision (decisionSource=Policy, fallbackDepth=0, failoverFrom=null — no task_chains override applies to "Generate"), got ${JSON.stringify(body)}.`);
  }
  if (!body.invocationId?.startsWith("aiv_")) {
    throw new Error(`expected an "aiv_" prefixed invocation id on Success, got ${JSON.stringify(body.invocationId)}.`);
  }

  const event = await readBackAuditEvent(body.invocationId);
  assertAuditEventShape(event, {
    outcome: "Success",
    provider: "anthropic",
    model: "claude-opus-4-8",
    decision_source: "Policy",
    policy_version: 1,
    failover_from: null,
  });
  await assertWatermarkAdvancedSince(before);
  assertNoPromptOrCompletionTextLeaked(event, ["PONG", "Reply with exactly"]);

  return { invocationId: body.invocationId, provider: body.provider, model: body.model };
}

async function drillFailover() {
  // task=EvalProbe: the ONLY task_chains override in the diagnostic fixture — hop 1 names a sentinel
  // model string no real Claude deployment serves (verified empirically: the real `claude` CLI returns
  // exit 0 / is_error:true / total_cost_usd:0 for it — a REAL failure, never simulated), hop 2 is the
  // real default model. AnthropicLocalTransport.cs's own IsError check throws on hop 1's shape;
  // AimlRouterService's real chain-walk catches it and serves hop 2 for real.
  const body = await invoke({ task: "EvalProbe", caller: "System", payloadClass: "NonPersonal", userText: "Reply with exactly: PONG" });

  if (body.outcome !== "Success") {
    throw new Error(`expected the SECOND hop to serve a real Success after the sentinel hop's real failure, got ${JSON.stringify(body)}. (If failureCause is "ChainExhausted", the local \`claude\` CLI itself may be missing/unauthenticated on this box.)`);
  }
  if (body.decisionSource !== "Failover" || body.fallbackDepth !== 1) {
    throw new Error(`expected decisionSource="Failover" and fallbackDepth=1 (one real failed hop before this one), got decisionSource=${body.decisionSource} fallbackDepth=${body.fallbackDepth}.`);
  }
  if (body.failoverFrom !== "anthropic:claude-opus-4-8-aiml-e2e-diagnostic-invalid-model") {
    throw new Error(`expected failoverFrom to name the sentinel hop exactly, got ${JSON.stringify(body.failoverFrom)}.`);
  }

  // FINDING 3 (this file's header): only ONE aiml.route_decided row exists per InvokeAsync call — the
  // failed hop is recorded ONLY via this terminal event's failover_from field, never as an independent
  // row of its own. Assert that REAL shape explicitly rather than assuming a second row exists.
  const rows = await queryAuditEventsByInvocationId(body.invocationId);
  if (rows.length !== 1) {
    throw new Error(
      `SLICE_S2_CONTRACT.md §10.3 says "both hops audited"; the real shipped AimlRouterService appends ` +
        `exactly ONE event per InvokeAsync (this file's FINDING 3). Expected exactly 1 row for this ` +
        `invocation, got ${rows.length} — if this is now >1, AppendDecision's call sites changed and ` +
        `this assertion (and FINDING 3's note) should be revisited, not just loosened.`
    );
  }
  assertAuditEventShape(rows[0], {
    outcome: "Success",
    provider: "anthropic",
    model: "claude-opus-4-8",
    decision_source: "Failover",
    failover_from: "anthropic:claude-opus-4-8-aiml-e2e-diagnostic-invalid-model",
  });
}

async function drillSpecialCategoryRefusal() {
  const body = await invoke({ task: "EvalProbe", caller: "System", payloadClass: "SpecialCategory", userText: "ping" });

  if (body.outcome !== "Failure" || body.failureCause !== "RefusedPrivacyFloor") {
    throw new Error(`expected Failure/RefusedPrivacyFloor for SpecialCategory (the refuse-all-special-category lock, §1b/§4 — no code path can override it before S17), got ${JSON.stringify(body)}.`);
  }
  // Failure unobservability structural check (§1b): NO invocationId is returned on Failure at all — the
  // caller genuinely cannot correlate a failed call to its audit row via the response, by design. This
  // IS the unobservability law in action, not a missing feature — assert its ABSENCE explicitly.
  if (body.invocationId !== null) {
    throw new Error(`expected invocationId to be absent on Failure (SLICE_S2_CONTRACT.md §1b failure unobservability), got ${JSON.stringify(body.invocationId)}.`);
  }

  const event = await mostRecentAuditEventWithOutcome("RefusedPrivacyFloor");
  assertAuditEventShape(event, { outcome: "RefusedPrivacyFloor", payload_class: "SpecialCategory", policy_version: 0, provider: null });
}

async function drillPersonalVsCeiling() {
  // See FINDING 2 in this file's header: this asserts SLICE_S2_CONTRACT.md §10.3's LITERAL text
  // ("Personal payload vs pseudonymous ceiling ⇒ RefusedPrivacyFloor"). Reproduced live during this
  // file's own construction: the real outcome is NoRouteConfigured. This assertion is written to the
  // CONTRACT, not to the code, so it stays RED and informative until the build phase resolves which of
  // the two is wrong.
  const body = await invoke({ task: "EvalProbe", caller: "System", payloadClass: "Personal", userText: "ping" });

  if (body.failureCause !== "RefusedPrivacyFloor") {
    throw new Error(
      `SLICE_S2_CONTRACT.md §10.3: "privacy-floor refusal drill (Personal payload vs pseudonymous ` +
        `ceiling ⇒ RefusedPrivacyFloor on the event, standard error out)". Got failureCause=` +
        `${JSON.stringify(body.failureCause)} instead (this file's FINDING 2: RefuseAllSpecialCategory` +
        `Authorizer.cs only refuses SpecialCategory, never Personal; the ceiling skip that DOES apply to ` +
        `Personal lives in Routing/Resolver.cs's ExceedsCeiling and empties the Automatic chain, which ` +
        `AimlRouterService.cs's cause-selection maps to NoRouteConfigured, never RefusedPrivacyFloor). ` +
        `Either AimlFailure's mapping for a ceiling-only-emptied Automatic chain should become ` +
        `RefusedPrivacyFloor, or this contract clause should read NoRouteConfigured — a build-phase/` +
        `Julien call, not a silent fix here.`
    );
  }
}

async function drillBudgetDenied() {
  const body = await invoke({ task: "Generate", caller: "System", payloadClass: "NonPersonal", userText: "ping" });

  if (body.failureCause !== "BudgetDenied") {
    throw new Error(
      `expected the diagnostic fixture's tiny quota.aiml.call.daily.cap=3 to be exhausted by this point ` +
        `(drills 1, 2, and 4 each consume exactly one real IQuotaService.Consume call; drill 3's ` +
        `SpecialCategory refusal happens before the budget check per AimlRouterService's own step ` +
        `ordering and consumes zero), got ${JSON.stringify(body)}.`
    );
  }
  const event = await mostRecentAuditEventWithOutcome("BudgetDenied");
  assertAuditEventShape(event, { outcome: "BudgetDenied", policy_version: 0, provider: null });
}

// ================================================================================================
// Real Postgres read-back (never a bypass: read-only verification of what the real code path actually
// persisted, the exact pattern backend/e2e/substrate.e2e.mjs already established for S1).
// ================================================================================================

async function psqlJson(sql) {
  const wrapped = `SELECT coalesce(json_agg(row_to_json(t)), '[]'::json) FROM (${sql}) t;`;
  const { stdout } = await execFileAsync(
    "docker",
    ["compose", "exec", "-T", "postgres", "psql", "-U", "svac", "-d", DB_NAME, "-t", "-A", "-c", wrapped],
    { cwd: REPO_ROOT }
  );
  return JSON.parse(stdout.trim());
}

async function readBackAuditEvent(invocationId) {
  const rows = await psqlJson(
    `SELECT event_type, seq, global_seq, region, lawful_basis, payload FROM core.events_audit ` +
      `WHERE event_type = 'aiml.route_decided' AND stream_id = '${invocationId}' ORDER BY seq`
  );
  if (rows.length !== 1) {
    throw new Error(`expected exactly one aiml.route_decided event for stream_id="${invocationId}", found ${rows.length}.`);
  }
  return rows[0];
}

async function queryAuditEventsByInvocationId(invocationId) {
  return psqlJson(
    `SELECT event_type, seq, global_seq, region, lawful_basis, payload FROM core.events_audit ` +
      `WHERE event_type = 'aiml.route_decided' AND stream_id = '${invocationId}' ORDER BY seq`
  );
}

/// Failures carry NO invocationId (the unobservability law, asserted structurally in DRILL 3) — so
/// correlating a failure's audit row uses recency instead, the same time-windowed technique
/// substrate.e2e.mjs uses for its behavioral-emit read-back.
async function mostRecentAuditEventWithOutcome(outcome) {
  const rows = await psqlJson(
    `SELECT event_type, seq, global_seq, region, lawful_basis, payload FROM core.events_audit ` +
      `WHERE event_type = 'aiml.route_decided' AND payload->>'outcome' = '${outcome}' ` +
      `ORDER BY global_seq DESC LIMIT 1`
  );
  if (rows.length !== 1) {
    throw new Error(`expected at least one aiml.route_decided event with outcome="${outcome}" in core.events_audit (db=${DB_NAME}), found ${rows.length}.`);
  }
  return rows[0];
}

async function assertWatermarkAdvancedSince(beforeIso) {
  const rows = await psqlJson(
    `SELECT max(global_seq) AS max_seq FROM core.events_audit WHERE recorded_at > '${beforeIso.toISOString()}'::timestamptz`
  );
  const maxSeq = rows[0]?.max_seq;
  if (maxSeq === null || maxSeq === undefined) {
    throw new Error(`expected at least one core.events_audit row recorded after ${beforeIso.toISOString()} (watermark must advance on every real Append — SLICE_S2_CONTRACT.md §10.3), found none.`);
  }
}

function assertAuditEventShape(event, expected) {
  if (!event) {
    throw new Error("assertAuditEventShape called with no event — the caller's own read-back query returned nothing.");
  }
  const payload = typeof event.payload === "string" ? JSON.parse(event.payload) : event.payload;
  for (const [key, want] of Object.entries(expected)) {
    const got = payload[key] ?? null;
    if (got !== want) {
      throw new Error(`aiml.route_decided payload.${key}: expected ${JSON.stringify(want)}, got ${JSON.stringify(got)}. Full payload: ${JSON.stringify(payload)}`);
    }
  }
  // Region/lawful_basis stamping (SLICE_S2_CONTRACT.md §10.3: "region/lawful_basis stamped"). Every
  // drill here uses a Subject=null system-actor call, so per RequestContext.cs's own documented law
  // ("pure-system rows use Region.Unknown / lawful_basis='n/a'") these are the exact expected values —
  // asserting the CONTRACTUAL sentinel, not an implementation detail that could drift silently.
  if (event.region !== "ZZ") {
    throw new Error(`expected region="ZZ" (RegionCode.Unknown, the pure-system sentinel) stamped on this event, got ${JSON.stringify(event.region)}.`);
  }
  if (event.lawful_basis !== "n/a") {
    throw new Error(`expected lawful_basis="n/a" (the pure-system sentinel) stamped on this event, got ${JSON.stringify(event.lawful_basis)}.`);
  }
}

/// Metadata-only proof (SLICE_S2_CONTRACT.md §1b: "prompt and completion text NEVER appear in any event
/// payload"). Checks the RAW persisted payload JSON never contains any of the real prompt/completion
/// strings this drill actually sent/received — a real check against what Postgres actually holds, not a
/// type-shape assumption.
function assertNoPromptOrCompletionTextLeaked(event, forbiddenSubstrings) {
  const raw = typeof event.payload === "string" ? event.payload : JSON.stringify(event.payload);
  for (const needle of forbiddenSubstrings) {
    if (raw.includes(needle)) {
      throw new Error(`aiml.route_decided payload leaked prompt/completion text ("${needle}" found in the persisted JSON) — SLICE_S2_CONTRACT.md §1b requires metadata-only events. Payload: ${raw}`);
    }
  }
}

// ================================================================================================
// Log sweep (BUILD.md §8 clause 4 / compose-smoke.sh's own exact exclusion, reused verbatim: EF Core's
// first-ever-migration probe against a truly fresh database always logs a benign server-side ERROR).
// ================================================================================================

async function assertZeroExceptionLogSweep(hostProcess) {
  const combined = `${hostProcess.stdoutBuf}\n${hostProcess.stderrBuf}`;
  const hits = combined
    .split("\n")
    .filter((line) => /error|fatal|exception|unhandled/i.test(line))
    .filter((line) => !line.includes('relation "__EFMigrationsHistory" does not exist'));
  if (hits.length > 0) {
    throw new Error(`diagnostic host log sweep found ${hits.length} error-shaped line(s):\n${hits.join("\n")}`);
  }
}

const isMain = process.argv[1] && import.meta.url === `file://${process.argv[1]}`;
if (isMain) {
  main();
}

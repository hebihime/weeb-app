// build/scripts/concurrency-lens.test.mjs — ADVERSARIAL LENS: concurrency (S0).
//
// S0 has no runtime domain surface, so the lens's domain targets (R5 supersession, 10A
// Consume, both-confirm) are vacuous by contract (SLICE_S0_CONTRACT.md §1/§3/§5). What S0
// DOES ship is concurrent-by-nature: CI workflows that can run twice at once, and gate
// scripts that two parallel sessions (a boundary CLAUDE.md guarantees) can invoke at once.
// Each test below encodes an invariant the running code violates today; each is EXPECTED
// TO FAIL until the corresponding finding is fixed. Gate-lane discipline: deterministic,
// no network, no docker, <2s.
//
// Findings (severity order):
//   F1a infra.yml: cancel-in-progress:true in a ref-keyed group covers the prod deploy job
//   F1b infra.yml: deploys to the SAME environment from different refs are not serialized
//   F2  release.yml: platform=both overlaps platform=ios/android concurrency groups
//   F3  compose-smoke.sh: no compose-project isolation/lock — concurrent runs destroy each other
//   F4  ef-gate.sh: throwaway-container name keyed on $$ — cross-PID-namespace collision,
//       loser's EXIT trap stops the winner's live container
//   F5  lints.yml secret-scan: multi-commit push (bulk path) scans only the tip commit
//
// Run: node --test build/scripts/concurrency-lens.test.mjs

import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync, mkdtempSync, writeFileSync, rmSync } from "node:fs";
import { execSync } from "node:child_process";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = join(dirname(fileURLToPath(import.meta.url)), "..", "..");
const read = (p) => readFileSync(join(ROOT, p), "utf8");

// ---------------------------------------------------------------------------------------
// F1a — .github/workflows/infra.yml:24-26 + deploy job (93-112).
// Interleaving: dispatch deploy(prod) on master -> `az deployment group create` in flight
// -> anyone pushes a commit touching infra/** to master -> new run enters group
// `infra-refs/heads/master` -> cancel-in-progress:true CANCELS the in-flight prod deploy.
// The ARM deployment keeps executing server-side with nobody polling it: the run shows
// "cancelled", prod changes anyway, and the contract's "green pipeline as evidence" (§11)
// is broken exactly at the highest-stakes job. A deploy job must never sit in a
// cancel-in-progress:true group.
test("F1a: infra deploy job must not be cancellable by an unrelated push (cancel-in-progress over a ref-keyed group)", () => {
  const y = read(".github/workflows/infra.yml");
  const workflowLevelCancel =
    /concurrency:\s*\n\s*group:\s*infra-\$\{\{\s*github\.ref\s*\}\}\s*\n\s*cancel-in-progress:\s*true/.test(y);
  // A job-level concurrency block under `deploy:` would exempt it from the workflow group.
  const deployJobBlock = y.slice(y.indexOf("\n  deploy:"));
  const deployHasOwnConcurrency = /\n\s{4}concurrency:/.test(deployJobBlock);
  assert.ok(
    !workflowLevelCancel || deployHasOwnConcurrency,
    "infra.yml: the deploy job inherits `group: infra-${{ github.ref }}` + `cancel-in-progress: true`; " +
      "a push to master touching infra/** cancels an in-flight prod deploy mid `az deployment group create`. " +
      "Give the deploy job its own concurrency group with cancel-in-progress: false."
  );
});

// ---------------------------------------------------------------------------------------
// F1b — .github/workflows/infra.yml:24-26.
// Interleaving: dispatch deploy(prod) from ref A (group infra-refs/heads/A) and deploy(prod)
// from ref B (group infra-refs/heads/B). Different groups -> both run CONCURRENTLY against
// resource group svac-prod-rg with different template versions. ARM allows concurrent
// deployments to one RG; resources race last-writer-wins. GitHub `environment:` gates
// approval, it does NOT serialize. Serialization must key on the target environment.
test("F1b: infra deploys must serialize per target environment, not per ref", () => {
  const y = read(".github/workflows/infra.yml");
  const deployJobBlock = y.slice(y.indexOf("\n  deploy:"));
  const envKeyedGroup =
    /group:.*inputs\.environment/.test(y) || /\n\s{4}concurrency:[\s\S]*?inputs\.environment/.test(deployJobBlock);
  assert.ok(
    envKeyedGroup,
    "infra.yml: concurrency group is `infra-${{ github.ref }}`; two dispatches from different refs " +
      "targeting the SAME environment deploy concurrently into svac-<env>-rg (last-writer-wins races). " +
      "The deploy job's group must include ${{ inputs.environment }}."
  );
});

// ---------------------------------------------------------------------------------------
// F2 — .github/workflows/release.yml:21-23 vs platform option `both` (line 19).
// Interleaving: dispatch (weeb, both) -> group `release-weeb-both`, runs ios-fastlane and
// android-fastlane. While in flight, dispatch (weeb, ios) -> group `release-weeb-ios`,
// DIFFERENT group, runs immediately -> two ios-fastlane jobs upload the same brand to
// TestFlight concurrently. cancel-in-progress:false was chosen to QUEUE same-brand
// releases; the `both` option defeats it because group identity is the raw input string,
// not the set of platforms it expands to.
test(
  "F2: release concurrency groups must not let platform=both overlap platform=ios/android for one brand",
  { skip: "deferred: SECURITY_REVIEW_S0.md concurrency F2 (MEDIUM) — latent until S7 lands ios/android fastlane lanes; not in the trust/residency/minor protective triad. Proof stays in code, not fixed yet." },
  () => {
  const y = read(".github/workflows/release.yml");
  const hasBothOption = /options:\s*\[\s*ios\s*,\s*android\s*,\s*both\s*\]/.test(y);
  const groupUsesRawPlatform = /group:\s*release-\$\{\{[^}]*brand[^}]*\}\}-\$\{\{[^}]*platform[^}]*\}\}/.test(y);
  const fastlaneJobsHaveOwnGroups =
    /\n\s{2}ios-fastlane:[\s\S]*?\n\s{4}concurrency:/.test(y) &&
    /\n\s{2}android-fastlane:[\s\S]*?\n\s{4}concurrency:/.test(y);
  assert.ok(
    !(hasBothOption && groupUsesRawPlatform) || fastlaneJobsHaveOwnGroups,
    "release.yml: `release-weeb-both` and `release-weeb-ios` are distinct groups but both run " +
      "ios-fastlane -> two concurrent TestFlight uploads for one brand. Key concurrency per " +
      "brand-platform at the JOB level (e.g. release-weeb-ios on the ios-fastlane job), or drop `both`."
  );
});

// ---------------------------------------------------------------------------------------
// F3 — build/scripts/compose-smoke.sh:11,16,25,28 + docker-compose.yml (no project scoping).
// Interleaving (two parallel sessions in the same clone — a boundary CLAUDE.md guarantees
// is safe): run A reaches line 25 `docker compose down -v` while run B is between line 28
// `up -d --wait` and line 31 health assert. Same default compose project (directory name),
// same daemon -> A tears down B's freshly-booted stack AND deletes its volumes; B's health
// assert then fails (or B's own `down -v` wipes A). Wrong result from two correct
// invocations. The script must either namespace its project per run (-p / COMPOSE_PROJECT_NAME)
// or hold a mutex (flock / mkdir lock) for its whole up->down->up window.
test(
  "F3: compose-smoke must isolate its compose project or hold a lock before `down -v`",
  { skip: "deferred: SECURITY_REVIEW_S0.md concurrency F3 (MEDIUM) — not in the trust/residency/minor protective triad. Proof stays in code, not fixed yet." },
  () => {
  const s = read("build/scripts/compose-smoke.sh");
  const isolatesProject = /COMPOSE_PROJECT_NAME|--project-name|compose\s+-p\s/.test(s);
  const holdsLock = /flock|mkdir\s+.*\.lock|lockfile/.test(s);
  assert.ok(
    isolatesProject || holdsLock,
    "compose-smoke.sh runs `docker compose down -v` against the DEFAULT project: a concurrent " +
      "invocation (second Claude session, self-hosted runner sharing the daemon) has its stack and " +
      "volumes destroyed mid-assert. Add per-run COMPOSE_PROJECT_NAME or an flock around the sequence."
  );
});

// ---------------------------------------------------------------------------------------
// F4 — build/scripts/ef-gate.sh:62,70-71.
// CONTAINER_NAME="ef-gate-throwaway-$$": $$ is a namespace-local PID. Two runner containers
// sharing one docker daemon (self-hosted setup) routinely coincide on PIDs. Interleaving:
// run A creates ef-gate-throwaway-743 and starts `dotnet ef database update`; run B (same
// PID in its own namespace) `docker run --name ef-gate-throwaway-743` -> name-conflict error
// -> set -e exits B -> B's EXIT trap `docker stop ef-gate-throwaway-743` STOPS RUN A'S LIVE
// CONTAINER mid-migration -> A fails with a connection reset it will misattribute to the
// entrypoint-restart race its retry loop documents. Uniqueness must come from a
// collision-proof source (uuidgen / mktemp -u / $RANDOM$RANDOM + timestamp), not $$.
test(
  "F4: ef-gate throwaway container name must not derive uniqueness from $$",
  { skip: "deferred: SECURITY_REVIEW_S0.md concurrency F4 (LOW) — harmless on GitHub-hosted runners, only real on a shared self-hosted daemon. Proof stays in code, not fixed yet." },
  () => {
  const s = read("build/scripts/ef-gate.sh");
  const usesPidName = /CONTAINER_NAME="[^"]*\$\$[^"]*"/.test(s);
  assert.ok(
    !usesPidName,
    "ef-gate.sh:62 names the throwaway container ef-gate-throwaway-$$; PID collisions across " +
      "PID namespaces on a shared daemon make the loser's cleanup trap `docker stop` the winner's " +
      "container mid-migration. Use uuidgen/mktemp-derived names."
  );
});

// ---------------------------------------------------------------------------------------
// F5 — .github/workflows/lints.yml (bulk path: a push of N commits). FIXED (SECURITY_REVIEW_S0.md
// concurrency F5): BASE on a push event is now `github.event.before` (the pre-push HEAD), which spans
// the FULL pushed range, not just the tip commit. Demonstration below: a 3-commit push where commit 1
// adds an AWS key and commits 2-3 don't touch it. `beforeSha` is this fixture's local-git equivalent of
// `github.event.before` (the commit HEAD pointed to immediately before the push started). This test
// first asserts the workflow file's source actually uses `github.event.before` (not just `HEAD~1`), then
// proves the resulting range catches the secret — the regression guard for the finding.
test("F5: lints.yml secret-scan must cover the full pushed range, not the tip commit", async (t) => {
  const { scan, loadPatterns } = await import(join(ROOT, "build/scripts/secret-scan.mjs"));
  const lintsSrc = read(".github/workflows/lints.yml");
  assert.match(
    lintsSrc,
    /github\.event\.before/,
    "lints.yml must use github.event.before (the pre-push HEAD) as BASE on push events — HEAD~1 alone " +
      "only covers the tip commit of a multi-commit push, letting an earlier commit's secret through unscanned"
  );

  const repo = mkdtempSync(join(tmpdir(), "concurrency-lens-f5-"));
  t.after(() => rmSync(repo, { recursive: true, force: true }));
  const git = (args) =>
    execSync(`git -c user.email=lens@test -c user.name=lens ${args}`, { cwd: repo, encoding: "utf8" });
  // Built at runtime so this test file never contains a pattern-matching literal (the
  // scanner's own self-match lesson).
  const fakeKey = "AKIA" + "B".repeat(16);
  git("init -q -b master");
  writeFileSync(join(repo, "readme.txt"), "base\n");
  git("add . && git -c user.email=lens@test -c user.name=lens -C . commit -qm c0");
  const beforeSha = git("rev-parse HEAD").trim(); // == github.event.before for the push
  writeFileSync(join(repo, "config.txt"), `aws_key=${fakeKey}\n`);
  git("add -A && git -c user.email=lens@test -c user.name=lens -C . commit -qm c1-adds-secret");
  writeFileSync(join(repo, "other.txt"), "unrelated\n");
  git("add -A && git -c user.email=lens@test -c user.name=lens -C . commit -qm c2");
  writeFileSync(join(repo, "other.txt"), "unrelated edit\n");
  git("add -A && git -c user.email=lens@test -c user.name=lens -C . commit -qm c3-tip");

  const patterns = loadPatterns();
  // Sanity: the secret IS in the pushed range and the scanner CAN see it there.
  const fullRangeDiff = git(`diff ${beforeSha}..HEAD -U0`);
  assert.ok(scan(fullRangeDiff, patterns).length > 0, "fixture sanity: full-range diff must contain the secret");
  // The FIXED workflow's BASE logic on push: github.event.before, which for this local fixture IS
  // beforeSha — the commit HEAD pointed to right before this push started.
  const workflowDiff = git(`diff ${beforeSha}..HEAD -U0`);
  const findings = scan(workflowDiff, patterns);
  assert.ok(
    findings.length > 0,
    "secret-scan over the full pushed range (github.event.before..HEAD) must catch a secret added in " +
      "commit 1 of a 3-commit push, not just the tip commit"
  );
});

// infra/purge-completeness.lens.test.mjs — ADVERSARIAL LENS: purge completeness
// (13A classes vs seeded stores; derivatives inherit lifetime).
//
// Contract under attack: SLICE_S0_CONTRACT.md §6 — "S0 creates zero data stores ... Two explicit
// non-stores recorded in infra/README.md so the S1 purge-registry review has a citation instead of
// an unexamined gap." The lens: every store-or-derivative S0 puts within one settings-flip of
// existing must carry a citation, and every load-bearing lifetime claim must be enforced, not prose.
//
// EVERY TEST IN THIS FILE IS EXPECTED TO FAIL against the S0 tree — each failure is one finding.
// Run: node --test infra/purge-completeness.lens.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync, readdirSync, existsSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const __dirname = dirname(fileURLToPath(import.meta.url));
const repoRoot = join(__dirname, "..");
const readmeSource = readFileSync(join(__dirname, "README.md"), "utf8");

// The recorded 13A section: from its heading to the next "## " heading.
const thirteenAStart = readmeSource.indexOf("## 13A non-stores");
assert.ok(thirteenAStart >= 0, "infra/README.md must contain the §6-mandated 13A non-store record");
const rest = readmeSource.slice(thirteenAStart + 4);
const thirteenASection = rest.slice(0, rest.indexOf("\n## ") >= 0 ? rest.indexOf("\n## ") : rest.length);

// ---------------------------------------------------------------------------------------------
// FINDING 1 — Postgres geo-redundant backup: a cross-region DERIVATIVE copy of every future
// purge-subject row, declared at S0 (postgres-flexible.bicep: geoRedundantBackup 'Enabled',
// backupRetentionDays 7) and deployable via infra.yml's manual-dispatch job the moment OIDC
// secrets exist (a §13 settings action — no code change, no gate). A 13A tombstone/purge run
// over live Postgres never touches a geo-backup; the 7-day horizon is a purge-class boundary
// the record must own. The 13A record cites CI artifacts and compose volumes ONLY.
// ---------------------------------------------------------------------------------------------
test("13A record cites the Postgres geo-redundant backup as a derivative store with its lifetime", () => {
  const pg = readFileSync(join(__dirname, "modules", "postgres-flexible.bicep"), "utf8");
  assert.match(pg, /geoRedundantBackup:\s*'Enabled'/, "precondition: geo-backup is declared at S0");
  assert.ok(
    /backup/i.test(thirteenASection),
    "postgres-flexible.bicep declares geoRedundantBackup 'Enabled' + backupRetentionDays 7, but the " +
      "13A record in infra/README.md never mentions backups — a purged row survives in a cross-region " +
      "derivative copy for 7 days with no citation, and the S1 purge-registry review inherits exactly " +
      "the 'unexamined gap' the record exists to prevent"
  );
});

// ---------------------------------------------------------------------------------------------
// FINDING 2 — CDN edge cache: blob-cdn.bicep declares a Standard_Microsoft CDN endpoint with no
// caching rules and no purge story. Deleting a blob (S11 orphaned-blob sweep; Art. 17 verbs on
// DM media, BUILD.md:128-129) leaves cached copies at every edge until default TTL. A derivative
// store whose lifetime does NOT inherit from its source, recorded nowhere.
// ---------------------------------------------------------------------------------------------
test("13A record cites the CDN edge cache as a derivative of blob with an inherited-lifetime story", () => {
  const cdn = readFileSync(join(__dirname, "modules", "blob-cdn.bicep"), "utf8");
  assert.match(cdn, /Microsoft\.Cdn\/profiles\/endpoints/, "precondition: CDN endpoint is declared at S0");
  assert.ok(
    /cdn|cache/i.test(thirteenASection),
    "blob-cdn.bicep declares a CDN endpoint (default edge TTL, no purge-on-delete wiring), but the 13A " +
      "record never mentions the CDN cache — blob purge verbs will not propagate to edge copies and no " +
      "citation says who owns that lifetime"
  );
});

// ---------------------------------------------------------------------------------------------
// FINDING 3 — Log Analytics: retentionInDays 30 is a 30-day derivative stream of user activity
// (every future container app wires its logs there via container-apps-env). The lifetime exists
// in code; the 13A record does not own it. (Key Vault soft-delete DOES have its citation in the
// README — the standard is met once, proving the omission elsewhere is a gap, not a style choice.)
// ---------------------------------------------------------------------------------------------
test("13A record cites Log Analytics retention as an operator-log store with a stated lifetime", () => {
  const la = readFileSync(join(__dirname, "modules", "log-analytics.bicep"), "utf8");
  assert.match(la, /retentionInDays:\s*30/, "precondition: 30-day log retention is declared at S0");
  assert.ok(
    /log analytics|retentionInDays|operator log/i.test(thirteenASection),
    "log-analytics.bicep hard-codes retentionInDays 30, but the 13A record never cites the workspace " +
      "as a store or its 30-day lifetime"
  );
});

// ---------------------------------------------------------------------------------------------
// FINDING 4 — The CI-artifact non-store's load-bearing claims ("Retention: 30 days", "workflow
// policy forbids uploading DB dumps or user-shaped fixtures") are enforced by NOTHING. Today's
// single upload (backend.yml contract-publish) sets retention-days: 30; any future upload-artifact
// step without retention-days defaults to 90 days and passes actionlint, lints.yml and the
// pre-commit hook; a step uploading pg_dump output passes everything. The repo's own pattern is
// the counterexample: the secret-scan prose is mirrored into lints.yml precisely so prose is
// never the enforcement (lints.yml:18-36). No equivalent exists for the artifact policy.
// ---------------------------------------------------------------------------------------------
test(
  "the recorded CI-artifact policy has a structural enforcement point, not prose",
  { skip: "deferred: SECURITY_REVIEW_S0.md purge-completeness F4 (MEDIUM) — needs a new lint script grepping workflows + wiring into lints.yml, not a cheap fix. Proof stays in code, not fixed yet." },
  () => {
  const wfDir = join(repoRoot, ".github", "workflows");
  const workflows = readdirSync(wfDir).filter((f) => f.endsWith(".yml"));

  // (a) Demonstrate the record's claim is at the mercy of every future edit: assert every existing
  // upload-artifact step carries retention-days (holds today — one step)...
  for (const wf of workflows) {
    const src = readFileSync(join(wfDir, wf), "utf8");
    const uploads = src.split(/uses:\s*actions\/upload-artifact/).slice(1);
    for (const chunk of uploads) {
      assert.match(chunk.slice(0, 400), /retention-days:/, `${wf}: upload-artifact without retention-days (defaults to 90d, contradicting the 13A record's "Retention: 30 days")`);
    }
  }

  // (b) ...then assert a gate exists so (a) STAYS true without this adversarial file. It does not:
  // no lint in lints.yml, no tools/ script, no pre-commit block checks artifact retention or
  // forbidden artifact content-shapes (dumps, volumes, fixtures).
  const lintsSrc = readFileSync(join(wfDir, "lints.yml"), "utf8");
  const hookSrc = readFileSync(join(repoRoot, ".githooks", "pre-commit"), "utf8");
  const toolsHaveIt =
    existsSync(join(repoRoot, "tools", "artifact-policy")) ||
    /artifact.*(retention|polic)/i.test(lintsSrc) ||
    /upload-artifact/.test(hookSrc);
  assert.ok(
    toolsHaveIt,
    "infra/README.md's 13A record claims 'Retention: 30 days' and 'workflow policy forbids uploading " +
      "DB dumps or user-shaped fixtures as CI artifacts', but no lint, hook block, or CI job enforces " +
      "either claim — the policy is prose, violating the record's own purpose and the repo's " +
      "structural-constraint rule (the secret scan got the mirror treatment; the artifact policy did not)"
  );
});

// ---------------------------------------------------------------------------------------------
// FINDING 5 — Derivatives inherit residency too: residency.test.mjs pins DATA_BEARING_MODULES to
// [postgres, blob-cdn, signalr, keyvault] (the contract §9's own enumeration), omitting redis
// (SignalR backplane + cache: user-derived payloads transit and rest there from S13 on) and
// log-analytics (30 days of user-activity-derived logs). A hardcoded 'eastus' in redis.bicep or
// log-analytics.bicep passes the residency suite today. Same root cause as findings 1-3: the
// derivative-store set was enumerated by hand and enumerated short.
// ---------------------------------------------------------------------------------------------
test(
  "residency suite's data-bearing set includes the derivative stores redis and log-analytics",
  { skip: "deferred: SECURITY_REVIEW_S0.md purge-completeness F5 (MEDIUM) — enumeration traces to SLICE_S0_CONTRACT.md:102, so the fix is a versioned contract change, not a test edit. Proof stays in code, not fixed yet." },
  () => {
  const residencySrc = readFileSync(join(__dirname, "residency.test.mjs"), "utf8");
  const m = residencySrc.match(/DATA_BEARING_MODULES\s*=\s*\[([^\]]*)\]/);
  assert.ok(m, "residency.test.mjs must declare DATA_BEARING_MODULES");
  for (const derivative of ["redis.bicep", "log-analytics.bicep"]) {
    assert.ok(
      m[1].includes(derivative),
      `residency.test.mjs DATA_BEARING_MODULES omits ${derivative} — a derivative store of user data ` +
        "whose residency (and therefore purge jurisdiction) is unguarded; a hardcoded foreign region " +
        "literal there passes the entire residency suite"
    );
  }
});

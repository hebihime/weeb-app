#!/usr/bin/env node
// backend/e2e/admin-host.e2e.mjs — SLICE_S5_CONTRACT.md §10.3 (THE HARDENED GATE, live-E2E leg).
//
// Live E2E against the compose stack's Svac.AdminHost (port 8091) — REAL HTTP, a hand-rolled cookie jar
// (no Playwright: §12.1 "vanilla wins twice" — static SSR + enhanced form posts stay pure HTTP), real
// antiforgery-token round-tripping, and a real `docker compose exec postgres psql` read-only probe for
// every audit/behavioral-event assertion (the SAME sanctioned pattern backend/e2e/identity.e2e.mjs and
// backend/e2e/substrate.e2e.mjs already use). The DEV staff-auth transport this script drives IS the dev
// backend of the SAME pipeline prod uses (SLICE_S5_CONTRACT.md §1b: "the pipeline after [the transport]
// is IDENTICAL in dev and prod") — never a SQL/stub bypass of sign-in, config edits, grants, or search;
// every mutation below happens because a real HTTP form POST drove it through AdminActionExecutor.
//
// It is EXPECTED TO FAIL (RED) as of this commit — Svac.AdminHost ships only a sign-in/dashboard STUB
// (SLICE_S5_CONTRACT.md §0 Scaffold). This file is the target the S5 build passes turn GREEN.
//
// ============================================================================================
// THE WIRE CONTRACT (this is the spec every S5 build pass implements to — Pass A/B/C/D, reconciled by
// the Finisher). Every route, cookie name, and data-testid below is a DELIBERATE design choice made by
// the test-author (SLICE_PLAYBOOK L30: the test author defines the intended surface); it is not
// discovered from an existing implementation, because none exists yet.
//
// Cookie: the staff auth cookie is named EXACTLY ".Svac.AdminAuth" (SLICE_S5_CONTRACT.md §1b cookie
// auth). Its presence/absence is asserted by name throughout.
//
// Antiforgery: every SSR form renders a hidden `<input name="__RequestVerificationToken" ...>` (ASP.NET
// Core's own default field name — Blazor's <AntiforgeryToken /> emits exactly this) alongside whatever
// antiforgery cookie ASP.NET Core issues (any name — the jar below is name-agnostic). Every POST in this
// script re-submits the token scraped from the page it just fetched.
//
// Routes:
//   GET  /signin
//     -> 200. One <form data-testid="devseams-fixture-<key>" method="post"
//        action="/internal/devseams/staff-signin"> per DevSeams fixture, each carrying a hidden
//        `fixture=<key>` input + its own antiforgery token. Fixture keys THIS script drives:
//        "superadmin" (external_subject "devseams:superadmin", MFA claim present — MUST equal the
//        compose admin-host service's SVAC_ADMIN_BOOTSTRAP_SUBJECT so a fresh boot bootstraps it as
//        SuperAdmin), "no-mfa" (MFA claim ABSENT), "not-provisioned" (MFA claim present, NO staff row
//        until this script provisions one via the Staff & Roles desk mid-run).
//     -> On a refused sign-in re-render, this SAME page additionally carries
//        data-testid="signin-refused" containing the keyed refusal reason.
//   POST /internal/devseams/staff-signin  (fields: fixture, __RequestVerificationToken)
//     -> Allowed: 302 to /dashboard, Set-Cookie: .Svac.AdminAuth=... .
//     -> Refused (no MFA / unknown subject / inactive): 200, re-renders /signin with
//        data-testid="signin-refused"; NO .Svac.AdminAuth cookie is set.
//   GET  /dashboard  (requires the cookie; else 302 -> /signin)
//     -> 200. Tiles as <section data-testid="tile-<tileId>">...<span
//        data-testid="tile-value-<tileId>">N</span>...</section>. tileId "config-changes" (THE LEDGER
//        HEADLINE tile, §8 seam 2 first registrant) MUST be present with a numeric value.
//     -> Also emits ONE core.events_behavioral row per page view (§8 seam 17), event_type
//        "admin.page_view".
//   GET  /config
//     -> 200. One row per registered 9A key: data-testid="config-row-<key>" wrapping
//        data-testid="config-scope-<key>" (text: founder|ops|set), data-testid="config-value-<key>"
//        (the JSON value, raw text), and — for a key whose manifest declares pending_consumer_slice —
//        data-testid="config-pending-<key>" containing that slice id (e.g. "S18").
//     -> founder/ops-scope keys additionally render data-testid="config-edit-form-<key>" (method post,
//        action "/config/<url-encoded key>/edit", fields `newValue`, `reason`). set-scope keys render
//        NO edit form (display-only, §4).
//   POST /config/<key>/edit  (fields: newValue, reason, __RequestVerificationToken)
//     -> ops-scope, in bounds, authorized: 302 -> /config; the next GET /config shows the new value.
//     -> founder-scope, authorized: 200, renders data-testid="config-confirm-form" (action
//        "/config/<key>/confirm", hidden fields `newValue`, `reason`, `confirmToken`, a FRESH
//        antiforgery token) plus data-testid="config-confirm-old-value" / "-new-value" (the typed
//        old->new diff, DESIGN.md neutral modal). Nothing is committed yet.
//     -> refused (policy deny / four-eyes): 200 (or 302 back to /config), renders
//        data-testid="action-denied"; value unchanged.
//     -> out-of-bounds: 200 (or 302 back to /config), renders data-testid="config-edit-error"
//        containing the registry's OWN ConfigBounds message text ("outside the declared bounds");
//        value unchanged.
//   POST /config/<key>/confirm  (fields: newValue, reason, confirmToken, __RequestVerificationToken)
//     -> 302 -> /config; the next GET /config shows the new value.
//   GET  /user-search
//     -> 200. data-testid="user-search-form" (method get or post, fields `query`, `queryClass`).
//   GET|POST /user-search?query=...&queryClass=...
//     -> 200. data-testid="user-search-results" wrapping EITHER data-testid="user-search-empty" (the
//        honest-dark state — EmptyUserSearchSource, §9) or one-or-more data-testid="user-search-row".
//        Every call (even an empty result) appends admin.user_search.executed (stream_id = staff ref)
//        and consumes core.quota_counters{quota_key:"admin.user_search.daily"}.
//   GET  /staff
//     -> 200. data-testid="staff-provision-form" (fields externalSubject, email, displayName, region,
//        reason). Per existing account: data-testid="staff-row-<staffId>" wrapping
//        data-testid="staff-external-subject-<staffId>" (so this script can resolve a staffId from a
//        known external_subject after provisioning), data-testid="staff-grant-form-<staffId>" (fields
//        role, reason), data-testid="staff-revoke-form-<staffId>-<role>" (field reason, present only for
//        an ACTIVE grant of that role), data-testid="staff-deactivate-form-<staffId>" (field reason).
//   POST /staff/provision | /staff/<id>/grant | /staff/<id>/revoke/<role> | /staff/<id>/deactivate
//     -> 302 -> /staff on success; data-testid="action-denied" on refusal.
//
// Usage: ADMIN_HOST_E2E_TARGET=http://localhost:8091 node backend/e2e/admin-host.e2e.mjs
//        (no ADMIN_HOST_E2E_TARGET set -> SKIP, not a lie)
//        REQUIRES: a local `docker compose` with this repo's postgres + admin-host services up, and the
//        admin-host service's SVAC_ADMIN_BOOTSTRAP_SUBJECT env var set to "devseams:superadmin" against
//        an EMPTY admin.staff_accounts table (a fresh `docker compose down -v && up` gives both) — this
//        is the ONE precondition outside this script's own control; a clear, fast failure message names
//        it explicitly rather than hanging or silently skipping if it is missing.
// ============================================================================================

import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";
import { execFile } from "node:child_process";
import { promisify } from "node:util";
import { ADVERSARIAL_PATHS } from "./edge-guard.mjs";

const execFileAsync = promisify(execFile);
const __dirname = dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = join(__dirname, "..", "..");
const TARGET = process.env.ADMIN_HOST_E2E_TARGET;
const AUTH_COOKIE_NAME = ".Svac.AdminAuth";

// ---------- cookie jar (name-agnostic; hand-rolled, zero new dependency per §12.1) ----------

class CookieJar {
  constructor() {
    this.cookies = new Map(); // name -> value
  }

  absorb(res) {
    const setCookies = typeof res.headers.getSetCookie === "function" ? res.headers.getSetCookie() : [];
    for (const line of setCookies) {
      const [pair] = line.split(";");
      const eq = pair.indexOf("=");
      if (eq === -1) continue;
      const name = pair.slice(0, eq).trim();
      const value = pair.slice(eq + 1).trim();
      if (value === "" || line.toLowerCase().includes("expires=thu, 01 jan 1970")) {
        this.cookies.delete(name);
      } else {
        this.cookies.set(name, value);
      }
    }
  }

  header() {
    return [...this.cookies.entries()].map(([k, v]) => `${k}=${v}`).join("; ");
  }

  has(name) {
    return this.cookies.has(name);
  }

  clear() {
    this.cookies.clear();
  }
}

async function jarFetch(jar, baseUrl, path, { method = "GET", body = null, redirect = "manual" } = {}) {
  const res = await fetch(new URL(path, baseUrl), {
    method,
    redirect,
    headers: {
      ...(jar.header() ? { cookie: jar.header() } : {}),
      ...(body ? { "content-type": "application/x-www-form-urlencoded" } : {}),
    },
    body,
  });
  jar.absorb(res);
  const text = await res.text();
  return { status: res.status, headers: res.headers, text, location: res.headers.get("location") };
}

async function get(jar, baseUrl, path) {
  return jarFetch(jar, baseUrl, path, { method: "GET" });
}

async function postForm(jar, baseUrl, path, fields) {
  const body = new URLSearchParams(fields).toString();
  return jarFetch(jar, baseUrl, path, { method: "POST", body });
}

/// Follows a manual 302 exactly once (GETs the Location header) — used when a stage cares about the
/// PAGE the redirect lands on (e.g. re-rendered /config after an edit), not just the redirect itself.
async function followOnce(jar, baseUrl, res) {
  if (res.status < 300 || res.status >= 400 || !res.location) {
    return res;
  }
  return get(jar, baseUrl, res.location);
}

// ---------- HTML scraping (regex-based, zero DOM-parser dependency per BUILD.md §1) ----------

function extractAntiforgeryToken(html) {
  const m = html.match(/name="__RequestVerificationToken"[^>]*value="([^"]*)"/);
  if (!m) {
    throw new Error('no <input name="__RequestVerificationToken" ...> found on the page — antiforgery scaffolding missing or page did not render as expected');
  }
  return m[1];
}

function extractFormAction(html, testId) {
  const re = new RegExp(`data-testid="${escapeRe(testId)}"[^>]*action="([^"]*)"`);
  const m = html.match(re);
  return m ? m[1] : null;
}

function extractByTestId(html, testId) {
  const re = new RegExp(`data-testid="${escapeRe(testId)}"[^>]*>([^<]*)<`);
  const m = html.match(re);
  return m ? m[1].trim() : null;
}

function hasTestId(html, testId) {
  return html.includes(`data-testid="${testId}"`);
}

function escapeRe(s) {
  return s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

// ---------- psql read-back probe (identity.e2e.mjs / substrate.e2e.mjs's sanctioned pattern) ----------

async function psql(sql) {
  const { stdout } = await execFileAsync(
    "docker",
    ["compose", "exec", "-T", "postgres", "psql", "-U", "svac", "-d", "svac", "-t", "-A", "-c", sql],
    { cwd: REPO_ROOT }
  );
  return stdout.trim();
}

async function pollUntil(fn, { timeoutMs = 8000, pollMs = 250, label = "condition" } = {}) {
  const deadline = Date.now() + timeoutMs;
  let last;
  while (Date.now() < deadline) {
    last = await fn();
    if (last) return last;
    await new Promise((r) => setTimeout(r, pollMs));
  }
  throw new Error(`timed out (${timeoutMs}ms) waiting for: ${label}`);
}

function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}

// ---------- stages ----------

async function stageBootstrapPrecondition(baseUrl) {
  const staffCount = Number.parseInt(await psql("SELECT count(*) FROM admin.staff_accounts;"), 10);
  if (Number.isNaN(staffCount)) {
    throw new Error("could not read admin.staff_accounts row count via psql — is the postgres compose service up and migrated?");
  }
  if (staffCount === 0) {
    throw new Error(
      "admin.staff_accounts is EMPTY and no bootstrap has run. This script REQUIRES the admin-host " +
        'compose service to boot once with SVAC_ADMIN_BOOTSTRAP_SUBJECT="devseams:superadmin" set ' +
        "against an empty table (SLICE_S5_CONTRACT.md §1b) before it starts — restart the admin-host " +
        "service (or `docker compose down -v && up`) with that env var set, then re-run."
    );
  }
  const superadmin = await psql("SELECT count(*) FROM admin.staff_accounts WHERE external_subject = 'devseams:superadmin' AND status = 'active';");
  assert(superadmin.trim() === "1", 'admin.staff_accounts has rows, but none is the active "devseams:superadmin" bootstrap fixture this script requires');
}

async function stageNoMfaRefused(baseUrl) {
  const jar = new CookieJar();
  const signinPage = await get(jar, baseUrl, "/signin");
  assert(signinPage.status === 200, `GET /signin -> HTTP ${signinPage.status}`);
  const action = extractFormAction(signinPage.text, "devseams-fixture-no-mfa");
  assert(action, 'no <form data-testid="devseams-fixture-no-mfa"> on /signin — the DevSeams no-MFA fixture is not wired');
  const token = extractAntiforgeryToken(signinPage.text);

  const res = await postForm(jar, baseUrl, action, { fixture: "no-mfa", __RequestVerificationToken: token });
  const landed = await followOnce(jar, baseUrl, res);

  assert(!jar.has(AUTH_COOKIE_NAME), `the no-MFA fixture must NEVER receive ${AUTH_COOKIE_NAME} — MFA absence is checked LIVE, before any directory lookup`);
  assert(hasTestId(landed.text, "signin-refused"), 'no-MFA sign-in must re-render /signin with data-testid="signin-refused"');

  const refused = await psql("SELECT count(*) FROM core.events_audit WHERE stream_id LIKE 'signin:%no-mfa%' AND event_type = 'admin.signin.refused';");
  assert(Number.parseInt(refused, 10) >= 1, "expected an admin.signin.refused audit event for the no-MFA fixture, found none");
}

async function stageNotProvisionedRefused(baseUrl) {
  const jar = new CookieJar();
  const signinPage = await get(jar, baseUrl, "/signin");
  const action = extractFormAction(signinPage.text, "devseams-fixture-not-provisioned");
  assert(action, 'no <form data-testid="devseams-fixture-not-provisioned"> on /signin');
  const token = extractAntiforgeryToken(signinPage.text);

  const before = Number.parseInt(await psql("SELECT count(*) FROM admin.staff_accounts;"), 10);
  const res = await postForm(jar, baseUrl, action, { fixture: "not-provisioned", __RequestVerificationToken: token });
  const landed = await followOnce(jar, baseUrl, res);
  const after = Number.parseInt(await psql("SELECT count(*) FROM admin.staff_accounts;"), 10);

  assert(!jar.has(AUTH_COOKIE_NAME), "an unprovisioned subject must never receive the auth cookie");
  assert(hasTestId(landed.text, "signin-refused"), "not-provisioned sign-in must render signin-refused");
  assert(after === before, `JIT provisioning must NEVER happen: admin.staff_accounts row count was ${before}, is now ${after}`);
}

async function stageSuperAdminSignsIn(baseUrl) {
  const jar = new CookieJar();
  const signinPage = await get(jar, baseUrl, "/signin");
  const action = extractFormAction(signinPage.text, "devseams-fixture-superadmin");
  assert(action, 'no <form data-testid="devseams-fixture-superadmin"> on /signin');
  const token = extractAntiforgeryToken(signinPage.text);

  const res = await postForm(jar, baseUrl, action, { fixture: "superadmin", __RequestVerificationToken: token });
  assert(res.status === 302, `SuperAdmin sign-in -> HTTP ${res.status}, expected 302`);
  assert(jar.has(AUTH_COOKIE_NAME), `SuperAdmin sign-in did not set the ${AUTH_COOKIE_NAME} cookie`);
  assert(/\/dashboard/.test(res.location ?? ""), `SuperAdmin sign-in redirected to "${res.location}", expected /dashboard`);
  return jar;
}

async function stageDashboardRendersLiveTiles(baseUrl, jar) {
  const before = new Date();
  const dashboard = await get(jar, baseUrl, "/dashboard");
  assert(dashboard.status === 200, `GET /dashboard -> HTTP ${dashboard.status}`);

  const configTile = extractByTestId(dashboard.text, "tile-value-config-changes");
  assert(configTile !== null, 'dashboard did not render <section data-testid="tile-config-changes"> with a tile-value-config-changes -- the ledger headline tile (§8 seam 2) is not registered');
  assert(/^\d+$/.test(configTile), `tile-value-config-changes rendered "${configTile}", expected a real integer count (real-or-honestly-dark, never fabricated)`);

  // §8 seam 17: this same page view emits a behavioral event, read back later in stageBehavioralPageView.
  return { before, initialConfigChangeCount: Number.parseInt(configTile, 10) };
}

async function stageConfigRegistryRendersV0Batch(baseUrl, jar) {
  const page = await get(jar, baseUrl, "/config");
  assert(page.status === 200, `GET /config -> HTTP ${page.status}`);

  const spotCheckValue = extractByTestId(page.text, "config-value-verification.age_gate_challenge_threshold");
  assert(spotCheckValue !== null, "verification.age_gate_challenge_threshold does not render on /config — the v0 batch is not seeded/rendered");
  assert(spotCheckValue.replace(/"/g, "") === "21", `verification.age_gate_challenge_threshold rendered "${spotCheckValue}", expected 21 (SLICE_S5_CONTRACT.md §10.3 spot-check)`);

  const scopeBadge = extractByTestId(page.text, "config-scope-verification.age_gate_challenge_threshold");
  assert(scopeBadge?.toLowerCase() === "founder", `expected a "founder" scope badge, got "${scopeBadge}"`);

  const pendingChip = extractByTestId(page.text, "config-pending-verification.age_gate_challenge_threshold");
  assert(pendingChip?.includes("S18"), `expected a pending-consumer chip naming S18, got "${pendingChip}"`);

  // A sampling of other real v0 keys, to prove this is the FULL batch, not one hand-picked row.
  for (const key of ["match.swipe_cap_free_daily", "premium.price_usd_monthly", "ads.sponsored_card_ratio", "economy.trade_status"]) {
    assert(extractByTestId(page.text, `config-value-${key}`) !== null, `/config did not render "${key}" — the v0 batch appears incomplete`);
  }

  // set-scope rows are display-only -- no edit form.
  assert(extractFormAction(page.text, "config-edit-form-economy.trade_status") === null, "economy.trade_status is set-scope and must render NO edit form");
}

async function stageEditOpsKeyWithReason_AuditedAndRerendered(baseUrl, jar, key, from, to, actorLabel) {
  const page = await get(jar, baseUrl, "/config");
  const action = extractFormAction(page.text, `config-edit-form-${key}`);
  assert(action, `no config-edit-form for "${key}" on /config`);
  const token = extractAntiforgeryToken(page.text);
  const before = new Date();

  const res = await postForm(jar, baseUrl, action, { newValue: String(to), reason: `${actorLabel} ops edit drill`, __RequestVerificationToken: token });
  const landed = await followOnce(jar, baseUrl, res);
  assert(!hasTestId(landed.text, "action-denied") && !hasTestId(landed.text, "config-edit-error"), `ops edit of "${key}" was refused/errored unexpectedly:\n${landed.text.slice(0, 400)}`);

  const rerendered = await get(jar, baseUrl, "/config");
  const newRendered = extractByTestId(rerendered.text, `config-value-${key}`);
  assert(newRendered?.replace(/"/g, "") === String(to), `after editing "${key}", the editor re-render shows "${newRendered}", expected ${to}`);

  const event = await pollUntil(
    async () => {
      const row = await psql(`SELECT payload FROM core.events_audit WHERE stream_id = '${key}' AND event_type = 'config.set' AND recorded_at > '${before.toISOString()}'::timestamptz ORDER BY seq DESC LIMIT 1;`);
      return row.trim().length > 0 ? row : null;
    },
    { label: `config.set audit event for "${key}"` }
  );
  const payload = JSON.parse(event);
  assert(payload.hat, `config.set payload for "${key}" carries no "hat" field -- the Phase-2a staff enrichment is missing:\n${event}`);
  assert(Array.isArray(payload.roles_held) && payload.roles_held.length > 0, `config.set payload for "${key}" carries no roles_held snapshot:\n${event}`);
  assert(payload.reason?.includes("ops edit drill"), `config.set payload for "${key}" does not carry the submitted reason:\n${event}`);

  const actorRow = await psql(`SELECT actor_ref, region, lawful_basis FROM core.events_audit WHERE stream_id = '${key}' AND event_type = 'config.set' ORDER BY seq DESC LIMIT 1;`);
  const [actorRef, region, lawfulBasis] = actorRow.split("|");
  assert(actorRef?.startsWith("Staff:stf"), `config.set actor_ref "${actorRef}" is not a staff (stf_) actor`);
  assert(region && region !== "", "config.set audit row carries no region (L21)");
  assert(lawfulBasis && lawfulBasis !== "", "config.set audit row carries no lawful_basis (L21)");
}

async function stageDashboardTileReflectsTheEdit(baseUrl, jar, before, initialCount) {
  await pollUntil(
    async () => {
      const dashboard = await get(jar, baseUrl, "/dashboard");
      const value = extractByTestId(dashboard.text, "tile-value-config-changes");
      return value !== null && Number.parseInt(value, 10) > initialCount ? value : null;
    },
    { label: "config-changes tile to reflect the just-made edit" }
  );
}

async function stageFounderEditWalksInterstitial(baseUrl, jar) {
  const key = "verification.age_gate_challenge_threshold";
  const page = await get(jar, baseUrl, "/config");
  const editAction = extractFormAction(page.text, `config-edit-form-${key}`);
  assert(editAction, `no founder-scope config-edit-form for "${key}"`);
  let token = extractAntiforgeryToken(page.text);

  const proposeRes = await postForm(jar, baseUrl, editAction, { newValue: "22", reason: "founder interstitial drill", __RequestVerificationToken: token });
  assert(proposeRes.status === 200, `founder-scope edit propose -> HTTP ${proposeRes.status}, expected 200 (the interstitial, not an immediate commit)`);
  assert(hasTestId(proposeRes.text, "config-confirm-form"), "founder-scope propose did not render the confirm-with-reason interstitial");
  assert(hasTestId(proposeRes.text, "config-confirm-old-value"), "interstitial missing the typed old-value side of the diff");
  assert(hasTestId(proposeRes.text, "config-confirm-new-value"), "interstitial missing the typed new-value side of the diff");

  const stillUnchanged = await psql(`SELECT value FROM core.config_entries WHERE key = '${key}';`);
  assert(stillUnchanged.replace(/"/g, "").trim() === "21", "the interstitial's propose step must not have committed anything yet");

  const confirmAction = extractFormAction(proposeRes.text, "config-confirm-form");
  token = extractAntiforgeryToken(proposeRes.text);
  const confirmFieldsMatch = proposeRes.text.match(/name="confirmToken"[^>]*value="([^"]*)"/);
  assert(confirmFieldsMatch, 'interstitial confirm form carries no hidden "confirmToken" field');

  const confirmRes = await postForm(jar, baseUrl, confirmAction, {
    newValue: "22",
    reason: "founder interstitial drill",
    confirmToken: confirmFieldsMatch[1],
    __RequestVerificationToken: token,
  });
  await followOnce(jar, baseUrl, confirmRes);

  const committed = await psql(`SELECT value FROM core.config_entries WHERE key = '${key}';`);
  assert(committed.replace(/"/g, "").trim() === "22", `after confirming, "${key}" should be 22, psql shows "${committed}"`);
}

async function stageProvisionAndGrantEconomyOps(baseUrl, jar) {
  const page = await get(jar, baseUrl, "/staff");
  const action = extractFormAction(page.text, "staff-provision-form");
  assert(action, 'no <form data-testid="staff-provision-form"> on /staff');
  const token = extractAntiforgeryToken(page.text);

  const provisionRes = await postForm(jar, baseUrl, action, {
    externalSubject: "devseams:not-provisioned",
    email: "not-provisioned@devseams.svac.internal",
    displayName: "Not-provisioned fixture",
    region: "US",
    reason: "provisioning the not-provisioned DevSeams fixture for the grant/revoke/deactivate leg",
    __RequestVerificationToken: token,
  });
  await followOnce(jar, baseUrl, provisionRes);

  const staffId = (await psql("SELECT id FROM admin.staff_accounts WHERE external_subject = 'devseams:not-provisioned';")).trim();
  assert(staffId.startsWith("stf"), `provisioning "devseams:not-provisioned" did not produce a stf_ row (got "${staffId}")`);

  const provisionEvent = await psql(`SELECT count(*) FROM core.events_audit WHERE stream_id = '${staffId}' AND event_type = 'admin.action.executed' AND payload::text LIKE '%admin.staff.provision%';`);
  assert(provisionEvent.trim() !== "0", "no admin.action.executed event read back for the provision action");

  const staffPage = await get(jar, baseUrl, "/staff");
  const grantAction = extractFormAction(staffPage.text, `staff-grant-form-${staffId}`);
  assert(grantAction, `no grant form for staff "${staffId}" on /staff`);
  const grantToken = extractAntiforgeryToken(staffPage.text);
  const grantRes = await postForm(jar, baseUrl, grantAction, { role: "economy_ops", reason: "grant drill", __RequestVerificationToken: grantToken });
  await followOnce(jar, baseUrl, grantRes);

  const grantEvent = await psql(`SELECT count(*) FROM core.events_audit WHERE stream_id = '${staffId}' AND event_type = 'admin.action.executed' AND payload::text LIKE '%admin.staff.role_grant%';`);
  assert(grantEvent.trim() !== "0", "no admin.action.executed event read back for the role_grant action");

  const activeGrant = await psql(`SELECT count(*) FROM admin.staff_role_grants WHERE staff_id = '${staffId}' AND role = 'economy_ops' AND revoked_at IS NULL;`);
  assert(activeGrant.trim() === "1", `expected exactly one active economy_ops grant for "${staffId}"`);

  return staffId;
}

async function stageEconomyOpsFixtureSignsIn(baseUrl) {
  const jar = new CookieJar();
  const signinPage = await get(jar, baseUrl, "/signin");
  const action = extractFormAction(signinPage.text, "devseams-fixture-not-provisioned");
  const token = extractAntiforgeryToken(signinPage.text);
  const res = await postForm(jar, baseUrl, action, { fixture: "not-provisioned", __RequestVerificationToken: token });
  assert(res.status === 302, `the just-provisioned+granted fixture failed to sign in -> HTTP ${res.status} (new hat must be live WITHOUT a redeploy)`);
  assert(jar.has(AUTH_COOKIE_NAME), "the just-granted fixture did not receive the auth cookie");
  return jar;
}

async function stageNonQualifyingFixtureFounderAttemptRefused(baseUrl, jar) {
  const key = "verification.age_gate_challenge_threshold";
  const before = await psql(`SELECT value FROM core.config_entries WHERE key = '${key}';`);

  const page = await get(jar, baseUrl, "/config");
  const action = extractFormAction(page.text, `config-edit-form-${key}`);
  const token = extractAntiforgeryToken(page.text);
  const res = await postForm(jar, baseUrl, action, { newValue: "1", reason: "EconomyOps founder-scope overreach drill", __RequestVerificationToken: token });
  const landed = await followOnce(jar, baseUrl, res);

  assert(hasTestId(landed.text, "action-denied"), "an EconomyOps hat attempting a SuperAdmin-only founder-scope edit must render action-denied");

  const after = await psql(`SELECT value FROM core.config_entries WHERE key = '${key}';`);
  assert(after === before, `"${key}" must be byte-identical after the refused attempt: before="${before}" after="${after}"`);

  const refusalEvent = await psql(`SELECT count(*) FROM core.events_audit WHERE event_type = 'admin.action.refused' AND payload::text LIKE '%${key}%';`);
  assert(refusalEvent.trim() !== "0", "no admin.action.refused audit event for the non-qualifying founder-scope attempt");
}

async function stageOutOfBoundsEditRefused(baseUrl, jar) {
  const key = "admin.session_lifetime_hours"; // real host tunable, bounds [1,24] (SLICE_S5_CONTRACT.md §4)
  const page = await get(jar, baseUrl, "/config");
  const action = extractFormAction(page.text, `config-edit-form-${key}`);
  assert(action, `no config-edit-form for "${key}"`);
  const token = extractAntiforgeryToken(page.text);
  const before = await psql(`SELECT value FROM core.config_entries WHERE key = '${key}';`);

  const res = await postForm(jar, baseUrl, action, { newValue: "999", reason: "bounds drill", __RequestVerificationToken: token });
  const landed = await followOnce(jar, baseUrl, res);

  assert(hasTestId(landed.text, "config-edit-error"), "an out-of-bounds edit must render config-edit-error");
  const errorText = extractByTestId(landed.text, "config-edit-error") ?? "";
  assert(errorText.toLowerCase().includes("bounds"), `config-edit-error text "${errorText}" does not mention the registry's own bounds message`);

  const after = await psql(`SELECT value FROM core.config_entries WHERE key = '${key}';`);
  assert(after === before, `"${key}" must be byte-identical after the out-of-bounds attempt`);
}

async function stageUserSearchExecutes(baseUrl, jar) {
  const before = new Date();
  const capBefore = await psql("SELECT coalesce(sum(consumed),0) FROM core.quota_counters WHERE quota_key = 'admin.user_search.daily';");

  const res = await get(jar, baseUrl, "/user-search?query=nobody-should-match-this&queryClass=HandlePrefix");
  assert(res.status === 200, `GET /user-search -> HTTP ${res.status}`);
  assert(hasTestId(res.text, "user-search-empty"), "EmptyUserSearchSource must render the honest-dark empty state, never a fabricated row");
  assert(!hasTestId(res.text, "user-search-row"), "no result rows should exist -- EmptyUserSearchSource is registered, zero fabricated rows");

  const event = await pollUntil(
    async () => {
      const row = await psql(`SELECT payload FROM core.events_audit WHERE event_type = 'admin.user_search.executed' AND recorded_at > '${before.toISOString()}'::timestamptz ORDER BY seq DESC LIMIT 1;`);
      return row.trim().length > 0 ? row : null;
    },
    { label: "admin.user_search.executed audit event" }
  );
  const payload = JSON.parse(event);
  assert(payload.query_class, "admin.user_search.executed payload carries no query_class");
  assert(payload.hat, "admin.user_search.executed payload carries no hat");

  const capAfter = await psql("SELECT coalesce(sum(consumed),0) FROM core.quota_counters WHERE quota_key = 'admin.user_search.daily';");
  assert(Number.parseInt(capAfter, 10) > Number.parseInt(capBefore, 10), "admin.user_search.daily quota was not consumed by this search");
}

async function stageEconomyOpsEditsOpsKey(baseUrl, jar, key, to) {
  const page = await get(jar, baseUrl, "/config");
  const action = extractFormAction(page.text, `config-edit-form-${key}`);
  assert(action, `no config-edit-form for "${key}"`);
  const token = extractAntiforgeryToken(page.text);
  const res = await postForm(jar, baseUrl, action, { newValue: String(to), reason: "EconomyOps ops edit drill", __RequestVerificationToken: token });
  const landed = await followOnce(jar, baseUrl, res);
  assert(!hasTestId(landed.text, "action-denied"), "EconomyOps must be ALLOWED to edit an ops-scope key (core.config.set.ops StaffRoles includes EconomyOps)");

  const value = await psql(`SELECT value FROM core.config_entries WHERE key = '${key}';`);
  assert(value.replace(/"/g, "").trim() === String(to), `"${key}" should now be ${to}, psql shows "${value}"`);
}

async function stageRevokeThenDeniedMidSession(baseUrl, superAdminJar, economyOpsJar, staffId, key) {
  const before = await psql(`SELECT value FROM core.config_entries WHERE key = '${key}';`);

  const staffPage = await get(superAdminJar, baseUrl, "/staff");
  const revokeAction = extractFormAction(staffPage.text, `staff-revoke-form-${staffId}-economy_ops`);
  assert(revokeAction, `no revoke form for staff "${staffId}" role economy_ops`);
  const token = extractAntiforgeryToken(staffPage.text);
  const revokeRes = await postForm(superAdminJar, baseUrl, revokeAction, { reason: "revoke drill", __RequestVerificationToken: token });
  await followOnce(superAdminJar, baseUrl, revokeRes);

  const revokeEvent = await psql(`SELECT count(*) FROM core.events_audit WHERE stream_id = '${staffId}' AND event_type = 'admin.action.executed' AND payload::text LIKE '%admin.staff.role_revoke%';`);
  assert(revokeEvent.trim() !== "0", "no admin.action.executed event read back for the role_revoke action");

  const activeGrant = await psql(`SELECT count(*) FROM admin.staff_role_grants WHERE staff_id = '${staffId}' AND role = 'economy_ops' AND revoked_at IS NULL;`);
  assert(activeGrant.trim() === "0", "the economy_ops grant is still active after revocation");

  // Mid-session, no re-login: the EconomyOps jar's cookie is untouched, but AdminActionExecutor
  // re-reads grants from the DB on every call (SLICE_S5_CONTRACT.md §1b law 2) -- the very next attempt
  // must deny.
  const page = await get(economyOpsJar, baseUrl, "/config");
  const action = extractFormAction(page.text, `config-edit-form-${key}`);
  const editToken = extractAntiforgeryToken(page.text);
  const res = await postForm(economyOpsJar, baseUrl, action, { newValue: "1", reason: "post-revoke attempt", __RequestVerificationToken: editToken });
  const landed = await followOnce(economyOpsJar, baseUrl, res);
  assert(hasTestId(landed.text, "action-denied"), "the revoked fixture's very next action must be denied, mid-session, without re-login");

  const after = await psql(`SELECT value FROM core.config_entries WHERE key = '${key}';`);
  assert(after === before, `"${key}" must be byte-identical after the post-revoke denied attempt`);
}

async function stageLowerRevalidationInterval(baseUrl, superAdminJar) {
  // admin.session_revalidate_seconds bounds are [15,900] (SLICE_S5_CONTRACT.md §4) -- 15 is the fastest
  // this drill can legally make the live-session-death leg below.
  const page = await get(superAdminJar, baseUrl, "/config");
  const action = extractFormAction(page.text, "config-edit-form-admin.session_revalidate_seconds");
  assert(action, "no config-edit-form for admin.session_revalidate_seconds");
  const token = extractAntiforgeryToken(page.text);
  const res = await postForm(superAdminJar, baseUrl, action, { newValue: "15", reason: "shorten the revalidation interval for this drill", __RequestVerificationToken: token });
  await followOnce(superAdminJar, baseUrl, res);

  const value = await psql("SELECT value FROM core.config_entries WHERE key = 'admin.session_revalidate_seconds';");
  assert(value.trim() === "15", `admin.session_revalidate_seconds should now be 15, psql shows "${value}"`);
}

async function stageDeactivateThenLiveSessionDies(baseUrl, superAdminJar, economyOpsJar, staffId) {
  const staffPage = await get(superAdminJar, baseUrl, "/staff");
  const deactivateAction = extractFormAction(staffPage.text, `staff-deactivate-form-${staffId}`);
  assert(deactivateAction, `no deactivate form for staff "${staffId}"`);
  const token = extractAntiforgeryToken(staffPage.text);
  const res = await postForm(superAdminJar, baseUrl, deactivateAction, { reason: "deactivation drill", __RequestVerificationToken: token });
  await followOnce(superAdminJar, baseUrl, res);

  const status = await psql(`SELECT status FROM admin.staff_accounts WHERE id = '${staffId}';`);
  assert(status.trim() === "deactivated", `staff "${staffId}" should be deactivated, psql shows "${status}"`);

  // The live cookie session (economyOpsJar) must die within the revalidation interval WITHOUT this
  // script touching its cookie — a pure passage-of-time + revalidation-on-next-request proof.
  await pollUntil(
    async () => {
      const dashboard = await get(economyOpsJar, baseUrl, "/dashboard");
      return dashboard.status === 302 || hasTestId(dashboard.text, "signin-refused") || dashboard.status === 401 ? true : null;
    },
    { timeoutMs: 25000, pollMs: 1000, label: "the deactivated fixture's live session to die within the revalidation interval" }
  );
}

async function stageTraversalProbes404PreAuth(baseUrl) {
  for (const path of ADVERSARIAL_PATHS) {
    const res = await fetch(new URL(path, baseUrl), { redirect: "manual" });
    assert(res.status === 404, `${path} -> HTTP ${res.status}, expected 404 pre-auth (SLICE_S5_CONTRACT.md §8 seam 13)`);
  }
}

async function stageBehavioralPageViewReadBack(baseUrl, jar, since) {
  const count = await pollUntil(
    async () => {
      const c = Number.parseInt(await psql(`SELECT count(*) FROM core.events_behavioral WHERE event_type = 'admin.page_view' AND recorded_at > '${since.toISOString()}'::timestamptz;`), 10);
      return c >= 1 ? c : null;
    },
    { label: "an admin.page_view behavioral event" }
  );
  assert(count >= 1, "expected at least one admin.page_view behavioral event, found none");
}

// ---------- runner ----------

async function main() {
  if (!TARGET) {
    console.log(
      "admin-host e2e SKIP: ADMIN_HOST_E2E_TARGET not set — no admin host running (guarded until compose is up). This is a documented skip, not a pass."
    );
    return;
  }

  console.log(`admin-host e2e: probing ${TARGET}`);
  const failures = [];
  const run = async (name, fn) => {
    try {
      await fn();
      console.log(`  [ok] ${name}`);
    } catch (err) {
      console.error(`  [FAIL] ${name}: ${err.message}`);
      failures.push(`${name}: ${err.message}`);
    }
  };

  await run("precondition: SuperAdmin bootstrap fixture exists", () => stageBootstrapPrecondition(TARGET));
  await run("no-MFA fixture REFUSED at sign-in (live check, audited)", () => stageNoMfaRefused(TARGET));
  await run("not-provisioned fixture refused, JIT never happens", () => stageNotProvisionedRefused(TARGET));

  let superAdminJar;
  await run("SuperAdmin fixture signs in (real cookie, MFA claim)", async () => {
    superAdminJar = await stageSuperAdminSignsIn(TARGET);
  });

  if (!superAdminJar) {
    failures.push("SuperAdmin sign-in failed — every dependent stage below skipped");
    reportAndExit(failures);
    return;
  }

  let dashboardBaseline = { before: new Date(), initialConfigChangeCount: 0 };
  await run("dashboard renders live S1/S2 tiles (real counts)", async () => {
    dashboardBaseline = await stageDashboardRendersLiveTiles(TARGET, superAdminJar);
  });

  await run("Config Registry renders the FULL v0 batch (spot-check age_gate_challenge_threshold=21, scope badges, pending chips)", () =>
    stageConfigRegistryRendersV0Batch(TARGET, superAdminJar)
  );

  await run(
    "edit an ops key with reason -> config.set read back off events_audit {stf_ actor, hat, roles_held, reason, region, lawful_basis} + editor re-render",
    () => stageEditOpsKeyWithReason_AuditedAndRerendered(TARGET, superAdminJar, "match.reciprocity_signal_budget", 30, 35, "SuperAdmin")
  );

  await run("the config-change dashboard tile reflects the just-made edit (the ledger outcome, OBSERVED)", () =>
    stageDashboardTileReflectsTheEdit(TARGET, superAdminJar, dashboardBaseline.before, dashboardBaseline.initialConfigChangeCount)
  );

  await run("founder-scope edit walks the confirm-with-reason interstitial", () => stageFounderEditWalksInterstitial(TARGET, superAdminJar));

  await run("out-of-bounds edit refused with the registry's own bounds message; value untouched", () => stageOutOfBoundsEditRefused(TARGET, superAdminJar));

  await run("user search executes -> admin.user_search.executed read back + quota consumed + honest-dark zero fabricated rows", () =>
    stageUserSearchExecutes(TARGET, superAdminJar)
  );

  let grantedStaffId;
  await run("SuperAdmin provisions + grants EconomyOps to the not-provisioned fixture (events read back)", async () => {
    grantedStaffId = await stageProvisionAndGrantEconomyOps(TARGET, superAdminJar);
  });

  await run("shorten admin.session_revalidate_seconds to its floor (15s) via the SAME editor, ahead of the deactivation drill", () =>
    stageLowerRevalidationInterval(TARGET, superAdminJar)
  );

  let economyOpsJar;
  if (grantedStaffId) {
    await run("the just-granted fixture signs in -- the new hat is live WITHOUT a redeploy", async () => {
      economyOpsJar = await stageEconomyOpsFixtureSignsIn(TARGET);
    });
  } else {
    failures.push("provision+grant leg failed — grant/revoke/deactivate stages below skipped");
  }

  if (economyOpsJar) {
    await run("that fixture's founder-scope attempt is REFUSED + admin.action.refused audited + value byte-identical", () =>
      stageNonQualifyingFixtureFounderAttemptRefused(TARGET, economyOpsJar)
    );

    await run("that fixture (EconomyOps hat) successfully edits an ops-scope key", () =>
      stageEconomyOpsEditsOpsKey(TARGET, economyOpsJar, "match.reciprocity_signal_budget", 40)
    );

    await run("SuperAdmin revokes EconomyOps -> denied at the very next action, mid-session, no re-login", () =>
      stageRevokeThenDeniedMidSession(TARGET, superAdminJar, economyOpsJar, grantedStaffId, "match.reciprocity_signal_budget")
    );

    await run("SuperAdmin deactivates the fixture -> its live session dies within the revalidation interval", () =>
      stageDeactivateThenLiveSessionDies(TARGET, superAdminJar, economyOpsJar, grantedStaffId)
    );
  }

  await run("traversal probes (%2e/%2f/%5c/dot-segment) 404 pre-auth", () => stageTraversalProbes404PreAuth(TARGET));

  await run("a behavioral page-view event is read back (§8 seam 17)", () => stageBehavioralPageViewReadBack(TARGET, superAdminJar, dashboardBaseline.before));

  reportAndExit(failures);
}

function reportAndExit(failures) {
  if (failures.length > 0) {
    console.error("admin-host e2e FAILED:");
    for (const f of failures) console.error(`  - ${f}`);
    process.exitCode = 1;
    return;
  }
  console.log("admin-host e2e OK: sign-in refusals, SuperAdmin journey, config editor + interstitial + bounds, user search, grant/revoke/deactivate, traversal probes, and the behavioral read-back are all green live.");
}

const isMain = process.argv[1] && import.meta.url === `file://${process.argv[1]}`;
if (isMain) {
  main();
}

export { CookieJar, extractAntiforgeryToken, extractByTestId, extractFormAction, hasTestId };

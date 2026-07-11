#!/usr/bin/env node
// backend/e2e/identity.e2e.mjs — SLICE_S3_CONTRACT.md §10.3. Covers the signup/auth foundation (Pass A),
// the /v1/me/* account-management surface (Pass B), the data-export subsystem (Pass C, §6b), and the
// deletion/purge pipeline (Pass D, §2/§6c) — the export leg exercises POST/GET/download live end-to-end,
// incl. the export⋈purge registry cross-check (every store export-registry.json declares Contributes for
// must have a file in the real zip) and the OwnedResource(export) IDOR drill; the deletion leg drives a
// dedicated account through Phase L (request/grace/cancel) and Phase P (physical purge) live, incl. the
// `events_heatmap_provenance` full-history purge verb's first real invocation.
//
// Live E2E against the compose stack's Svac.PublicApi host — REAL HTTP endpoints, REAL Mailpit (REST
// oracle at :8025, never SMTP-side inspection), REAL 3A events read back via a real `docker compose exec
// postgres psql` probe (the same sanctioned pattern backend/e2e/substrate.e2e.mjs already uses for its
// own behavioral-event read-back — a READ-only verification probe, never a fake of the mutation flow
// itself, which runs 100% through real HTTP). SQL/stub bypass of the FLOW is banned (L30); this script
// never does that — every account/session/challenge/device/consent exists ONLY because a real HTTP call
// created it.
//
// Journey: handle-availability -> email-verification (code fetched from Mailpit) -> confirm -> complete
// (18th-birthday boundary vector) -> GET /v1/me works -> refresh rotation -> reuse drill (old refresh =>
// family revoked + audit event read back + email.sessions_revoked in Mailpit) -> old access token dies
// -> device register + push-consent write (read back off events_consent) -> category-8 drill (PUT /…/8
// byte-identical to /…/17) -> handle change, immediate second change denied LimitReached w/ correct
// resetsAt -> IDOR drill (a second account's sessionId DELETEd by the first ⇒ absence byte-identical to a
// random id, victim session survives) -> email change + confirm (old address gets email.email_changed_
// notice in Mailpit) -> login via auth/email-code + auth/session -> GET /v1/me -> logout -> under-18 +
// under-13 refusal drills (wire byte-identical, DB row count unchanged, under-13 challenge row
// destroyed) -> DELETION LEG on its own dedicated account: request (real grace) -> email.deletion_
// scheduled in Mailpit -> grace-law drill (GET /v1/me + export download still work, settings PATCH
// denies as absence) -> cancel -> GET /v1/me + PATCH work normally again -> DevSeams grace_days=0
// override -> re-request -> DevSeams sweep trigger -> email.deletion_completed in Mailpit -> purge
// read-back via psql (tombstoned account, retired+unavailable handle, freed+re-registrable email, zero
// sessions/devices/email_challenges, pseudonymized consent, gone export artifact, purge_run receipts
// incl. events_heatmap_provenance) -> old access token dies.
//
// Usage: IDENTITY_E2E_TARGET=http://localhost:8090 node backend/e2e/identity.e2e.mjs
//        (no IDENTITY_E2E_TARGET set -> SKIP, not a lie)
//        Requires a local `docker compose` with this repo's postgres + mailpit services up (the DB read-
//        back probes and the Mailpit REST calls both need them), and SVAC_DEVSEAMS_ENABLED=true on the
//        target host (already the docker-compose.yml default) for the two /internal/devseams/* triggers
//        the deletion leg needs — never reachable outside a DevSeams-enabled boot (L18).

import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";
import { execFile } from "node:child_process";
import { promisify } from "node:util";
import { readFileSync } from "node:fs";
import { mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";

const execFileAsync = promisify(execFile);
const __dirname = dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = join(__dirname, "..", "..");
const TARGET = process.env.IDENTITY_E2E_TARGET;
const MAILPIT = process.env.IDENTITY_E2E_MAILPIT ?? "http://localhost:8025";

// ---------- generic helpers ----------

function uniqueEmail(tag) {
  return `e2e-${tag}-${Date.now()}-${Math.floor(Math.random() * 1e6)}@example.com`;
}

function uniqueHandle(tag) {
  return `${tag}${Date.now()}`.replace(/[^a-z0-9_]/gi, "").slice(0, 20).toLowerCase();
}

async function postJson(baseUrl, path, body, headers = {}) {
  const res = await fetch(new URL(path, baseUrl), {
    method: "POST",
    headers: { "content-type": "application/json", ...headers },
    body: JSON.stringify(body),
  });
  const text = await res.text();
  let json;
  try {
    json = text.length > 0 ? JSON.parse(text) : {};
  } catch {
    json = { rawText: text };
  }
  return { status: res.status, body: json };
}

async function getJson(baseUrl, path, headers = {}) {
  const res = await fetch(new URL(path, baseUrl), { headers });
  const text = await res.text();
  let json;
  try {
    json = text.length > 0 ? JSON.parse(text) : {};
  } catch {
    json = { rawText: text };
  }
  return { status: res.status, body: json };
}

function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}

// ---------- Mailpit REST oracle ----------

async function findLatestMailpitMessage(toEmail, subjectContains, { timeoutMs = 5000, pollMs = 200 } = {}) {
  const deadline = Date.now() + timeoutMs;
  let lastCount = -1;
  while (Date.now() < deadline) {
    const res = await fetch(`${MAILPIT}/api/v1/search?query=${encodeURIComponent(`to:${toEmail}`)}`);
    if (res.ok) {
      const doc = await res.json();
      lastCount = doc.messages?.length ?? 0;
      const match = (doc.messages ?? []).find((m) => m.Subject.includes(subjectContains));
      if (match) {
        return match;
      }
    }
    await new Promise((r) => setTimeout(r, pollMs));
  }
  throw new Error(
    `no Mailpit message to "${toEmail}" with subject containing "${subjectContains}" within ${timeoutMs}ms (last search returned ${lastCount} message(s))`
  );
}

async function extractSixDigitCode(mailpitMessage) {
  const res = await fetch(`${MAILPIT}/api/v1/message/${mailpitMessage.ID}`);
  assert(res.ok, `GET /api/v1/message/${mailpitMessage.ID} -> HTTP ${res.status}`);
  const full = await res.json();
  const text = full.Text ?? full.HTML ?? "";
  const match = text.match(/\b(\d{6})\b/);
  assert(match, `could not find a 6-digit code in Mailpit message body: ${JSON.stringify(text)}`);
  return match[1];
}

// ---------- real Postgres read-back probe (READ-only; SLICE_S1_CONTRACT.md §10.4 precedent) ----------

async function psql(sql) {
  const { stdout } = await execFileAsync(
    "docker",
    ["compose", "exec", "-T", "postgres", "psql", "-U", "svac", "-d", "svac", "-t", "-A", "-c", sql],
    { cwd: REPO_ROOT }
  );
  return stdout.trim();
}

async function countAccountsWithEmail(email) {
  const out = await psql(`SELECT count(*) FROM identity.accounts WHERE email = '${email}';`);
  return Number.parseInt(out, 10);
}

async function challengeRowExists(challengeId) {
  const out = await psql(`SELECT count(*) FROM identity.email_challenges WHERE challenge_id = '${challengeId}';`);
  return Number.parseInt(out, 10) > 0;
}

async function auditEventTypesFor(accountId) {
  const out = await psql(`SELECT event_type FROM core.events_audit WHERE stream_id = '${accountId}' ORDER BY seq;`);
  return out.length > 0 ? out.split("\n") : [];
}

async function consentPayloadsFor(accountId) {
  const out = await psql(`SELECT payload FROM core.events_consent WHERE stream_id = '${accountId}' ORDER BY seq;`);
  if (out.length === 0) {
    return [];
  }
  return out.split("\n").map((line) => JSON.parse(line));
}

async function sessionRevokedAt(sessionId) {
  const out = await psql(`SELECT revoked_at FROM identity.sessions WHERE session_id = '${sessionId}';`);
  return out; // empty string means NULL
}

// ---------- deletion/purge read-back probes (SLICE_S3_CONTRACT.md §2/§6a/§6c/§10.3) ----------

async function countRows(sql) {
  return Number.parseInt(await psql(sql), 10);
}

/** {accountState, tombstoned, emailIsNull, handle} for one accounts row — the tombstone shape itself (§2 Phase P step 6). */
async function accountRow(accountId) {
  const out = await psql(
    `SELECT account_state, (tombstoned_at IS NOT NULL), (email IS NULL), handle FROM identity.accounts WHERE account_id = '${accountId}';`
  );
  const [accountState, tombstonedRaw, emailIsNullRaw, handle] = out.split("|");
  return { accountState, tombstoned: tombstonedRaw === "t", emailIsNull: emailIsNullRaw === "t", handle };
}

async function retiredHandleExists(handle) {
  return (await countRows(`SELECT count(*) FROM identity.retired_handles WHERE handle = '${handle}';`)) > 0;
}

async function countSessionsForAccount(accountId) {
  return countRows(`SELECT count(*) FROM identity.sessions WHERE account_id = '${accountId}';`);
}

async function countDevicesForAccount(accountId) {
  return countRows(`SELECT count(*) FROM identity.devices WHERE account_id = '${accountId}';`);
}

async function countEmailChallengesForEmail(email) {
  return countRows(`SELECT count(*) FROM identity.email_challenges WHERE email_lower = lower('${email}');`);
}

async function countConsentCurrentForAccount(accountId) {
  return countRows(`SELECT count(*) FROM identity.consent_current WHERE account_id = '${accountId}';`);
}

/** Zero rows here proves the RAW account id was severed from core.events_consent (Pseudonymize re-keys stream_id, §6a). */
async function countEventsConsentForStreamId(accountId) {
  return countRows(`SELECT count(*) FROM core.events_consent WHERE stream_id = '${accountId}';`);
}

async function countExportJobsForAccount(accountId) {
  return countRows(`SELECT count(*) FROM identity.export_jobs WHERE account_id = '${accountId}';`);
}

/** Must be captured BEFORE the sweep runs — Phase P pseudonymizes deletion_jobs.account_id as part of the SAME purge pass (§6a: "the receipt SURVIVES — proof deletion ran"), so a post-sweep lookup by account_id would find nothing. */
async function latestDeletionJobId(accountId) {
  const out = await psql(`SELECT deletion_id FROM identity.deletion_jobs WHERE account_id = '${accountId}' ORDER BY requested_at DESC LIMIT 1;`);
  assert(out.length > 0, `no identity.deletion_jobs row found for account ${accountId}`);
  return out;
}

/**
 * Post-sweep read of the SAME job row by its immutable PK (deletion_id) — survives the account_id
 * pseudonymization above. Three separate scalar queries rather than one multi-column row: purge_run_ids'
 * own JSON text could in principle contain psql's "|" unaligned-output field separator, and a single
 * query would then split ambiguously.
 */
async function deletionJobById(deletionId) {
  const state = await psql(`SELECT state FROM identity.deletion_jobs WHERE deletion_id = '${deletionId}';`);
  const purgeRunIdsJson = await psql(`SELECT coalesce(purge_run_ids::text, '') FROM identity.deletion_jobs WHERE deletion_id = '${deletionId}';`);
  const executed = (await psql(`SELECT (executed_at IS NOT NULL) FROM identity.deletion_jobs WHERE deletion_id = '${deletionId}';`)) === "t";
  return { state, purgeRunIdsJson, executed };
}

/** The DIRECT receipt-table read-back (core.purge_runs, §6c) — independent of the deletion_jobs JSON blob's own copy of the same fact, scoped to runs started at/after `sinceIso` so a long-lived compose stack's PRIOR e2e pass never makes this vacuously true. */
async function purgeRunReceiptExists(storeKey, sinceIso) {
  return (
    (await countRows(
      `SELECT count(*) FROM core.purge_runs WHERE store_key = '${storeKey}' AND started_at >= '${sinceIso}';`
    )) > 0
  );
}

// ---------- export⋈purge registry + zip helpers (SLICE_S3_CONTRACT.md §6b) ----------

/** The same source-of-truth committed manifest ExportPurgeCrossGateTests.cs checks against — every "Contributes" store key this build's export MUST produce a file for. */
function contributesStoreKeys() {
  const registry = JSON.parse(readFileSync(join(REPO_ROOT, "backend", "domain-core", "export-registry.json"), "utf8"));
  return registry.filter((e) => e.state === "Contributes").map((e) => e.storeKey);
}

/** Writes the downloaded zip to a scratch temp dir and lists its entry names via the system `unzip` (vanilla — no new npm dependency for one CLI tool already assumed present in CI/dev images). */
async function listZipEntries(zipBuffer) {
  const dir = await mkdtemp(join(tmpdir(), "identity-e2e-export-"));
  const zipPath = join(dir, "export.zip");
  await writeFile(zipPath, zipBuffer);
  const { stdout } = await execFileAsync("unzip", ["-Z1", zipPath]);
  const entries = stdout.trim().split("\n").filter(Boolean);
  return { dir, zipPath, entries };
}

async function readZipEntry(zipPath, entryName) {
  const { stdout } = await execFileAsync("unzip", ["-p", zipPath, entryName]);
  return stdout;
}

// ---------- the journey, one function per stage ----------

async function stageHealth(baseUrl) {
  const { status, body } = await getJson(baseUrl, "/health");
  assert(status === 200, `GET /health -> HTTP ${status}, expected 200`);
  assert(body.status === "healthy", `GET /health body.status = "${body.status}", expected "healthy"`);
}

async function stageHandleAvailability(baseUrl) {
  const freshHandle = uniqueHandle("avail");
  const { status, body } = await getJson(baseUrl, `/v1/signup/handle-availability?handle=${freshHandle}`);
  assert(status === 200, `GET handle-availability -> HTTP ${status}`);
  assert(body.available === true, `expected a fresh handle to be available, got ${JSON.stringify(body)}`);
}

async function stageSignupThroughSession(baseUrl, email, birthdate) {
  const { status: issueStatus, body: issueBody } = await postJson(baseUrl, "/v1/signup/email-verification", {
    email,
    locale: "en",
  });
  assert(issueStatus === 202, `POST email-verification -> HTTP ${issueStatus}: ${JSON.stringify(issueBody)}`);
  assert(typeof issueBody.challengeId === "string" && issueBody.challengeId.startsWith("chl_"), `unexpected challengeId shape: ${JSON.stringify(issueBody)}`);

  const mail = await findLatestMailpitMessage(email, "verification code");
  const code = await extractSixDigitCode(mail);

  const { status: confirmStatus, body: confirmBody } = await postJson(baseUrl, "/v1/signup/email-verification/confirm", {
    challengeId: issueBody.challengeId,
    code,
  });
  assert(confirmStatus === 200, `POST confirm -> HTTP ${confirmStatus}: ${JSON.stringify(confirmBody)}`);
  assert(typeof confirmBody.verifiedToken === "string" && confirmBody.verifiedToken.startsWith("vft_"), `unexpected verifiedToken shape: ${JSON.stringify(confirmBody)}`);

  const handle = uniqueHandle("e2e");
  const { status: completeStatus, body: completeBody } = await postJson(baseUrl, "/v1/signup/complete", {
    verifiedToken: confirmBody.verifiedToken,
    handle,
    birthdate,
    fandomTag: "shonen",
    locale: "en",
  });
  assert(completeStatus === 201, `POST signup/complete -> HTTP ${completeStatus}: ${JSON.stringify(completeBody)}`);
  assert(typeof completeBody.accountId === "string" && completeBody.accountId.startsWith("usr_"), `unexpected accountId shape: ${JSON.stringify(completeBody)}`);
  assert(typeof completeBody.accessToken === "string" && completeBody.accessToken.startsWith("sst_"));
  assert(typeof completeBody.refreshToken === "string" && completeBody.refreshToken.startsWith("srt_"));

  return { handle, challengeId: issueBody.challengeId, session: completeBody };
}

async function stageMeWorks(baseUrl, accessToken, expectedHandle, expectedEmail) {
  const { status, body } = await getJson(baseUrl, "/v1/me", { authorization: `Bearer ${accessToken}` });
  assert(status === 200, `GET /v1/me -> HTTP ${status}: ${JSON.stringify(body)}`);
  assert(body.handle === expectedHandle, `GET /v1/me handle = "${body.handle}", expected "${expectedHandle}"`);
  assert(body.email === expectedEmail, `GET /v1/me email = "${body.email}", expected "${expectedEmail}"`);
  assert(typeof body.ageYears === "number", `GET /v1/me ageYears missing/non-numeric: ${JSON.stringify(body)}`);
  assert(!("birthdate" in body) && !("dob" in body), `GET /v1/me leaked a raw birthdate field: ${JSON.stringify(body)}`);
}

async function stageRefreshRotationAndReuseAlarm(baseUrl, session, accountId, email) {
  const { status: rotateStatus, body: rotated } = await postJson(baseUrl, "/v1/auth/refresh", {
    refreshToken: session.refreshToken,
  });
  assert(rotateStatus === 200, `POST auth/refresh (legit) -> HTTP ${rotateStatus}: ${JSON.stringify(rotated)}`);
  assert(rotated.refreshToken !== session.refreshToken, "refresh rotation returned the SAME refresh token — single-use violated");

  // REUSE: present the OLD (already-consumed) refresh token — the theft alarm.
  const { status: reuseStatus, body: reuseBody } = await postJson(baseUrl, "/v1/auth/refresh", {
    refreshToken: session.refreshToken,
  });
  assert(reuseStatus >= 400 && reuseStatus < 500, `refresh REUSE -> HTTP ${reuseStatus}, expected a 4xx generic Problem: ${JSON.stringify(reuseBody)}`);
  assert(reuseBody.messageKey !== undefined, `refresh REUSE response is not the Problem shape: ${JSON.stringify(reuseBody)}`);

  // The whole family died, including the token that JUST won the legit rotation above.
  const { status: deadFamilyStatus } = await postJson(baseUrl, "/v1/auth/refresh", { refreshToken: rotated.refreshToken });
  assert(deadFamilyStatus >= 400 && deadFamilyStatus < 500, `post-reuse rotated token still refreshes -> HTTP ${deadFamilyStatus}, the family was not fully revoked`);

  // The original access token is dead too (session revoked -> resolves anonymous -> policy denies as absence).
  const { status: oldAccessStatus } = await getJson(baseUrl, "/v1/me", { authorization: `Bearer ${session.accessToken}` });
  assert(oldAccessStatus === 404, `GET /v1/me with a revoked session's access token -> HTTP ${oldAccessStatus}, expected 404 (absence)`);

  // Audit event landed, read back for real (SLICE_S1_CONTRACT.md §10.4 precedent: a real psql probe).
  const auditTypes = await auditEventTypesFor(accountId);
  assert(
    auditTypes.includes("identity.session_family_revoked"),
    `core.events_audit for account ${accountId} does not contain "identity.session_family_revoked" (found: ${JSON.stringify(auditTypes)})`
  );

  // The category-8 security notice landed in Mailpit.
  const notice = await findLatestMailpitMessage(email, "revoked");
  assert(notice, "email.sessions_revoked mail never arrived in Mailpit after the reuse drill");
}

/** Logs in via the two-step email-code flow and returns the fresh SessionCreated body. */
async function stageLogin(baseUrl, email) {
  const { status: codeStatus, body: codeBody } = await postJson(baseUrl, "/v1/auth/email-code", { email });
  assert(codeStatus === 202, `POST auth/email-code -> HTTP ${codeStatus}: ${JSON.stringify(codeBody)}`);
  assert(Object.keys(codeBody).length === 0, `POST auth/email-code body should be {} per §1c, got ${JSON.stringify(codeBody)}`);

  const mail = await findLatestMailpitMessage(email, "sign-in code");
  const code = await extractSixDigitCode(mail);

  const { status: sessionStatus, body: sessionBody } = await postJson(baseUrl, "/v1/auth/session", { email, code });
  assert(sessionStatus === 200, `POST auth/session -> HTTP ${sessionStatus}: ${JSON.stringify(sessionBody)}`);
  assert(typeof sessionBody.accessToken === "string" && sessionBody.accessToken.startsWith("sst_"));
  return sessionBody;
}

async function stageLoginJourney(baseUrl, email, handle) {
  const sessionBody = await stageLogin(baseUrl, email);
  await stageMeWorks(baseUrl, sessionBody.accessToken, handle, email);

  const { status: logoutStatus } = await postJson(baseUrl, "/v1/auth/logout", {}, { authorization: `Bearer ${sessionBody.accessToken}` });
  assert(logoutStatus === 204, `POST auth/logout -> HTTP ${logoutStatus}, expected 204`);

  const { status: postLogoutStatus } = await getJson(baseUrl, "/v1/me", { authorization: `Bearer ${sessionBody.accessToken}` });
  assert(postLogoutStatus === 404, `GET /v1/me after logout -> HTTP ${postLogoutStatus}, expected 404 (absence)`);
}

/** One drill for a given (email, birthdate) pair — shared by the under-18 and under-13 cases below so the two callers' assertions are trivially comparable. */
async function runMinorDrill(baseUrl, tag, birthdate) {
  const email = uniqueEmail(tag);
  const { status: issueStatus, body: issueBody } = await postJson(baseUrl, "/v1/signup/email-verification", { email, locale: "en" });
  assert(issueStatus === 202, `[${tag}] POST email-verification -> HTTP ${issueStatus}`);

  const mail = await findLatestMailpitMessage(email, "verification code");
  const code = await extractSixDigitCode(mail);

  const { status: confirmStatus, body: confirmBody } = await postJson(baseUrl, "/v1/signup/email-verification/confirm", {
    challengeId: issueBody.challengeId,
    code,
  });
  assert(confirmStatus === 200, `[${tag}] POST confirm -> HTTP ${confirmStatus}`);

  const { status: completeStatus, body: completeBody } = await postJson(baseUrl, "/v1/signup/complete", {
    verifiedToken: confirmBody.verifiedToken,
    handle: uniqueHandle(tag),
    birthdate,
    fandomTag: "shonen",
    locale: "en",
  });

  const accountCount = await countAccountsWithEmail(email);
  return { status: completeStatus, body: completeBody, challengeId: issueBody.challengeId, accountCount };
}

async function stageMinorProtectionDrills(baseUrl) {
  const today = new Date();
  const isoYearsAgo = (years) => {
    const d = new Date(today);
    d.setUTCFullYear(d.getUTCFullYear() - years);
    return d.toISOString().slice(0, 10);
  };

  const under18 = await runMinorDrill(baseUrl, "under18", isoYearsAgo(17));
  const under13 = await runMinorDrill(baseUrl, "under13", isoYearsAgo(10));

  assert(under18.status === 422, `under-18 drill -> HTTP ${under18.status}, expected 422`);
  assert(under13.status === 422, `under-13 drill -> HTTP ${under13.status}, expected 422`);
  assert(
    under18.body.messageKey === "signup.refused_age_floor",
    `under-18 drill messageKey = "${under18.body.messageKey}", expected "signup.refused_age_floor"`
  );
  assert(
    under18.body.messageKey === under13.body.messageKey,
    `under-18 and under-13 drills rendered DIFFERENT message keys ("${under18.body.messageKey}" vs "${under13.body.messageKey}") — the minor floors must be wire-indistinguishable`
  );
  assert(under18.status === under13.status, "under-18 and under-13 drills rendered different HTTP statuses — must be byte-identical");

  assert(under18.accountCount === 0, `under-18 drill left ${under18.accountCount} account row(s) behind — zero persistence required`);
  assert(under13.accountCount === 0, `under-13 drill left ${under13.accountCount} account row(s) behind — zero persistence required`);

  const under18ChallengeSurvives = await challengeRowExists(under18.challengeId);
  const under13ChallengeSurvives = await challengeRowExists(under13.challengeId);
  assert(under18ChallengeSurvives, "under-18 drill's challenge row was destroyed — only under-13 is a hard delete (§1g)");
  assert(!under13ChallengeSurvives, "under-13 drill's challenge row SURVIVED — §1g requires a same-tx hard delete");
}

// ---------- /v1/me/* stages (SLICE_S3_CONTRACT.md §1c Pass 2) ----------

async function stageDeviceAndPushConsent(baseUrl, accessToken, accountId) {
  const { status: deviceStatus, body: deviceBody } = await postJson(
    baseUrl,
    "/v1/me/devices",
    { platform: "ios", pushToken: "e2e-push-token-abc" },
    { authorization: `Bearer ${accessToken}` }
  );
  assert(deviceStatus === 201, `POST /v1/me/devices -> HTTP ${deviceStatus}: ${JSON.stringify(deviceBody)}`);
  assert(typeof deviceBody.deviceId === "string" && deviceBody.deviceId.startsWith("dev_"), `unexpected deviceId shape: ${JSON.stringify(deviceBody)}`);

  const putRes = await fetch(new URL("/v1/me/push-consents/3", baseUrl), {
    method: "PUT",
    headers: { "content-type": "application/json", authorization: `Bearer ${accessToken}` },
    body: JSON.stringify({ enabled: true }),
  });
  assert(putRes.status === 204, `PUT /v1/me/push-consents/3 -> HTTP ${putRes.status}, expected 204`);

  const { status: getStatus, body: rows } = await getJson(baseUrl, "/v1/me/push-consents", { authorization: `Bearer ${accessToken}` });
  assert(getStatus === 200, `GET /v1/me/push-consents -> HTTP ${getStatus}`);
  assert(Array.isArray(rows) && rows.length === 8, `GET /v1/me/push-consents expected exactly 8 rows (1-7,9), got ${JSON.stringify(rows)}`);
  assert(!rows.some((r) => r.category === 8), "GET /v1/me/push-consents returned a row for category 8 — must be absent");
  const row3 = rows.find((r) => r.category === 3);
  assert(row3 && row3.enabled === true, `push-consent category 3 not enabled after PUT: ${JSON.stringify(rows)}`);

  // The write landed on events_consent, read back for real (never a stub/SQL bypass of the WRITE path).
  const payloads = await consentPayloadsFor(accountId);
  const push3 = payloads.find((p) => p.consent_key === "push_category_3");
  assert(push3, `no push_category_3 consent.recorded event found for account ${accountId} in core.events_consent (found: ${JSON.stringify(payloads)})`);
  assert(push3.decision === "granted", `push_category_3 consent decision = "${push3.decision}", expected "granted"`);
}

async function stagePutMethod(baseUrl, path, body, headers) {
  const res = await fetch(new URL(path, baseUrl), {
    method: "PUT",
    headers: { "content-type": "application/json", ...headers },
    body: JSON.stringify(body),
  });
  const text = await res.text();
  return { status: res.status, text };
}

async function stageCategoryEightDrill(baseUrl, accessToken) {
  const headers = { authorization: `Bearer ${accessToken}` };
  const eight = await stagePutMethod(baseUrl, "/v1/me/push-consents/8", { enabled: true }, headers);
  const seventeen = await stagePutMethod(baseUrl, "/v1/me/push-consents/17", { enabled: true }, headers);

  assert(eight.status === 404, `PUT /v1/me/push-consents/8 -> HTTP ${eight.status}, expected 404 (unrepresentable)`);
  assert(eight.status === seventeen.status, `category-8 (${eight.status}) and category-17 (${seventeen.status}) rendered DIFFERENT statuses — must be byte-identical`);
  assert(eight.text === seventeen.text, `category-8 and category-17 response bodies differ: "${eight.text}" vs "${seventeen.text}"`);
}

async function stageHandleChangeCooldown(baseUrl, accessToken) {
  const headers = { authorization: `Bearer ${accessToken}` };
  const newHandle = uniqueHandle("hc1");
  const first = await postJson(baseUrl, "/v1/me/handle", { handle: newHandle }, headers);
  assert(first.status === 200, `POST /v1/me/handle (first change) -> HTTP ${first.status}: ${JSON.stringify(first.body)}`);

  const second = await postJson(baseUrl, "/v1/me/handle", { handle: uniqueHandle("hc2") }, headers);
  assert(second.status === 429, `POST /v1/me/handle (immediate second change) -> HTTP ${second.status}, expected 429 LimitReached: ${JSON.stringify(second.body)}`);
  assert(second.body.quotaKey === "identity.handle.change", `second handle-change LimitReached.quotaKey = "${second.body.quotaKey}", expected "identity.handle.change"`);
  assert(second.body.premiumExtends === false, `second handle-change LimitReached.premiumExtends = ${second.body.premiumExtends}, expected false`);
  assert(typeof second.body.resetsAt === "string" && !Number.isNaN(Date.parse(second.body.resetsAt)), `second handle-change LimitReached.resetsAt is not a valid timestamp: ${JSON.stringify(second.body)}`);
  const resetsAt = new Date(second.body.resetsAt);
  const now = new Date();
  assert(resetsAt.getTime() > now.getTime(), `second handle-change LimitReached.resetsAt (${second.body.resetsAt}) is not in the future`);

  return newHandle;
}

async function stageIdorSessionDrill(baseUrl, victimAccessToken, attackerAccessToken) {
  const { status: sessionsStatus, body: sessions } = await getJson(baseUrl, "/v1/me/sessions", { authorization: `Bearer ${victimAccessToken}` });
  assert(sessionsStatus === 200, `GET /v1/me/sessions (victim) -> HTTP ${sessionsStatus}`);
  const currentSession = sessions.find((s) => s.current === true);
  assert(currentSession, `no session marked "current" for the victim: ${JSON.stringify(sessions)}`);
  const victimSessionId = currentSession.sessionId;

  const randomSessionId = "ses_01HZZZZZZZZZZZZZZZZZZZZZZZ";
  const foreignRes = await fetch(new URL(`/v1/me/sessions/${victimSessionId}`, baseUrl), {
    method: "DELETE",
    headers: { authorization: `Bearer ${attackerAccessToken}` },
  });
  const randomRes = await fetch(new URL(`/v1/me/sessions/${randomSessionId}`, baseUrl), {
    method: "DELETE",
    headers: { authorization: `Bearer ${attackerAccessToken}` },
  });

  assert(foreignRes.status === 404, `DELETE /v1/me/sessions/{victim's session} by attacker -> HTTP ${foreignRes.status}, expected 404 (absence)`);
  assert(foreignRes.status === randomRes.status, `foreign session (${foreignRes.status}) and random session (${randomRes.status}) rendered DIFFERENT statuses — IDOR must be byte-identical to nonexistence`);
  const foreignText = await foreignRes.text();
  const randomText = await randomRes.text();
  assert(foreignText === randomText, `foreign-session and random-session DELETE bodies differ: "${foreignText}" vs "${randomText}"`);

  // The victim's session is provably untouched (both the DB row AND the wire GET /v1/me still work).
  const revokedAt = await sessionRevokedAt(victimSessionId);
  assert(revokedAt === "", `victim's session was revoked by the attacker's failed DELETE attempt (revoked_at = "${revokedAt}") — deny-as-absence must never mutate what it denies`);

  const { status: stillWorksStatus } = await getJson(baseUrl, "/v1/me", { authorization: `Bearer ${victimAccessToken}` });
  assert(stillWorksStatus === 200, `GET /v1/me with the victim's still-live session -> HTTP ${stillWorksStatus}, expected 200 (untouched)`);
}

async function stageEmailChangeDrill(baseUrl, accessToken, oldEmail) {
  const newEmail = uniqueEmail("changed");
  const putRes = await fetch(new URL("/v1/me/email", baseUrl), {
    method: "PUT",
    headers: { "content-type": "application/json", authorization: `Bearer ${accessToken}` },
    body: JSON.stringify({ email: newEmail }),
  });
  const putText = await putRes.text();
  const putStatus = putRes.status;
  const putBody = putText.length > 0 ? JSON.parse(putText) : {};
  assert(putStatus === 202, `PUT /v1/me/email -> HTTP ${putStatus}: ${JSON.stringify(putBody)}`);
  assert(typeof putBody.challengeId === "string" && putBody.challengeId.startsWith("chl_"), `unexpected challengeId shape: ${JSON.stringify(putBody)}`);

  const mail = await findLatestMailpitMessage(newEmail, "verification code");
  const code = await extractSixDigitCode(mail);

  const { status: confirmStatus, body: confirmBody } = await postJson(
    baseUrl,
    "/v1/me/email/confirm",
    { challengeId: putBody.challengeId, code },
    { authorization: `Bearer ${accessToken}` }
  );
  assert(confirmStatus === 200, `POST /v1/me/email/confirm -> HTTP ${confirmStatus}: ${JSON.stringify(confirmBody)}`);

  // The account-takeover tripwire: the OLD address gets the security notice.
  const notice = await findLatestMailpitMessage(oldEmail, "email changed");
  assert(notice, `email.email_changed_notice never arrived at the OLD address (${oldEmail}) in Mailpit`);

  const { status: meStatus, body: meBody } = await getJson(baseUrl, "/v1/me", { authorization: `Bearer ${accessToken}` });
  assert(meStatus === 200, `GET /v1/me after email change -> HTTP ${meStatus}`);
  assert(meBody.email === newEmail, `GET /v1/me email = "${meBody.email}" after change, expected "${newEmail}"`);

  return newEmail;
}

// ---------- export leg (SLICE_S3_CONTRACT.md §1c/§6b/§10.3) ----------

/** POST /v1/me/export -> worker runs -> email.export_ready in Mailpit -> GET state ready -> download the zip -> assert per-store files + manifest present (incl. consent + ledger contributions). Returns the exportId for the IDOR drill below. */
async function stageExportLeg(baseUrl, accessToken, email) {
  const { status: postStatus, body: postBody } = await postJson(baseUrl, "/v1/me/export", {}, { authorization: `Bearer ${accessToken}` });
  assert(postStatus === 202, `POST /v1/me/export -> HTTP ${postStatus}: ${JSON.stringify(postBody)}`);
  const exportId = postBody.exportId;
  assert(typeof exportId === "string" && exportId.startsWith("exp_"), `unexpected exportId shape: ${JSON.stringify(postBody)}`);

  // Idempotency: a duplicate POST while the job is active resolves to the SAME job.
  const { status: dupStatus, body: dupBody } = await postJson(baseUrl, "/v1/me/export", {}, { authorization: `Bearer ${accessToken}` });
  assert(dupStatus === 202, `duplicate POST /v1/me/export -> HTTP ${dupStatus}`);
  assert(dupBody.exportId === exportId, `duplicate POST /v1/me/export returned a DIFFERENT job id ("${dupBody.exportId}" vs "${exportId}") — single-active idempotency violated`);

  const mail = await findLatestMailpitMessage(email, "export");
  assert(mail, `email.export_ready never arrived in Mailpit for ${email}`);

  const { status: getStatus, body: statusBody } = await getJson(baseUrl, `/v1/me/export/${exportId}`, { authorization: `Bearer ${accessToken}` });
  assert(getStatus === 200, `GET /v1/me/export/${exportId} -> HTTP ${getStatus}: ${JSON.stringify(statusBody)}`);
  assert(statusBody.state === "ready", `export state = "${statusBody.state}", expected "ready": ${JSON.stringify(statusBody)}`);
  assert(typeof statusBody.expiresAt === "string" && !Number.isNaN(Date.parse(statusBody.expiresAt)), `export expiresAt is not a valid timestamp: ${JSON.stringify(statusBody)}`);

  const downloadRes = await fetch(new URL(`/v1/me/export/${exportId}/download`, baseUrl), { headers: { authorization: `Bearer ${accessToken}` } });
  assert(downloadRes.status === 200, `GET /v1/me/export/${exportId}/download -> HTTP ${downloadRes.status}`);
  assert((downloadRes.headers.get("content-type") ?? "").includes("zip"), `export download content-type = "${downloadRes.headers.get("content-type")}", expected zip`);
  const zipBuffer = Buffer.from(await downloadRes.arrayBuffer());

  const requiredStoreKeys = contributesStoreKeys();
  const { dir, zipPath, entries } = await listZipEntries(zipBuffer);
  try {
    assert(entries.includes("manifest.json"), `manifest.json missing from export zip entries: ${JSON.stringify(entries)}`);
    for (const storeKey of requiredStoreKeys) {
      assert(entries.includes(`${storeKey}.json`), `${storeKey}.json (a registered Contributes store) missing from the export zip — entries: ${JSON.stringify(entries)}`);
    }

    const manifest = JSON.parse(await readZipEntry(zipPath, "manifest.json"));
    const manifestStoreKeys = new Set(manifest.stores.map((s) => s.storeKey));
    for (const storeKey of requiredStoreKeys) {
      assert(manifestStoreKeys.has(storeKey), `manifest.json stores[] missing "${storeKey}": ${JSON.stringify(manifest.stores)}`);
    }
    assert(typeof manifest.generatedAt === "string", `manifest.json missing generatedAt: ${JSON.stringify(manifest)}`);
    assert(typeof manifest.statutoryDeadlineAt === "string", `manifest.json missing statutoryDeadlineAt: ${JSON.stringify(manifest)}`);

    // The consent CONTRIBUTION, read back for real off the zip (this journey's own real push-category-3
    // write, stageDeviceAndPushConsent) — content-verified, not just presence-verified.
    const consentRaw = await readZipEntry(zipPath, "events_consent.json");
    assert(consentRaw.includes("push_category_3"), `events_consent.json export does not contain this journey's push_category_3 consent write: ${consentRaw}`);
    const consentCurrentRaw = await readZipEntry(zipPath, "identity.consent_current.json");
    assert(consentCurrentRaw.includes("push_category_3"), `identity.consent_current.json export does not contain push_category_3: ${consentCurrentRaw}`);

    // The ledger CONTRIBUTION — present and correctly shaped (this journey never earns ledger points,
    // §0: S3 owns no ledger-earning endpoint, so a real non-zero balance is not this leg's to prove;
    // presence + the ILedger.BalanceOf shape is).
    const ledgerRaw = await readZipEntry(zipPath, "ledger_entries.json");
    const ledgerPayload = JSON.parse(ledgerRaw);
    assert(typeof ledgerPayload.balance === "object" && ledgerPayload.balance !== null, `ledger_entries.json export missing the balance object: ${ledgerRaw}`);
    assert(Array.isArray(ledgerPayload.entries), `ledger_entries.json export missing the entries array: ${ledgerRaw}`);

    // The accounts contribution carries the REAL birthdate (Art. 15's own artifact — unlike GET /v1/me).
    const accountsRaw = await readZipEntry(zipPath, "identity.accounts.json");
    assert(accountsRaw.includes("\"birthdate\"") && !accountsRaw.includes("\"birthdate\": null"), `identity.accounts.json export missing a real birthdate: ${accountsRaw}`);
  } finally {
    await rm(dir, { recursive: true, force: true });
  }

  return exportId;
}

/** A second account's exportId ⇒ absence, byte-identical to a random id (both the status read AND the download). */
async function stageExportIdorDrill(baseUrl, victimExportId, attackerAccessToken) {
  const randomExportId = "exp_01HZZZZZZZZZZZZZZZZZZZZZZZ";
  const headers = { authorization: `Bearer ${attackerAccessToken}` };

  const foreignStatusRes = await fetch(new URL(`/v1/me/export/${victimExportId}`, baseUrl), { headers });
  const randomStatusRes = await fetch(new URL(`/v1/me/export/${randomExportId}`, baseUrl), { headers });
  assert(foreignStatusRes.status === 404, `GET a foreign account's exportId -> HTTP ${foreignStatusRes.status}, expected 404 (absence)`);
  assert(foreignStatusRes.status === randomStatusRes.status, `foreign exportId (${foreignStatusRes.status}) and random exportId (${randomStatusRes.status}) rendered DIFFERENT statuses — must be byte-identical`);
  const foreignStatusText = await foreignStatusRes.text();
  const randomStatusText = await randomStatusRes.text();
  assert(foreignStatusText === randomStatusText, `foreign-exportId and random-exportId GET bodies differ: "${foreignStatusText}" vs "${randomStatusText}"`);

  const foreignDownloadRes = await fetch(new URL(`/v1/me/export/${victimExportId}/download`, baseUrl), { headers });
  const randomDownloadRes = await fetch(new URL(`/v1/me/export/${randomExportId}/download`, baseUrl), { headers });
  assert(foreignDownloadRes.status === 404, `download a foreign account's exportId -> HTTP ${foreignDownloadRes.status}, expected 404 (absence)`);
  assert(foreignDownloadRes.status === randomDownloadRes.status, `foreign download (${foreignDownloadRes.status}) and random download (${randomDownloadRes.status}) rendered DIFFERENT statuses — must be byte-identical`);
  const foreignDownloadText = await foreignDownloadRes.text();
  const randomDownloadText = await randomDownloadRes.text();
  assert(foreignDownloadText === randomDownloadText, `foreign-exportId and random-exportId download bodies differ, byte-for-byte comparison failed`);
}

async function stageEighteenthBirthdayBoundary(baseUrl) {
  // Turns 18 exactly TODAY — must be treated as an adult (the birthday itself already passes, AgeMath.IsAtLeast).
  const today = new Date();
  const exactly18 = new Date(today);
  exactly18.setUTCFullYear(exactly18.getUTCFullYear() - 18);
  const birthdate = exactly18.toISOString().slice(0, 10);

  const email = uniqueEmail("boundary18");
  const { session } = await stageSignupThroughSession(baseUrl, email, birthdate);
  assert(typeof session.accountId === "string", `18th-birthday-today vector was refused, expected acceptance: ${JSON.stringify(session)}`);
}

// ---------- deletion/purge leg (SLICE_S3_CONTRACT.md §2/§6a/§6c/§10.3) ----------
//
// Runs on its OWN dedicated account (never the shared journey account above) so purge-completeness
// assertions never have to untangle handle/email changes another leg already made. Two DevSeams-gated
// triggers do the otherwise-impossible-without-an-admin-desk work: flipping identity.deletion.grace_days
// at runtime (POST /internal/devseams/deletion-grace-days) and running the physical-purge worker on
// demand (POST /internal/devseams/deletion-sweep, the S1 canary pattern the prior agent built). The
// journey needs BOTH a real (nonzero) grace window — so the cancel drill has a genuine future
// effective_at to cancel against, since grace_days=0 makes cancellation mathematically impossible by
// design (effective_at == requested_at exactly; see AccountLifecycle.CancelDeletion) — AND grace_days=0
// for the request that must actually reach the worker live; those two needs cannot share one boot-time
// config value, hence the runtime flip mid-journey.

/** POST /internal/devseams/deletion-grace-days {days} — never in the shipped contract; DevSeams-gated identically to the sweep trigger. */
async function devSeamsSetGraceDays(baseUrl, days) {
  const { status, body } = await postJson(baseUrl, "/internal/devseams/deletion-grace-days", { days });
  assert(status === 200, `POST /internal/devseams/deletion-grace-days {days:${days}} -> HTTP ${status}: ${JSON.stringify(body)}`);
  assert(body.graceDays === days, `devseams grace-days override echoed ${JSON.stringify(body)}, expected graceDays:${days}`);
}

/** POST /internal/devseams/deletion-sweep — runs DeletionPhysicalPurgeWorker.RunDueSweepAsync on demand; returns the number of jobs processed. */
async function devSeamsTriggerSweep(baseUrl) {
  const { status, body } = await postJson(baseUrl, "/internal/devseams/deletion-sweep", {});
  assert(status === 200, `POST /internal/devseams/deletion-sweep -> HTTP ${status}: ${JSON.stringify(body)}`);
  return body.processed;
}

async function stageDeletionLeg(baseUrl) {
  const email = uniqueEmail("deletion");
  const { handle: originalHandle, session } = await stageSignupThroughSession(baseUrl, email, "1990-01-01");
  const { accessToken, accountId } = session;
  const headers = { authorization: `Bearer ${accessToken}` };

  // Give the account real rows in every store the purge registry says AccountDeletion touches, so the
  // "zero afterward" assertions below are non-vacuous: a device + a push-category consent write (mirrors
  // stageDeviceAndPushConsent's own real-write-then-read-back discipline), and a real export job+artifact.
  await stageDeviceAndPushConsent(baseUrl, accessToken, accountId);
  const { status: preExportPostStatus, body: preExportPostBody } = await postJson(baseUrl, "/v1/me/export", {}, headers);
  assert(preExportPostStatus === 202, `POST /v1/me/export (deletion leg) -> HTTP ${preExportPostStatus}: ${JSON.stringify(preExportPostBody)}`);
  const preDeletionExportId = preExportPostBody.exportId;
  const { status: preExportStatus, body: preExportBody } = await getJson(baseUrl, `/v1/me/export/${preDeletionExportId}`, headers);
  assert(preExportStatus === 200 && preExportBody.state === "ready", `export not ready immediately after POST (deletion leg, synchronous worker): ${JSON.stringify(preExportBody)}`);

  // --- Phase L, request #1: real (nonzero, manifest v0=14d) grace — this is the one the cancel drill needs. ---
  const { status: post1Status, body: post1Body } = await postJson(baseUrl, "/v1/me/deletion", {}, headers);
  assert(post1Status === 200, `POST /v1/me/deletion (request #1) -> HTTP ${post1Status}: ${JSON.stringify(post1Body)}`);
  assert(post1Body.exportOffered === true, `POST /v1/me/deletion response missing exportOffered:true (§1c export-offered-first): ${JSON.stringify(post1Body)}`);
  assert(typeof post1Body.effectiveAt === "string" && new Date(post1Body.effectiveAt).getTime() > Date.now(), `POST /v1/me/deletion effectiveAt is not safely in the future (grace_days=0 would make the cancel drill below impossible): ${JSON.stringify(post1Body)}`);

  const scheduledMail = await findLatestMailpitMessage(email, "deletion is scheduled");
  assert(scheduledMail, "email.deletion_scheduled never arrived in Mailpit after request #1");

  // --- Grace-law drill (§2 Phase L): the RIGHTS SET stays reachable; everything else denies as absence. ---
  const { status: graceGetMeStatus, body: graceGetMeBody } = await getJson(baseUrl, "/v1/me", headers);
  assert(graceGetMeStatus === 200, `GET /v1/me during grace -> HTTP ${graceGetMeStatus}, expected 200 (me.read survives grace)`);
  assert(typeof graceGetMeBody.deletionScheduledFor === "string", `GET /v1/me during grace missing deletionScheduledFor: ${JSON.stringify(graceGetMeBody)}`);

  const graceExportDownloadRes = await fetch(new URL(`/v1/me/export/${preDeletionExportId}/download`, baseUrl), { headers });
  assert(graceExportDownloadRes.status === 200, `export download during grace -> HTTP ${graceExportDownloadRes.status}, expected 200 (export survives grace, §2)`);

  const gracePatchRes = await fetch(new URL("/v1/me", baseUrl), {
    method: "PATCH",
    headers: { "content-type": "application/json", ...headers },
    body: JSON.stringify({ locale: "en" }),
  });
  assert(gracePatchRes.status === 404, `PATCH /v1/me during grace -> HTTP ${gracePatchRes.status}, expected 404 (identity.settings.update denies outside active/suspended as absence, §3b)`);

  // --- Cancel: the ONE deleted->active transition, grace-only. ---
  const cancelRes = await fetch(new URL("/v1/me/deletion", baseUrl), { method: "DELETE", headers });
  assert(cancelRes.status === 204, `DELETE /v1/me/deletion (cancel) -> HTTP ${cancelRes.status}, expected 204`);

  const { status: postCancelStatusHttp, body: postCancelStatusBody } = await getJson(baseUrl, "/v1/me/deletion", headers);
  assert(postCancelStatusHttp === 200 && postCancelStatusBody.state === "canceled", `GET /v1/me/deletion after cancel -> ${JSON.stringify(postCancelStatusBody)}, expected state:"canceled"`);

  const { status: postCancelMeStatus, body: postCancelMeBody } = await getJson(baseUrl, "/v1/me", headers);
  assert(postCancelMeStatus === 200, `GET /v1/me after cancel -> HTTP ${postCancelMeStatus}, expected 200 (works normally again)`);
  assert(postCancelMeBody.deletionScheduledFor == null, `GET /v1/me after cancel still carries deletionScheduledFor: ${JSON.stringify(postCancelMeBody)}`);

  const postCancelPatchRes = await fetch(new URL("/v1/me", baseUrl), {
    method: "PATCH",
    headers: { "content-type": "application/json", ...headers },
    body: JSON.stringify({ locale: "en" }),
  });
  assert(postCancelPatchRes.status === 200, `PATCH /v1/me after cancel -> HTTP ${postCancelPatchRes.status}, expected 200 (active again, settings.update works normally)`);

  // --- Phase L, request #2: flip to grace_days=0 (bounds-legal, [0,30]) so THIS request's effective_at
  // is due immediately, then re-request and let the DevSeams sweep trigger run Phase P live. ---
  await devSeamsSetGraceDays(baseUrl, 0);
  const purgeWindowStart = new Date().toISOString();

  const { status: post2Status, body: post2Body } = await postJson(baseUrl, "/v1/me/deletion", {}, headers);
  assert(post2Status === 200, `POST /v1/me/deletion (request #2, grace_days=0) -> HTTP ${post2Status}: ${JSON.stringify(post2Body)}`);

  // Captured BEFORE the sweep — Phase P pseudonymizes deletion_jobs.account_id in the SAME pass.
  const deletionId = await latestDeletionJobId(accountId);

  const processed = await devSeamsTriggerSweep(baseUrl);
  assert(processed >= 1, `DevSeams deletion-sweep processed ${processed} job(s), expected >= 1 (request #2's job should have been immediately due)`);

  const completedMail = await findLatestMailpitMessage(email, "has been deleted");
  assert(completedMail, "email.deletion_completed never arrived in Mailpit after the sweep");

  // --- Old access token is dead (session revoked as part of Phase P step 4). ---
  const { status: postPurgeMeStatus } = await getJson(baseUrl, "/v1/me", headers);
  assert(postPurgeMeStatus === 404, `GET /v1/me with the purged account's access token -> HTTP ${postPurgeMeStatus}, expected 404 (absence)`);

  // --- Purge read-back: real psql reads against the live Postgres, never a stub (L30). ---
  const account = await accountRow(accountId);
  assert(account.accountState === "deleted", `post-purge accounts.account_state = "${account.accountState}", expected "deleted" (tombstoned rows stay pinned deleted, §2 step 6)`);
  assert(account.tombstoned === true, `post-purge accounts.tombstoned_at is NULL — the account was never physically purged`);
  assert(account.emailIsNull === true, `post-purge accounts.email is NOT NULL — PII column was not nulled at tombstone`);
  assert(account.handle !== originalHandle, `post-purge accounts.handle ("${account.handle}") still equals the pre-purge handle "${originalHandle}" — handle-to-retired-handles move never happened`);

  assert(await retiredHandleExists(originalHandle), `identity.retired_handles has no row for "${originalHandle}" (OQ-2)`);
  const { status: availAfterStatus, body: availAfterBody } = await getJson(baseUrl, `/v1/signup/handle-availability?handle=${originalHandle}`);
  assert(availAfterStatus === 200 && availAfterBody.available === false, `GET handle-availability for the retired handle "${originalHandle}" -> ${JSON.stringify(availAfterBody)}, expected available:false — a retired handle must render identically to reserved/taken`);

  assert((await countSessionsForAccount(accountId)) === 0, `identity.sessions still has row(s) for account ${accountId} after purge`);
  assert((await countDevicesForAccount(accountId)) === 0, `identity.devices still has row(s) for account ${accountId} after purge`);
  assert((await countEmailChallengesForEmail(email)) === 0, `identity.email_challenges still has row(s) for "${email}" after purge (keyed-by-email purge-reaches-it, the S2 invocation-id scar)`);
  assert((await countConsentCurrentForAccount(accountId)) === 0, `identity.consent_current still has row(s) for account ${accountId} after purge`);
  assert((await countEventsConsentForStreamId(accountId)) === 0, `core.events_consent still has row(s) keyed by the RAW account id ${accountId} after purge — Pseudonymize must re-key stream_id, not just actor_ref`);
  assert((await countExportJobsForAccount(accountId)) === 0, `identity.export_jobs still has row(s) (incl. the artifact bytea) for account ${accountId} after purge`);

  // The deletion_jobs receipt SURVIVES (pseudonymized subject, §6a) — read back by immutable PK.
  const job = await deletionJobById(deletionId);
  assert(job.state === "complete", `identity.deletion_jobs[${deletionId}].state = "${job.state}", expected "complete"`);
  assert(job.executed === true, `identity.deletion_jobs[${deletionId}].executed_at is NULL, expected a real completion timestamp`);
  assert(job.purgeRunIdsJson.includes("events_heatmap_provenance"), `identity.deletion_jobs[${deletionId}].purge_run_ids does not mention "events_heatmap_provenance" — the full-history purge verb's registry entry was never reached: ${job.purgeRunIdsJson}`);

  // The SAME fact, independently confirmed against the actual receipt table (core.purge_runs), never
  // just the deletion_jobs row's own JSON copy of it — this is THE events_heatmap_provenance full-history
  // purge verb's first real invocation (SLICE_S3_CONTRACT.md §2 step 5 / the ledger row's own anchor).
  assert(
    await purgeRunReceiptExists("events_heatmap_provenance", purgeWindowStart),
    `core.purge_runs has no row for store_key="events_heatmap_provenance" started at/after ${purgeWindowStart} — the purge orchestrator never reached the heatmap-provenance registry entry`
  );

  // The freed email is genuinely re-registrable, not merely NULLed on the dead row (SLICE_S3_CONTRACT.md
  // §2: "email freed by the partial index") — a fresh signup attempt gets a REAL code, never the
  // already-registered anti-enumeration branch this exact address used to trigger pre-purge.
  const { status: reissueStatus, body: reissueBody } = await postJson(baseUrl, "/v1/signup/email-verification", { email, locale: "en" });
  assert(reissueStatus === 202, `POST email-verification for the freed email -> HTTP ${reissueStatus}: ${JSON.stringify(reissueBody)}`);
  const freshCodeMail = await findLatestMailpitMessage(email, "verification code");
  assert(freshCodeMail, `the freed email "${email}" did not receive a fresh email.verify_code mail — it is still being treated as already-registered`);

  // Leave the shared founder-scope 9A row exactly as this leg found it (manifest v0=14d) — the same
  // discipline DeletionPipelineTests.cs's SetGraceDays helper applies, so a second run of this SAME
  // script against a long-lived compose stack starts its own cancel drill with a real grace window too.
  await devSeamsSetGraceDays(baseUrl, 14);
}

// ---------- runner ----------

async function main() {
  if (!TARGET) {
    console.log(
      "identity e2e SKIP: IDENTITY_E2E_TARGET not set — no public host running (guarded until compose is up). This is a documented skip, not a pass."
    );
    return;
  }

  console.log(`identity e2e: probing ${TARGET}`);
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

  await run("GET /health", () => stageHealth(TARGET));
  await run("GET /v1/signup/handle-availability (fresh handle)", () => stageHandleAvailability(TARGET));

  await run("18th-birthday-today boundary vector is accepted", () => stageEighteenthBirthdayBoundary(TARGET));

  const journeyEmail = uniqueEmail("journey");
  const adultBirthdate = "2000-06-15";
  let journey;
  await run("signup: email-verification -> confirm -> complete", async () => {
    journey = await stageSignupThroughSession(TARGET, journeyEmail, adultBirthdate);
  });

  if (journey) {
    await run("GET /v1/me works with the minted session", () =>
      stageMeWorks(TARGET, journey.session.accessToken, journey.handle, journeyEmail)
    );

    await run("refresh rotation + reuse alarm (family revoked, audit event read back, security mail sent)", () =>
      stageRefreshRotationAndReuseAlarm(TARGET, journey.session, journey.session.accountId, journeyEmail)
    );

    // The reuse-alarm drill above revokes journey.session's ENTIRE family (the theft alarm is
    // "the whole session dies", §1b) — every /v1/me/* stage from here needs a FRESH session, exactly
    // what a real client would do after that revoke: log back in via the email-code door.
    let meSession;
    await run("re-login after the reuse-alarm revoke (fresh session for the /v1/me/* stages)", async () => {
      meSession = await stageLogin(TARGET, journeyEmail);
    });

    if (meSession) {
      await run("device register + push-consent write, read back off events_consent", () =>
        stageDeviceAndPushConsent(TARGET, meSession.accessToken, meSession.accountId)
      );

      await run("category-8 drill: PUT /v1/me/push-consents/8 wire-identical to /17", () =>
        stageCategoryEightDrill(TARGET, meSession.accessToken)
      );

      let currentHandle = journey.handle;
      await run("handle change, then immediate second change denied as LimitReached", async () => {
        currentHandle = await stageHandleChangeCooldown(TARGET, meSession.accessToken);
      });

      let attackerSession;
      await run("second account signup (IDOR drill attacker)", async () => {
        const attackerJourney = await stageSignupThroughSession(TARGET, uniqueEmail("attacker"), "1999-03-03");
        attackerSession = attackerJourney.session;
      });

      if (attackerSession) {
        await run("IDOR drill: attacker DELETEs the victim's sessionId, byte-identical to a random id, victim session survives", () =>
          stageIdorSessionDrill(TARGET, meSession.accessToken, attackerSession.accessToken)
        );
      } else {
        failures.push("attacker account never signed up — IDOR drill skipped");
      }

      let exportId;
      await run("export: POST /v1/me/export -> worker runs -> email.export_ready in Mailpit -> ready -> download the zip (per-store files + manifest, incl. consent + ledger contributions)", async () => {
        exportId = await stageExportLeg(TARGET, meSession.accessToken, journeyEmail);
      });

      if (exportId && attackerSession) {
        await run("export IDOR drill: a second account's exportId -> absence byte-identical to a random id (status + download)", () =>
          stageExportIdorDrill(TARGET, exportId, attackerSession.accessToken)
        );
      } else {
        failures.push("export leg or attacker account missing — export IDOR drill skipped");
      }

      let changedEmail;
      await run("email change: confirm swaps the email, old address gets email.email_changed_notice in Mailpit", async () => {
        changedEmail = await stageEmailChangeDrill(TARGET, meSession.accessToken, journeyEmail);
      });

      await run("logout the /v1/me/* session; GET /v1/me then renders absence", async () => {
        const { status: logoutStatus } = await postJson(TARGET, "/v1/auth/logout", {}, { authorization: `Bearer ${meSession.accessToken}` });
        assert(logoutStatus === 204, `POST auth/logout -> HTTP ${logoutStatus}, expected 204`);
        const { status: postLogoutStatus } = await getJson(TARGET, "/v1/me", { authorization: `Bearer ${meSession.accessToken}` });
        assert(postLogoutStatus === 404, `GET /v1/me after logout -> HTTP ${postLogoutStatus}, expected 404 (absence)`);
      });

      await run("login via auth/email-code + auth/session (post email-change), then logout", () =>
        stageLoginJourney(TARGET, changedEmail ?? journeyEmail, currentHandle)
      );
    } else {
      failures.push("re-login after the reuse-alarm revoke failed — /v1/me/* stages skipped");
    }
  } else {
    failures.push("signup journey never completed — dependent stages skipped");
  }

  await run("under-18 + under-13 minor-protection drills (wire-identical, zero persistence, under-13 hard delete)", () =>
    stageMinorProtectionDrills(TARGET)
  );

  await run(
    "deletion/purge: request (real grace) -> email.deletion_scheduled -> grace-law drill -> cancel -> works normally -> re-request (grace_days=0) -> DevSeams sweep -> email.deletion_completed -> purge read-back incl. events_heatmap_provenance receipt -> old token dies",
    () => stageDeletionLeg(TARGET)
  );

  if (failures.length > 0) {
    console.error("identity e2e FAILED:");
    for (const f of failures) console.error(`  - ${f}`);
    process.exitCode = 1;
    return;
  }
  console.log("identity e2e OK: signup, verification, session, refresh rotation, login, logout, export, minor-protection drills, and the deletion/purge leg all green live.");
}

const isMain = process.argv[1] && import.meta.url === `file://${process.argv[1]}`;
if (isMain) {
  main();
}

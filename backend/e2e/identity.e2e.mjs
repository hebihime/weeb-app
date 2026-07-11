#!/usr/bin/env node
// backend/e2e/identity.e2e.mjs — SLICE_S3_CONTRACT.md §10.3, S3 BUILD phase (signup/* + auth/* +
// minimal GET /v1/me — the /v1/me/* management surface and export/deletion are the NEXT pass, per the
// build's own DO-NOT list).
//
// Live E2E against the compose stack's Svac.PublicApi host — REAL HTTP endpoints, REAL Mailpit (REST
// oracle at :8025, never SMTP-side inspection), REAL 3A events read back via a real `docker compose exec
// postgres psql` probe (the same sanctioned pattern backend/e2e/substrate.e2e.mjs already uses for its
// own behavioral-event read-back — a READ-only verification probe, never a fake of the mutation flow
// itself, which runs 100% through real HTTP). SQL/stub bypass of the FLOW is banned (L30); this script
// never does that — every account/session/challenge exists ONLY because a real HTTP call created it.
//
// Journey: handle-availability -> email-verification (code fetched from Mailpit) -> confirm -> complete
// (18th-birthday boundary vector) -> GET /v1/me works -> refresh rotation -> reuse drill (old refresh =>
// family revoked + audit event read back + email.sessions_revoked in Mailpit) -> old access token dies
// -> login via auth/email-code + auth/session -> GET /v1/me -> logout -> under-18 + under-13 refusal
// drills (wire byte-identical, DB row count unchanged, under-13 challenge row destroyed).
//
// Usage: IDENTITY_E2E_TARGET=http://localhost:8090 node backend/e2e/identity.e2e.mjs
//        (no IDENTITY_E2E_TARGET set -> SKIP, not a lie)
//        Requires a local `docker compose` with this repo's postgres + mailpit services up (the DB read-
//        back probes and the Mailpit REST calls both need them).

import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";
import { execFile } from "node:child_process";
import { promisify } from "node:util";

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

async function stageLoginJourney(baseUrl, email, handle) {
  const { status: codeStatus, body: codeBody } = await postJson(baseUrl, "/v1/auth/email-code", { email });
  assert(codeStatus === 202, `POST auth/email-code -> HTTP ${codeStatus}: ${JSON.stringify(codeBody)}`);
  assert(Object.keys(codeBody).length === 0, `POST auth/email-code body should be {} per §1c, got ${JSON.stringify(codeBody)}`);

  const mail = await findLatestMailpitMessage(email, "sign-in code");
  const code = await extractSixDigitCode(mail);

  const { status: sessionStatus, body: sessionBody } = await postJson(baseUrl, "/v1/auth/session", { email, code });
  assert(sessionStatus === 200, `POST auth/session -> HTTP ${sessionStatus}: ${JSON.stringify(sessionBody)}`);
  assert(typeof sessionBody.accessToken === "string" && sessionBody.accessToken.startsWith("sst_"));

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

    await run("login via auth/email-code + auth/session, then logout", () =>
      stageLoginJourney(TARGET, journeyEmail, journey.handle)
    );
  } else {
    failures.push("signup journey never completed — dependent stages skipped");
  }

  await run("under-18 + under-13 minor-protection drills (wire-identical, zero persistence, under-13 hard delete)", () =>
    stageMinorProtectionDrills(TARGET)
  );

  if (failures.length > 0) {
    console.error("identity e2e FAILED:");
    for (const f of failures) console.error(`  - ${f}`);
    process.exitCode = 1;
    return;
  }
  console.log("identity e2e OK: signup, verification, session, refresh rotation, login, logout, and minor-protection drills all green live.");
}

const isMain = process.argv[1] && import.meta.url === `file://${process.argv[1]}`;
if (isMain) {
  main();
}

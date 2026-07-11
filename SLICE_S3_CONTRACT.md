# SLICE S3 CONTRACT — identity-accounts (LOCKED)

**Gate:** G0 · **Actor:** user ("signs up, verifies email, holds sessions, exports, deletes") · **Kills:** none new · **Ledger outcome:** signup→verified→delete E2E green (BUILD.md §7 S3 row) · **Authoritative nouns:** profilemodel §1, §1b, §1c, §12, §13 · **Carried findings LANDING here:** Auth-F3 (S1→S2 Correction 2 → S3: the first client-reachable resource-scoped 4A slice) and SilentRej-L4 (first policy-gated consumer read — `GET /v1/me` is that read).

Synthesized from three proposals by the design judge. Per-conflict adoptions and the one ruling contradiction are recorded in §12. Three open questions remain (§11); everything else is resolved with stated reasoning.

**Governing theses (grafted):**

- **From P1 (simplicity):** the email address IS the credential and the account IS the resource. Passwordless email-code auth, opaque server-side tokens, every consumer route anonymous-or-self (`/v1/me/*` — no account id in any consumer URL), no JWTs, no Redis, no blob store, no password column anywhere. The statutory pipeline is the load-bearing half of the slice and ships full-strength.
- **From P2 (extraction-grade seams):** S3 is the template every later PII module copies. It ships four reusable machines later slices join by REGISTRATION, never by editing S3: the resource-target 4A chokepoint (boot-refused when target conveyance is missing), the DSR registry pair (export mirrors purge, CI-gated), bearer auth as a Hosting seam (one chokepoint, three credential systems by design), and `identity.account_state_changed` on the 3A substrate as the cascade bus (the §1c table becomes a conformance checklist implemented as projections).
- **From P3 (privacy/residency/i18n/minor-protection):** every PII byte S3 creates is, from row one, simultaneously region+lawful-basis stamped, export-enumerable, and purge-destroyable — cross-checked by a build-failing export⋈purge gate so the three can never drift. Minor protection is server-authoritative with zero storage of refused minors' data and zero observable difference on the wire; birthdate has no table it could land in before the adult-proven atomic write.

---

## 0. Scope ruling

**S3 owns `backend/modules/identity/**`** — the third occupant of `backend/modules/`, copying S2's module template exactly (`Svac.Identity.Contracts` public + `Svac.Identity` internal + `backend/tests/Svac.Tests.Identity` + `backend/modules/identity/config/identity.config.json` + `backend/e2e/identity.e2e.mjs`). It additionally owns **narrow, versioned, additive mutations of DONE units**, each run as a Phase-2a mutation per SLICE_PLAYBOOK (mutate → byte-identical-behavior proof for existing consumers → rerun S1/S2 suites + S1 E2E → then fan out):

1. **`Svac.DomainCore.Contracts.Policy` + `Svac.DomainCore.Hosting`** — the Auth-F3 chokepoint redesign (§3): `PolicyTargetBinding`, the `TargetRule` table column, `IResourceOwnershipResolver`, extended boot refusal, the `accountState` actor axis, and the `IBearerAuthenticator` Hosting seam (default: anonymous — S1/S2 behavior byte-identical).
2. **`Svac.DomainCore.Contracts`** — three additive seam families: `IEmailSender` (the S4-subsumable email door; `IPaymentService` precedent — buy-side seams live here, never in a pre-created S4 module), `IExportContributor`/`IExportSink` + the export registry + its CI cross-gate (§6c — statutory machinery every module registers into, exactly like 13A; it must live where a module can register without referencing identity), and `IConsentLedgerWriter` + the closed `ConsentKind` enum (the consent stream is S1 substrate; its typed write door is substrate plumbing S17 builds cards ON).
3. **`Svac.DomainCore.Deterministic`** — `AgeMath` + `HandleRules`, pure, IO-free (existing arch test), golden-vectored, with the vector file shared verbatim with the client libs (§1g).
4. **`contracts/openapi.v0.json` — the FIRST real path delta** (§1c) and `contracts/message-keys.json` additions ×4 locales.

**S3 owns (per the ledger row + BUILD.md §4 A2):** account creation to the profilemodel §13 signup minimum (handle, verified email, birthdate attest, online-avatar-OR-skip, one fandom tag); email verification; passwordless email-code login; opaque revocable sessions with refresh rotation + family reuse detection; devices + push-token rows and per-category push-consent PLUMBING (delivery is S4); handle change (cooldown + reserved list + history); account settings (locale, email change); the `account_state` machine + the §1c cascade engine's event spine; the data EXPORT pipeline (statutory scope, registry-backed); the DELETION pipeline (export-offered-first, grace, tombstones, crypto-shred, full-history heatmap purge via S1's existing registration, custody-hold override consulted from day one); consent-ledger plumbing (write door + rebuildable projection; S3's REAL writers: age attestation, ToS acceptance, push categories); the server-authoritative 18+ floor and under-13 COPPA hard floor.

**S3 does NOT own:** the full profile — photos, display_name, pronouns, tags beyond the one signup tag, special-category fields, privacy-matrix DTOs (S10); the 5 consent CARDS + revocation cascade suite (S17 — S3 ships the write path and projection they build on); notification DELIVERY — APNs/FCM/email spine, inbox, quiet hours (S4 — S3 emits the 3A events now and sends its own gate-critical transactional mail through the seam); verification photo/ID/L1–L4 (S18); suspend/ban ACTIONS as operator surfaces (S12 — the state MACHINE, its 4A rows, and its cascade dispatch are S3's; S12 calls into them); phone (optional per §1b; no SMS vendor ratified — absent with reason); matching (S14/S19); MFA (consumer identity has none by design; staff Entra is S5, partner is S29); **client wiring** (the ledger row owns `backend/modules/identity` only — see the client-seam callout in §1d).

**Two deliberate darkness rulings (real-or-honestly-dark, L6):**
- **Avatar upload is DARK until S10/S11 (unanimous across proposals).** An online avatar is a published image; B10's own law (nothing publishes unscanned; PhotoDNA + Content Safety land at S11) applies to it exactly as to gallery posts, and a second media-ingest path inside identity violates 1A/L22. The §13 minimum is "online avatar OR skip" — skip IS a sanctioned outcome; the signup journey completes via the skip path; `avatar_ref` ships as a NULL column with no endpoint. This deliberately supersedes SLICE_S7_CONTRACT's "picker affordance whose flow lands with S3" note: the flow lands at S10/S11 as ONE additive contract change (endpoint + scan gate together), recorded here so it never resurfaces as drift.
- **The signup fandom tag is FREE TEXT at S3** (charset/length-validated, stored on the account row, display-inert). The canonical taxonomy + Content Desk queue are S10's; only canonical tags ever drive matching (profilemodel §4). S10 imports it through `IAccountDirectory`, never a cross-module join.

**The one structural law S3 makes true:** *after S3, no client-reachable resource-scoped action can execute without the 4A chokepoint proving the target belongs to the caller (a policy row demanding a target whose endpoint cannot convey one refuses to BOOT), and no PII store can exist that the export registry cannot enumerate and the purge registry cannot destroy — all enforced by build-failing tests.*

**Design artifacts consumed as constraints:** design/05 signup states (built at S7; the wire shapes line up per §1d); design/06 settings/deletion/export state catalogs; neutral-plain register on every refusal; absence-not-disablement (category-8 push consent is ABSENT from the mutable set, not a locked toggle); one limit surface (cooldown + quota denials all render S1's `LimitReached` component); silent rejection (cross-user probes wire-identical to nonexistence).

## 1. Module API surface + OpenAPI delta

### 1a. Assemblies

- **`Svac.Identity.Contracts`** (public; references only `Svac.DomainCore.Contracts`) — the ONLY assembly later modules reference. Carries: `IAccountLifecycle` (the state machine S12/S18 call), `IAccountDirectory` (the read-composition seam: `GetState(accountId)`, `GetHandle(accountId)`, `GetAgeYears(accountId)` — S10's privacy matrix consumes this, never identity's tables), the frozen `AccountStateChanged` event envelope, `AccountStateCascadeMatrix` (the §1c table transcribed as checked-in code — one cell = `{state, surface, behavior, owningSlice}`; later modules' projections reference the cell they implement), DTOs, typed ids (`usr_` existing; new: `ses_`, `dev_`, `chl_`, `exp_`, `del_`).
- **`Svac.Identity`** (internal; `InternalsVisibleTo` tests) — `IdentityDbContext` (schema `identity` — the first module-owned schema; sets the 1A precedent that `core` is not a dumping ground), endpoint handlers, session/challenge services, export orchestrator + Postgres artifact store, deletion worker, ownership resolvers, the session-backed `IBearerAuthenticator`.
- **Phase-2a domain-core delta** — enumerated in §0; additive; existing S1/S2 tests byte-identical before builders fan out.
- **`backend/tests/Svac.Tests.Identity`** (gate lane, deterministic, Testcontainers where DB-bound, <2s) + **`backend/e2e/identity.e2e.mjs`**.
- **Evals: N/A with reason** — zero latent surface. Signup, age math, sessions, export, deletion are 100% deterministic space (CLAUDE.md split rule; BUILD.md §0 enumerates the product's latent surfaces and none is here). The quality story is golden vectors + adversarial lens tests + the live E2E. Stated, not skipped.

### 1b. Auth model (tensions 1 + 2 — RESOLVED)

**Sessions: opaque, server-side, DB-backed, revocable — NOT stateless JWT (unanimous).** Judged against the stated criteria: (a) **instant revocation on ban/delete (§1c)** is an indexed UPDATE in the same tx as the state change — a JWT denylist recreates the per-call DB read JWT exists to avoid, plus a second moving part; (b) **zero client-encoded trust (L20):** an opaque token carries nothing to decode, forge, or someday wrongly trust (`age_verified` in a JWT claim is a standing invitation); (c) we are a modular monolith with one Postgres (1A) — JWT's real win, distributed validation without a shared store, is a problem we structurally do not have; (d) device binding and the settings-surface session list are native to a session table. Cost: one PK lookup per authenticated request against Postgres — correct at this scale; the promotion path (read-through cache behind the session validator) is one DI swap, and S1 already ruled Redis idle until a hot path is proven.

**Shape (P2/P3 over P1 — see §12 item 2):** access token = 256-bit random, prefix `sst_` (greppable in logs/dumps), stored SHA-256 only, TTL `identity.session.access_ttl_minutes` (v0 60) · refresh token = 256-bit random, prefix `srt_`, single-use, **rotated on every refresh, family-linked (`refresh_family_id`); presenting an already-consumed refresh token revokes the entire family + session, appends a 3A audit event, and queues the category-8 security notice (§7)** — rotation converts token theft from an invisible condition into a detectable event, which no amount of server-side revocability provides on its own. Refresh TTL `identity.session.refresh_ttl_days` (v0 90). Header only (`Authorization: Bearer sst_…`); never cookie/query at S3 (the web funnel's cookie story is S9's, behind the same session store). Active-session cap `identity.session.max_active_per_account` (oldest revoked past cap).

**Credential model: passwordless email one-time code (unanimous; OQ-1 ratifies).** The §13 signup minimum contains NO password noun; §1b names verified email as the anchor; the web spec says "email-based". One challenge machine (6-digit code, HMAC'd at rest, TTL'd, attempt-capped, single-use) serves signup verification, login, and email change. No password column exists anywhere — the credential-stuffing/reset/breach class is structurally absent. Passkeys (WebAuthn) are the named additive upgrade; adding any credential later is additive (nothing to migrate FROM is the point).

**Bearer resolution (Hosting seam):** `IBearerAuthenticator` in `Svac.DomainCore.Hosting`; default registration = anonymous-only (S1/S2 behavior unchanged, byte-identical proof). Identity registers the session-backed resolver: token hash → live session row + account join (state, region, locale) → `ActorRef(usr_…, User)` + `accountState` into `RequestContext`. Revoked/expired/unknown tokens resolve to the ANONYMOUS actor (no 401 oracle at middleware); endpoints requiring User then deny per policy as absence. The admin host (S5, Entra) and partner host (S29) later mount the SAME chokepoint with different resolvers — one middleware, three credential systems, by design (§5 cross-cutting row).

**Email door (tension 1 — seam-now, S4 subsumes):**

```
// Svac.DomainCore.Contracts (IPaymentService family — buy-side seams NEVER live in a
// pre-created future module; S1 §12.1 precedent)
IEmailSender { Task<EmailResult> SendAsync(EmailMessage msg, RequestContext ctx, CancellationToken ct) }
EmailMessage { To, TemplateKey, Locale, Model: IReadOnlyDictionary<string,string> }  // keyed ×4, NEVER prose
EmailResult = Sent(providerRef) | Failed(reason)                                     // closed union
```

Impl now: `SmtpEmailSender` over compose Mailpit (`localhost:1025`) — a REAL SMTP transport, not a DevSeam (Mailpit is just the dev server behind a real transport); the same impl points at any prod relay by config. **Prod boot with no configured SMTP throws at startup** (L18 family, arch-tested like IPaymentService). Transport selection is environment/config, **never a 9A entry** (S1 DevSeams ruling analog — the ops desk must not be able to repoint verification email). S4 subsumes by ONE DI re-registration (the notifications module's delivery-tracked impl); the interface, template keys, and call sites survive verbatim — that is the definition of seam-now.

**Consent plumbing:**

```
// Svac.DomainCore.Contracts (S17's module and S4 both register/consume without referencing identity)
ConsentKind (CLOSED): AgeAttestation18Plus | TermsAcceptance | PushCategory(1..7, 9)
                    | IrlAccess | BackgroundLocation | SpecialCategoryIdentity
                    | IdentityVerification | Marketing          // declared, ZERO writers until S17/S4
IConsentLedgerWriter { Record(subject, ConsentKind, version, surface, Decision: Granted|Revoked, ctx) }
```

Appends ONE `consent.recorded` event to `events_consent` in the CALLER'S tx; payload frozen `{consent_key, version, decision, surface}` + region/lawful_basis stamped by the substrate. **`PushCategory(8)` does not exist as a ConsentKind — "never mutable" is unrepresentable in the type, not a check.** S3's real writers: `AgeAttestation18Plus` + `TermsAcceptance` in the signup-complete tx; `PushCategory` on every push-consent change. Marketing is declared-UNWRITTEN at S3 (§12 item 12 — DR-7.1 rules; no marketing opt-in exists at signup; nothing may honor a flag no surface writes). `identity.consent_current` is the rebuildable projection — identity's first 3A stream consumer, shipping the §8-clause-7 foreign-event skip test non-vacuously.

**Lifecycle machine:**

```
// Svac.Identity.Contracts
IAccountLifecycle {
  Suspend(accountId, reasonKey, ctx) · Reinstate(accountId, ctx) · Ban(accountId, reasonKey, ctx)
  RequestDeletion(accountId, ctx) · CancelDeletion(accountId, ctx)
  // CLOSED transition table: active↔suspended · active|suspended→banned · banned→active (reinstate)
  // active|suspended→deleted ONLY via RequestDeletion (a direct Transition(deleted) throws — erasure has
  // exactly one door) · deleted→active ONLY via CancelDeletion inside the grace window · post-purge
  // deleted is terminal. Every transition: 4A-gated (§3), appends identity.account_state_changed to the
  // AUDIT stream in the SAME tx (frozen envelope: {account_id, from, to, reason_key, effective_at}),
  // the single publication point every later module's §1c cascade projection keys on.
}
```

Identity's OWN cascade rows (the only ones whose stores exist): **suspended** — sessions REMAIN valid; feature surfaces deny later via the `accountState` policy axis; export/deletion/logout stay reachable (GDPR rights survive suspension). **banned** — all sessions + refresh families revoked in-tx; device push tokens cleared; email-code issuance silently refused (wire-uniform 202 — a banned probe is indistinguishable from a nonexistent email). **deleted** — §2 deletion pipeline. Later modules implement their §1c cells as projections against the event (matches hide at S14, DMs pause at S13, …), each referencing its `AccountStateCascadeMatrix` cell; the §8-clause-7 foreign-event skip harness ships with the first external consumer (S4).

### 1c. OpenAPI delta (the FIRST real paths; drift-gated regen; `securitySchemes.bearer` semantics filled: opaque `sst_` token, header only)

**Flow shape (§12 item 1): email is verified FIRST; the account is created by ONE atomic `complete`.** No account row, no birthdate, no PII persists before the email is proven owned and the birthdate is proven adult — `identity.email_challenges` has NO birthdate column BY DESIGN; there is no table a refused minor's birthdate could land in. All account-scoped routes are **`/v1/me/*` — no account id appears in ANY consumer route or body**, so account-level IDOR has no input to tamper with (defense in depth with §3). All request DTOs trust-field-free (arch scan extended: `account_state|email_verified|attested|deletion_|session_`; plus a RESPONSE-graph scan for `birthdate|dob`). Anonymous mutation endpoints additionally sit behind a host-level per-IP fixed-window rate limiter (vanilla ASP.NET `RateLimiter` — transport abuse control, NOT 10A; stated so nobody builds a second quota system).

| Route | Verb | Auth | Req → Resp | WHY |
|---|---|---|---|---|
| `/v1/signup/handle-availability` | GET `?handle=` | anon | → `{available: bool}` | S7 gateway member 1; reserved, retired, and taken all render IDENTICALLY `false` (no reserved-list, no deleted-account oracle) |
| `/v1/signup/email-verification` | POST | anon | `{email, locale}` → 202 `{challengeId}` | gateway member 2; sends the code via `IEmailSender`. **Anti-enumeration:** an already-registered email gets a byte-identical 202 and a "you already have an account" MAIL instead of a code — the wire never confirms account existence |
| `/v1/signup/email-verification/confirm` | POST | anon | `{challengeId, code}` → 200 `{verifiedToken}` | instant code feedback at the code-entry step (design/05 state); `verifiedToken` single-use, hash-stored, TTL `identity.signup.verified_token_ttl_minutes`; invalid/expired/exhausted = ONE `auth.code_invalid` Problem |
| `/v1/signup/complete` | POST | anon | `{verifiedToken, handle, birthdate, fandomTag, locale}` → 201 `SessionCreated` | THE atomic write: AgeMath oracle FIRST (§1g) → handle unique (catch 23505, re-read winner) → account row + AgeAttestation/ToS consent events + `identity.account_created` audit event + behavioral event + first session, ONE tx. Idempotent under race by verifiedToken single-consumption (replay → the winner's session). No second login round trip |
| `/v1/auth/email-code` | POST | anon | `{email}` → 202 `{}` | login step 1; same challenge machine, purpose=login; uniform 202 + normalized timing whether the account exists, is banned, or is absent (code mail sends only for a live account) |
| `/v1/auth/session` | POST | anon | `{email, code}` → 200 `SessionCreated` | login step 2 (single step — no verifiedToken needed when no other fields follow). Deleted-in-grace account → 200 into the rights-restricted state (§2 grace law); `GET /v1/me` then carries `deletionScheduledFor` and design/06 renders the restore/cancel surface |
| `/v1/auth/refresh` | POST | anon | `{refreshToken}` → 200 `SessionCreated` | rotation; reuse ⇒ family revoked + audit + generic Problem (a thief learns nothing) |
| `/v1/auth/logout` | POST | user | `{}` → 204 | revokes the presenting session + clears its device's push token (§1c row "devices/push: delete on logout") |
| `/v1/me` | GET | user | → `AccountSelf` | **the first policy-gated consumer read (SilentRej-L4 lands here, §3).** `{accountId, handle, email, ageYears (derived — birthdate NEVER in any response, arch-tested), locale, fandomTag, createdAt, deletionScheduledFor?}` — no state flags beyond the deletion surface |
| `/v1/me` | PATCH | user | `{locale?}` → 200 | locale ∈ `i18n/locales.json` ×4 |
| `/v1/me/handle` | POST | user | `{handle}` → 200 · 429 `LimitReached` · 422 | cooldown renders THE one limit surface (`resetsAt` = last change + cooldown, `premiumExtends: false`); reserved/retired/taken = one `handle.taken` |
| `/v1/me/email` | PUT | user | `{email}` → 202 `{challengeId}` | email change via the same challenge machine (code to the NEW address) |
| `/v1/me/email/confirm` | POST | user | `{challengeId, code}` → 200 | swaps email; the OLD address gets the security notice (§7 — the account-takeover tripwire in a passwordless system) |
| `/v1/me/sessions` | GET | user | → `[SessionSummary]` | design/06 surface: `{sessionId, platform?, createdAt, lastSeenAt, current}` — the honest theft lever |
| `/v1/me/sessions/{sessionId}` | DELETE | user | → 204 · 404-absence | **THE Auth-F3 exemplar route (§3):** target bound from route, owner-only; foreign ≡ nonexistent, byte-identical |
| `/v1/me/devices` | POST | user | `{platform, pushToken?}` → 201 `{deviceId}` | device + push-token STORE (delivery is S4); registration happens at the client's post-first-value opt-in moment (DR-7.1) — the API doesn't care when |
| `/v1/me/devices/{deviceId}` | DELETE | user | → 204 · 404-absence | resource-scoped like sessions |
| `/v1/me/push-consents` | GET | user | → rows for categories **1–7, 9** | account-level per-category consent plumbing; **category 8 is ABSENT from the readable/mutable set** (absence law) |
| `/v1/me/push-consents/{category}` | PUT | user | `{enabled}` → 204 · 404 | writes via `IConsentLedgerWriter` same-tx; **`PUT /…/8` is 404, wire-identical to `PUT /…/17`** — not a locked toggle, not a 403 |
| `/v1/me/export` | POST | user | `{}` → 202 `{exportId}` | statutory export; duplicate active request returns the SAME job (idempotent; single-active enforced by partial unique index) |
| `/v1/me/export/{exportId}` | GET | user | → `{state, expiresAt?}` | pending → ready(expiresAt) → delivered/expired/failed |
| `/v1/me/export/{exportId}/download` | GET | user | → zip stream | streams THROUGH the authed, policy-gated endpoint (no public SAS URL — no leaked-link class); download audited (3A) |
| `/v1/me/deletion` | POST | user | `{}` → 200 `{effectiveAt, exportOffered: true}` | **export-offered-first is IN the response contract** — no client can build the flow without rendering the offer; grace clock starts now |
| `/v1/me/deletion` | DELETE | user | → 204 | cancel during grace; idempotent under race (state-guarded UPDATE) |
| `/v1/me/deletion` | GET | user | → `{state, scheduledFor?}` | the design/06 "deletion scheduled" surface |

New components: `SessionCreated {accessToken, accessExpiresAt, refreshToken, accountId}`, `AccountSelf`, `SessionSummary`, `ExportStatus`, `DeletionStatus`. All ids `OpaqueId`-format. Reuse: `Problem`, `LimitReached`, `CursorPage`.

### 1d. Client-seam lineup (the "or you version the contract" callout — this IS a versioned contract change, documented)

The S7 `SignupGateway`'s five per-field verbs do NOT become five server round-trips — a five-write signup is a partial-account generator under any crash or race, and the server must never hold a birthdate before the atomic adult-proven write. Mapping: `checkHandleAvailability` → handle-availability GET · `sendEmailVerification` → email-verification POST (+ the confirm step — a code-entry sub-state design/05 already catalogs; S7 built no success path at all, so this is activation work, not breakage) · `recordBirthdateAttestation` → client-local staging · `uploadAvatar(nil)` → skip (dark until S10/S11) · `submitFandomTag` (the final S7 submit call) → the atomic `POST /v1/signup/complete` carrying the staged fields. This is the activation shape SLICE_S7_CONTRACT pre-approved ("contract regen + one new conforming type"); `SignupStepResult` gains its success case at that slice. **Client activation is NOT in S3's build scope** (the ledger row owns `backend/modules/identity` only); it is the named next client slice's first task, against this frozen contract. HARDENED-GATE clause 5 records N/A-with-note for client surfaces; the wire-level dignity/absence laws are asserted by tests instead (§10).

**New `message-keys.json` entries (each ×4 locales — EN/ES/PT/zh-Hans — i18n-lint armed; neutral-plain register):** server-emitted: `signup.refused_age_floor`, `handle.taken`, `handle.invalid`, `auth.code_invalid` (+ reuse of `limit_reached.generic`, `error.generic`). Email template keys: `email.verify_code.*`, `email.login_code.*`, `email.already_registered.*`, `email.email_changed_notice.*`, `email.export_ready.*`, `email.deletion_scheduled.*`, `email.deletion_completed.*`, `email.sessions_revoked.*` ({subject,body} families, server-rendered in recipient locale). Nothing else; every key has an emitter.

## 2. Schema DDL

Schema `identity`, owned solely by `IdentityDbContext`; **region + lawful_basis NOT NULL on every PII row** (L21); ddl-lint `pii-patterns.json` gains `identity.*`, red-fixture-proven.

```sql
CREATE SCHEMA identity;

CREATE TABLE identity.accounts (
  account_id            text PRIMARY KEY,                 -- usr_ ULID
  handle                text NOT NULL,                    -- canonical NFKC-folded lowercase (HandleRules)
  email                 text,                             -- PLAINTEXT BY RULING (§12 item 6): S1's CLOSED
                                                          --   IFieldEncryptor purpose enum (special_category,
                                                          --   birthdate, verification_audit) IS the ratified
                                                          --   tier; email is the login lookup + S5 audited
                                                          --   admin-search key. Upgrade path documented:
                                                          --   HMAC lookup column + ciphertext, one migration.
                                                          --   NULLed at tombstone.
  email_verified_at     timestamptz NOT NULL,             -- email-first flow: never NULL on a live row
  birthdate_enc         bytea NOT NULL,                   -- IFieldEncryptor purpose 'birthdate' (S1 enum,
                                                          --   per-subject key); NO age/year column exists;
                                                          --   age derives on read via AgeMath, never raw
  attested_adult_at     timestamptz NOT NULL,
  terms_version         text NOT NULL,
  fandom_tag            text NOT NULL,                    -- free text at S3; S10 imports via IAccountDirectory
                                                          --   (cross-boundary ref FLAGGED: no FK, no join)
  avatar_ref            text,                             -- NULL until S10/S11 (dark ruling, §0)
  locale                text NOT NULL,
  account_state         text NOT NULL DEFAULT 'active'
                        CHECK (account_state IN ('active','suspended','banned','deleted')),
  irl_access_state      text NOT NULL DEFAULT 'active'
                        CHECK (irl_access_state IN ('active','suspended')),  -- profilemodel §1 distinct axis;
                                                          --   writer-less at S3 (S12/S18 write it)
  state_changed_at      timestamptz NOT NULL,
  deletion_requested_at timestamptz,
  deletion_effective_at timestamptz,                      -- request + grace; worker key
  created_at            timestamptz NOT NULL,
  last_active_at        timestamptz NOT NULL,             -- write-throttled (≥1h delta); NEVER in a response
                                                          --   DTO at S3; coarse-bucket derivation is a
                                                          --   presentation lib at S10/S14 (display law)
  tombstoned_at         timestamptz,                      -- set only by the deletion pipeline
  region text NOT NULL, region_source text NOT NULL, lawful_basis text NOT NULL
);
CREATE UNIQUE INDEX ux_accounts_handle ON identity.accounts (handle) WHERE account_state <> 'deleted';
CREATE UNIQUE INDEX ux_accounts_email  ON identity.accounts (lower(email))
  WHERE account_state <> 'deleted' AND email IS NOT NULL;
-- Signup race: rely on the unique violation, catch 23505, re-read the winner (BUILD.md §9 check-then-act).
CREATE INDEX ix_accounts_deletion_due ON identity.accounts (deletion_effective_at)
  WHERE deletion_effective_at IS NOT NULL;

CREATE TABLE identity.email_challenges (                  -- ONE machine: signup | login | email_change.
  challenge_id  text PRIMARY KEY,                         -- chl_ ULID
  purpose       text NOT NULL CHECK (purpose IN ('signup','login','email_change')),
  email_lower   text NOT NULL,
  account_id    text,                                     -- only login/email_change
  code_hash     bytea NOT NULL,                           -- HMAC(IFieldKeyVault named secret, code) — keyed;
                                                          --   a 6-digit space must not be offline-brutable
  attempts      int NOT NULL DEFAULT 0,
  verified_at   timestamptz,                              -- signup confirm step
  verified_token_hash bytea,                              -- single-use complete ticket, hash only
  consumed_at   timestamptz,
  expires_at    timestamptz NOT NULL,
  created_at    timestamptz NOT NULL,
  locale text NOT NULL, region text NOT NULL, lawful_basis text NOT NULL
);  -- NO birthdate column BY DESIGN (minor no-store, §1g). Consumption = single guarded UPDATE
    -- (… WHERE consumed_at IS NULL AND attempts < max RETURNING) — idempotent under race.
    -- Issuing a new code voids prior unconsumed same-purpose rows (UPDATE, not DELETE).

CREATE TABLE identity.sessions (
  session_id        text PRIMARY KEY,                     -- ses_ ULID
  account_id        text NOT NULL REFERENCES identity.accounts(account_id),
  device_id         text,                                 -- nullable REFERENCES identity.devices
  access_token_hash bytea NOT NULL UNIQUE,                -- SHA-256; plaintext NEVER stored
  refresh_family_id text NOT NULL,
  created_at timestamptz NOT NULL, last_seen_at timestamptz NOT NULL,  -- write coalesced ≥60s
  access_expires_at timestamptz NOT NULL,
  revoked_at timestamptz,
  revoke_reason text,   -- logout|user_revoked|rotation_reuse|state_cascade|expired|cap_evicted
  region text NOT NULL, lawful_basis text NOT NULL
);  -- no IP column, no raw user-agent (data minimization; coarse platform lives on the device row)
CREATE INDEX ix_sessions_account ON identity.sessions (account_id) WHERE revoked_at IS NULL;

CREATE TABLE identity.refresh_tokens (
  id text PRIMARY KEY, session_id text NOT NULL REFERENCES identity.sessions(session_id),
  token_hash bytea NOT NULL UNIQUE, family_id text NOT NULL,
  issued_at timestamptz NOT NULL, expires_at timestamptz NOT NULL,
  consumed_at timestamptz, superseded_by text,            -- reuse detection: consumed presented again
                                                          --   ⇒ revoke family (single guarded UPDATE CAS)
  region text NOT NULL, lawful_basis text NOT NULL
);

CREATE TABLE identity.devices (
  device_id  text PRIMARY KEY,                            -- dev_ ULID, SERVER-minted; never a client
                                                          --   hardware id (S7 zero-device-identifier law)
  account_id text NOT NULL REFERENCES identity.accounts(account_id),
  platform   text NOT NULL CHECK (platform IN ('ios','android','web')),
  push_token text, push_token_updated_at timestamptz,     -- STORE only; delivery is S4
  created_at timestamptz NOT NULL, last_seen_at timestamptz NOT NULL, revoked_at timestamptz,
  region text NOT NULL, lawful_basis text NOT NULL
);

CREATE TABLE identity.push_category_consents (            -- PROJECTION of events_consent; rebuildable
  account_id text NOT NULL, category smallint NOT NULL
    CHECK (category BETWEEN 1 AND 9 AND category <> 8),   -- category 8 UNREPRESENTABLE, not just immutable
  enabled boolean NOT NULL, updated_at timestamptz NOT NULL,
  region text NOT NULL, lawful_basis text NOT NULL,
  PRIMARY KEY (account_id, category)
);

CREATE TABLE identity.consent_current (                   -- rebuildable projection over events_consent
  account_id text NOT NULL, consent_kind text NOT NULL, version text NOT NULL,
  status text NOT NULL CHECK (status IN ('granted','revoked')),
  surface text NOT NULL, decided_at timestamptz NOT NULL,
  region text NOT NULL, lawful_basis text NOT NULL,
  PRIMARY KEY (account_id, consent_kind)
);

CREATE TABLE identity.handle_history (                    -- moderation trail (profilemodel §1) + cooldown src
  id text PRIMARY KEY, account_id text NOT NULL REFERENCES identity.accounts(account_id),
  old_handle text NOT NULL, new_handle text NOT NULL, changed_at timestamptz NOT NULL,
  region text NOT NULL, lawful_basis text NOT NULL
);  -- NO consumer read path exists in the contract assembly (moderation-visible only, structural)

CREATE TABLE identity.reserved_handles (                  -- non-PII; seeded from a checked-in manifest
  handle text PRIMARY KEY, reason text NOT NULL           --   (brand/staff/impersonation terms: weeb, friki,
);                                                        --   admin, staff, mod, support, safety, official,
                                                          --   svac, …); desk-editable at S5. The top-1k
                                                          --   fandom half arrives WITH its source of truth
                                                          --   (S10 taxonomy) — recorded, not faked.

CREATE TABLE identity.retired_handles (                   -- handle afterlife (OQ-2); subject-SEVERED at
  handle text PRIMARY KEY, retired_at timestamptz NOT NULL  -- write (no account ref) — pseudonymous by
);                                                        --   construction; swept per retirement_days config

CREATE TABLE identity.export_jobs (
  export_id  text PRIMARY KEY,                            -- exp_ ULID
  account_id text NOT NULL REFERENCES identity.accounts(account_id),
  state      text NOT NULL CHECK (state IN ('pending','ready','delivered','expired','failed')),
  artifact   bytea,                                       -- Postgres bytea BY RULING (§12 item 5): an S3
                                                          --   export is kilobytes of JSON; module-internal
                                                          --   IExportArtifactStore makes blob promotion one
                                                          --   DI swap when S10-era exports grow media
  manifest   jsonb,                                       -- store keys + row counts contributed (the receipt)
  requested_at timestamptz NOT NULL, ready_at timestamptz, expires_at timestamptz,
  region text NOT NULL, lawful_basis text NOT NULL
);
CREATE UNIQUE INDEX ux_export_active ON identity.export_jobs (account_id)
  WHERE state IN ('pending','ready');                     -- single active job, race-proof by index

CREATE TABLE identity.deletion_jobs (
  deletion_id text PRIMARY KEY,                           -- del_ ULID
  account_id  text NOT NULL,                              -- pseudonymized post-run (purge_runs pattern)
  state text NOT NULL CHECK (state IN ('scheduled','canceled','executing','held','complete')),
  requested_at timestamptz NOT NULL, scheduled_for timestamptz NOT NULL,
  export_offered boolean NOT NULL DEFAULT true,
  custody_holds_found int, custody_hold_refs jsonb,       -- ER-14 answer recorded EVERY run, even when empty
  executed_at timestamptz, purge_run_ids jsonb,           -- the 13A receipts
  region text NOT NULL, lawful_basis text NOT NULL
);
```

**Cross-boundary refs, enumerated (zero cross-module joins exist):** `fandom_tag` (plain value; S10 import path via `IAccountDirectory`) · export read-path composes other modules' data ONLY through `IExportContributor` registrations (each module serializes its own stores; identity never queries another schema) · ledger balances in the export come from `ILedger.BalanceOf` (S1 contract) · `accounts.account_id` is the opaque `usr_` ref every later module stores WITHOUT an FK (1A) · consent/audit/behavioral rows live on S1's streams via S1 contracts.

**Encryption tier (PROF OQ5 — RESOLVED, §12 item 6):** field-encrypted = `birthdate` now (S1's existing purpose; per-subject key; crypto-shred on deletion), `special_category` at S10, `verification_audit` at S18 — exactly S1's closed purpose enum. Deliberately NOT field-encrypted: email (above), session/refresh tokens and codes (**hashed** — one-way beats reversible for secrets never read back), push tokens (opaque vendor-rotated addresses, 13A-deleted everywhere), handles (public by definition).

**The deletion pipeline (tension 5 — RESOLVED; two-phase, rights-preserving grace):**

- **Phase L (logical, at request, reversible):** `account_state → 'deleted'` immediately + the `account_state_changed` event in the same tx — the user DISAPPEARS from the product now (§1c presentation cascades are later modules' projections off this event), which is what they asked for. `deletion_effective_at = now + identity.deletion.grace_days` (founder config, v0 14, bounds [0,30] — 0 exists so the E2E executes live and a jurisdictional immediate-erasure variant works; 30 cap keeps the whole pipeline inside GDPR's one-month clock). **Sessions are NOT revoked and login stays open — but the `accountState=deleted` policy axis (§3) restricts the account to the RIGHTS SET: `me.read`, `export.*`, `deletion.read/cancel`, `sessions.read/revoke`, `logout`.** Cancel must be reachable and export must be obtainable WITHOUT canceling (a deletion flow that ransoms the export against the cancel is coercive); everything else denies as absence. The deletion-scheduled email (grace end date + cancel path + export offer) sends now; export is also offered in the response shape.
- **Phase P (physical, at `effective_at`, the worker):** (1) consult the 13A custody-hold registry (ER-14) and RECORD the answer on the job row even when empty — no holds can exist before S12; the check is structural now, red-fixtured with a test-registered hold; held stores are skipped with a documented-basis purge_run row while the REST proceeds; release re-enqueues the remainder. (2) If an export job is `pending`, finish it first (cap `identity.export.pre_deletion_wait_hours`). (3) Send the completion email — the LAST outbound act, before the shred (closure proof to a still-existing address; §12 item 9). (4) Revoke all sessions/refresh families; delete devices + push tokens; delete export jobs + artifacts. (5) ONE call — `IPurgePipeline.Run(account_deletion, subject)` — across the ENTIRE registry: identity stores per §6, S1 stores per their ratified verbs **including `events_heatmap_provenance` full-history (the profilemodel §1c founder ruling, registered at S1 — S3 is the FIRST caller of that verb; this is the ledger row's "full-history heatmap purge anchor")**, ledger refs tombstoned, consent/audit pseudonymize-tombstone per S1 OQ-1a, **crypto-shred of the subject's `birthdate` field key as the final verb**. (6) Tombstone: the accounts row survives as `{account_id, account_state='deleted', tombstoned_at}` with every PII column NULLed; handle moves to `identity.retired_handles` (OQ-2); email freed by the partial index. (7) `identity.deletion_completed` audit event + purge_run receipts on the job row. **Cancel** any time before `effective_at`: the ONE `deleted→active` transition, audited; post-purge, deleted is terminal. Statutory clock: `requested_at` + grace + wait caps ≤ 17 days worst case, inside Art. 12(3) with margin; the job row + purge_runs are the evidence chain, rendered as S5 desk tiles from data flowing now.

## 3. 4A policy entries + the Auth-F3 redesign (tension 3 — RESOLVED as a general mechanism)

### 3a. The chokepoint redesign (domain-core Phase-2a; additive; S1/S2 rows byte-identical via defaults)

```
// Endpoint side — how a mapping CONVEYS the target (CLOSED union):
PolicyTargetBinding: None                                  // S1/S2 behavior: TargetRef.ForAction(action)
                   | SelfAccount                           // TargetRef("account", actor.Id) — from the
                                                           //   SESSION, never from route/body/client input
                   | FromRoute(paramName, resourceType)    // real resource ids (sessionId, deviceId, exportId)

RequirePolicyAction(action)                        // existing signature = None (byte-identical)
RequirePolicyAction(action, PolicyTargetBinding)   // new overload

// Table side — what the row DEMANDS (closed): TargetRule: ActionScoped | SelfOnly | OwnedResource(type)
// SelfOnly: engine denies unless target.ResourceId == actor.Id.
// OwnedResource(t): engine calls the registered resolver:
IResourceOwnershipResolver { string ResourceType; Task<OpaqueId?> OwnerOf(string resourceId, ct) }
// identity registers: session, device, export (one indexed owner-column lookup each);
// unknown id ⇒ owner null ⇒ deny-as-absence — nonexistent and foreign are ONE branch.

// Actor axes gain accountState (resolved server-side from the session join, never client input) so
// rows can declare state conditions declaratively — S3's grace rights-set (§2) and every later
// slice's "suspended ⇒ deny" row are table data, not handler code.

// THE BOOT-REFUSAL LOCK (the extraction point, fail-closed BOTH directions, red-fixture-proven):
// an endpoint whose action's row declares SelfOnly/OwnedResource but whose mapping binds None — or a
// FromRoute naming a param absent from the route pattern — REFUSES TO BOOT; a None row given a route
// binding also refuses. A resource-scoped action without target conveyance is structurally
// unshippable, forever, for every future slice.
```

**Deny mode for ownership failure: `DenyAsAbsence`.** Ownership is folded into the QUERY PREDICATE (`WHERE id = @id AND account_id = @actor` — the resolver performs the same single fetch the handler would): not-found and not-owned are one query returning zero rows — one code path, one timing profile, by construction. **This structurally discharges SilentRej-L4 for these endpoints** (the constant-path denial S1 deferred is simply how the resolver is shaped); both S1 skip-annotated lens tests — `AuthIdorLensTests.PolicyChokepoint_MustConveyTheRealTargetResourceId` and `SilentRejectionTimingChannelLensTests.ExcludedRead_AndGenuineAbsentRead_TraverseTheSameCodePath` — UN-SKIP and go green at S3 (two deferred findings retired).

**Red fixtures (contract requirement):** (1) user A DELETEs user B's `sessionId` → 404 byte-identical to a random id, B's session provably still valid, zero side effects, deny audited; (2) the owning caller's revoke succeeds; (3) a fixture endpoint mapping a scoped row with binding `None` → boot refusal; (4) the reverse (None row + route binding) → boot refusal.

### 3b. The table (every S3 mutation + the first true read rows; consumer deny ⇒ absence coercion holds engine-wide; DenyStandard stays staff/system-only per S1)

| action | actors | TargetRule | accountState axis | denyMode | notes |
|---|---|---|---|---|---|
| `identity.signup.challenge` / `.confirm` / `.complete` | anonymous | ActionScoped | — | DenyAsAbsence | quota-guarded (§5) |
| `identity.auth.request_code` / `.create_session` / `.refresh` | anonymous | ActionScoped | — | DenyAsAbsence | uniform-failure laws (§1c) |
| `identity.auth.logout` | user | SelfOnly | any | DenyAsAbsence | rights set |
| `identity.me.read` | user | SelfOnly | any | DenyAsAbsence | **IsReadPath=true — the FIRST true read row; S1's read-path guard goes non-vacuous** |
| `identity.settings.update` | user | SelfOnly | active·suspended | DenyAsAbsence | denied in grace |
| `identity.handle.change` | user | SelfOnly | active | DenyAsLimit(`identity.handle.change`) on cooldown | reserved/retired/taken = 422 `handle.taken` |
| `identity.email.change` / `.change_confirm` | user | SelfOnly | active·suspended | DenyAsAbsence | old address notified (§7) |
| `identity.session.list` | user | SelfOnly | any | DenyAsAbsence | IsReadPath |
| `identity.session.revoke` | user | **OwnedResource(session)** | any | DenyAsAbsence | THE Auth-F3 red-fixture route |
| `identity.device.register` | user | SelfOnly | active·suspended | DenyAsAbsence | |
| `identity.device.remove` | user | **OwnedResource(device)** | any | DenyAsAbsence | |
| `identity.consent.set_push_category` | user | SelfOnly | active·suspended | DenyAsAbsence | category 8 has no row to gate — unrepresentable |
| `identity.export.request` | user | SelfOnly | any | DenyAsLimit(`identity.export.request.daily`) | statutory — survives suspension AND grace |
| `identity.export.read` / `.download` | user | **OwnedResource(export)** | any | DenyAsAbsence | IsReadPath; download audited |
| `identity.deletion.request` | user | SelfOnly | active·suspended | DenyAsAbsence | |
| `identity.deletion.cancel` / `.read` | user | SelfOnly | any | DenyAsAbsence | cancel reachable in grace by definition |
| `identity.account.suspend` / `.ban` / `.reinstate` | system; staff: SuperAdmin, SafetyAgent | OwnedResource(account) | — | DenyStandard | **internal verbs, NO HTTP mapping at S3** (S12 drives them); `requires_reason=true`; the rows existing now means an ungated moderation route can never ship (S1 `core.ledger.append` precedent); exercised by the engine matrix + `IAccountLifecycle` tests, not vacuous |
| `identity.deletion.execute` | system only (the worker) | OwnedResource(account) | — | DenyStandard | `requires_reason=false` (the job row is provenance) |

The CI-generated action×axis matrix regenerates from this table; a consumer-reachable mutation without a row remains a boot refusal (B1 machinery, guarding real consumer endpoints for the first time).

## 4. 9A config entries

Additive manifest `backend/modules/identity/config/identity.config.json` (S1 union-merge). Every entry has a real S3 consumer — the dead-tunable lint holds:

| key | scope | type | v0 | bounds | reason? | consumer |
|---|---|---|---|---|---|---|
| `identity.deletion.grace_days` | founder | int | 14 | **[0,30]** | true | deletion scheduler (0 = E2E-live + immediate-erasure variant; 30 = GDPR month ceiling) |
| `identity.handle.cooldown_days` | ops | int | 30 | [1,365] | false | handle change (profilemodel §1 default 1/30d — PROF OQ1 resolved) |
| `identity.handle.retirement_days` | founder | int | 365 | [30,36500] | true | retired-handles sweep (OQ-2 rides this value; permanent ≈ max bound) |
| `identity.session.access_ttl_minutes` | ops | int | 60 | [5,1440] | false | session validator |
| `identity.session.refresh_ttl_days` | ops | int | 90 | [7,365] | false | refresh rotation |
| `identity.session.max_active_per_account` | ops | int | 10 | [1,50] | false | session issue (oldest revoked past cap, reason `cap_evicted`) |
| `identity.email_code.ttl_minutes` | ops | int | 15 | [5,60] | false | challenge issue/validate |
| `identity.email_code.max_attempts` | ops | int | 5 | [3,10] | false | brute-force lockout per row |
| `identity.signup.verified_token_ttl_minutes` | ops | int | 30 | [10,120] | false | confirm→complete window |
| `identity.email_challenge.retention_hours` | ops | int | 24 | [1,72] | false | 13A retention_expiry sweep of challenge rows |
| `identity.email.send_daily_cap` | ops | int | 10 | [3,50] | false | cap behind quota `identity.email.send.daily` |
| `identity.export.daily_cap` | ops | int | 2 | **[1,10]** | false | cap behind quota `identity.export.request.daily` (floor 1: no ops edit can zero a legal right) |
| `identity.export.link_ttl_hours` | ops | int | 72 | [24,168] | false | export download expiry + sweep |
| `identity.export.pre_deletion_wait_hours` | ops | int | 72 | [0,168] | false | deletion worker export-wait cap |
| `identity.export.statutory_deadline_days` | founder | int | 30 | [1,30] | true | the statutory clock the S5 desk renders (Art. 12(3)) |
| `identity.device.register_daily_cap` | ops | int | 20 | [5,100] | false | cap behind quota `identity.device.register.daily` |
| `identity.handle_history.retention_months` | founder | int | 12 | [6,84] | true | post-deletion handle-history sweep (impersonation-defense window) |
| `identity.deletion.sweep_minutes` | ops | int | 15 | [1,60] | false | deletion/expiry worker interval |

**Structurally NOT config (each a deliberate refusal):** the **18 and 13 age floors — code constants in `AgeMath`** (§12 item 7: a desk tunable that can lower a child-safety floor must be structurally impossible; same logic as S1's DevSeams-not-in-9A ruling) · email transport selection (environment, DevSeams-family law) · token entropy/hash parameters (code) · the reserved-handle list (a seeded table with a desk mutation path at S5, not a config blob).

## 5. 10A quota keys (S1's one `Consume` verb; denials render the ONE LimitReached surface)

- **`identity.email.send.daily`** — actor = `eml_<HMAC(vault-named-secret, email_lower)>` synthetic actor (documented pattern: anonymous-flow quotas key on the HMAC'd PROTECTED RESOURCE — the victim's mailbox — never a raw email in `quota_counters`, never caller-scoped, so an attacker rotating IPs still cannot mail-bomb one address). Cap `identity.email.send_daily_cap`. Consumers: signup code, resend, login code, already-registered mail, email-change code. **Deny on anonymous surfaces stays the uniform 202** (a quota oracle would BE the enumeration oracle the 202 exists to kill; the mail just doesn't send — audited on the behavioral stream).
- **`identity.export.request.daily`** — actor = user, daily/UTC, cap `identity.export.daily_cap`. Deny = 429 `LimitReached`.
- **`identity.device.register.daily`** — actor = user, cap `identity.device.register_daily_cap` (push-token churn brake). Deny = 429 `LimitReached`.
- **Handle-change cooldown is NOT a 10A key — stated with reasoning (unanimous):** 10A windows are calendar-reset (daily/weekly, con-/user-local); a rolling 30-day cooldown is a different mechanism, and widening S1's window union for one consumer is the substrate mutation vanilla-by-default forbids. The cooldown is an identity-local deterministic check (`handle_history` max(changed_at) + cooldown config) whose deny SERIALIZES AS the same `LimitReached` component (`quotaKey: "identity.handle.change"`, `resetsAt` = eligible date, `premiumExtends: false`) — token law 4 is about ONE rendered surface, and the shape is the surface. A second rolling-window consumer promotes `RollingDays(n)` into 10A then, with a real second data point.
- Anonymous per-IP flood control (signup/login/handle-check) = host-level ASP.NET rate limiting (transport concern, §1c), NOT 10A — 10A stays actor-scoped product quotas. Stated so nobody retrofits it wrong.

## 6. 13A store registrations + the DSR machinery

### 6a. New purge-registry rows (every identity store; the seed→purge→assert-zero suite extends S1's; every subject-bearing row keyed by something OTHER than account_id — challenges by email, deletion_jobs — gets an explicit purge-reaches-it test: the S2 invocation-id scar, never again)

| store | account_deletion | statutory_erasure | minor_purge | consent_revocation | retention_expiry | orphaned_blob |
|---|---|---|---|---|---|---|
| `identity.accounts` | **Tombstone** (PII columns → NULL; state pinned `deleted`; handle → retired_handles; birthdate key **CryptoShred** via field_key_refs' existing registration) | Tombstone | **Delete** (confirmed-minor: enumerated purge, no tombstone identity survives; the legal-hold copy is S12's store, not ours) | NotApplicable (account existence isn't consent-gated) | n/a | n/a |
| `identity.email_challenges` | Delete | Delete | Delete (+ the in-tx under-13 hard delete happens PRE-pipeline, by design §1g) | n/a | `retention_hours` sweep | n/a |
| `identity.sessions` + `identity.refresh_tokens` | Delete | Delete | Delete | n/a | expired/revoked GC past 30d | n/a |
| `identity.devices` (+ push tokens) | Delete | Delete | Delete | n/a | n/a | n/a |
| `identity.push_category_consents` | Delete (projection; stream keeps S1 OQ-1a posture) | Delete | Delete | recompute rows for revoked categories | n/a | n/a |
| `identity.consent_current` | Delete (projection; evidence stays on the pseudonymized stream) | Delete | Delete | recompute | n/a | n/a |
| `identity.handle_history` | Pseudonymize account ref (HMAC re-key — moderation linkage survives for key holders, raw id severed) | Pseudonymize | Delete | n/a | `retention_months` sweep | n/a |
| `identity.export_jobs` (incl. artifact bytea) | **Delete incl. artifacts** (the artifact contains the whole PII corpus — it dies with the account) | Delete | Delete | n/a | expired-job sweep | n/a (no blob exists — the bytea ruling makes this class structurally empty until S11) |
| `identity.deletion_jobs` | Pseudonymize subject (the receipt SURVIVES — proof deletion ran; S1 purge_runs precedent) | Pseudonymize | Pseudonymize | n/a | statutory receipt retention | n/a |
| `identity.retired_handles` | NotApplicable (subject-severed at write, no linkage exists) — registered with reason | | | | `retirement_days` sweep | n/a |
| `identity.reserved_handles` | NotApplicable all classes (zero personal data) — registered with reason, never silently exempt | | | | | |

### 6b. The export pipeline — registry-driven, the mirror of 13A (the extraction centerpiece)

```
// Svac.DomainCore.Contracts
IExportContributor { string StoreKey; Task ContributeAsync(SubjectRef subject, IExportSink sink, ct) }
// sink writes (path, schema-versioned JSON). Registered via DI + compiled manifest
// backend/domain-core/export-registry.json, drift-gated like purge-registry.json.
// Non-contributing dispositions are REGISTERED states, never silent omissions:
//   NotExportable(reason)   — e.g. quota_counters (derived operational), data_protection_keys
//                             (key material), email_challenges (transient credential artifacts)
//   Withheld(basisRef)      — the pending-counsel posture (L-2): a legal question is an
//                             auditable registration, not a missing row
```

**The export⋈purge CROSS-GATE (new CI arch test, build-failing, red-fixture-proven):** every store in `purge-registry.json` declaring any subject-scoped purge verb MUST appear in `export-registry.json` with a contributor or a declared disposition. **This is the structural form of "preference_answers ALWAYS in export" (profilemodel §12):** when S6/S10 create that store, the build fails until they register its contributor — no future slice can quietly ship un-exportable PII. S3 registers contributors for every existing subject-bearing store: the identity tables above, `ledger_entries` (+ `ILedger.BalanceOf`), `events_consent`, `events_behavioral`, `events_ledger`, and the subject's OWN `events_audit` rows (staff-actor identities redacted with declared reason — Art. 15 covers the subject's data, not staff identities). Artifact: one zip of per-store schema-versioned JSON + `manifest.json` (stores, counts, schema versions, generated_at), built by the worker, stored as bytea behind module-internal `IExportArtifactStore`, TTL'd, streamed through the authed endpoint. `identity.export_ready` audit event + email (§7).

### 6c. The deletion pipeline as purge ORCHESTRATOR

Fully specified in §2. One orchestrator over S1's ONE purge pipeline — never a second pipeline; later modules join by 13A registration and the orchestrator never changes (the extraction guarantee).

## 7. Notification taxonomy rows (closure rule)

S3 EMITS the 3A events now; S4 delivers later; gate-critical rows ALSO go out as direct `IEmailSender` transactional mail now (the only channel that exists — honest, not dark). All category 8 (account & safety — never mutable):

| Row | S3 emits | Delivery at S3 |
|---|---|---|
| **data-export ready** (row already enumerated in the taxonomy) | `identity.export_ready` audit event | email `email.export_ready` now; push/inbox at S4 on the same event |
| **deletion scheduled** (grace end date, cancel path, export offer) — ADDITIVE row, called out to S4's closure-lint baseline | `identity.deletion_scheduled` | email now |
| **deletion canceled** — additive row | `identity.deletion_canceled` | in-app via `GET /v1/me/deletion` now; push at S4 |
| **deletion completed** | `identity.deletion_completed` | email now — the LAST outbound act BEFORE the shred (§12 item 9) |
| **sessions revoked** (refresh-reuse family revocation — the theft alarm) | `identity.session_family_revoked` | email `email.sessions_revoked` now |
| **email changed** (to the OLD address — the takeover tripwire in a passwordless system) | `identity.email_changed` | email now |

**Outside the taxonomy, documented:** verification/login/email-change code mails and the already-registered mail are transactional auth — they precede an account or ARE the authentication act; consent is inapplicable; no inbox row exists (stated so S4's closure-lint baseline is asserted, not assumed). **Deliberately ABSENT, with reasons:** new-device login notice (in passwordless auth, email compromise IS account compromise — a notice to the same compromised mailbox is no tripwire; the sessions list + revoke is the honest lever; the behavioral event is emitted for S12's anomaly work) · handle-change/session-revoke inline confirmations (no taxonomy row exists in the ratified nine-category table; not invented here). Zero rows in categories 1–7, 9 (asserted).

## 8. BUILD.md §9 seams made concrete

| Seam | S3 concrete form |
|---|---|
| Schema-per-context (1A) | second feature module on the S2 template; schema `identity` under module-owned DbContext; consumers reference `Svac.Identity.Contracts` only; `IAccountDirectory` is the read-composition seam; zero cross-module FK/join (§2 enumeration); module-boundary arch tests get their first two-feature-module exercise (identity ↔ AimlRouter mutual non-reference proven) |
| Region-first PII (L21) | region + lawful_basis (+ region_source on accounts) NOT NULL on all 12 tables; ddl-lint pii-patterns += `identity.*`; signup stamps `RegionSource.Signup` — S1's `IRegionResolver` ladder gets its first real rung, provenance recorded, history never rewritten |
| Transactional outbox (3A) | every mutation appends in-tx: consent stream (attestation, ToS, push categories), audit stream (state transitions, handle/email change, deletion request/cancel/complete, export lifecycle, session-family revocations), behavioral stream (funnel) — kill-between test proves mutation-without-event impossible |
| Server-authoritative trust (L20) | trust-DTO arch scan extended (`account_state|email_verified|attested|deletion_|session_`) + a RESPONSE-graph scan for `birthdate|dob` (red-fixture-proven); minor floors are server-side constants; region never client-set; opaque tokens carry zero decodable claims |
| Silent rejection unobservable | enumeration-proof 202s (signup, login, resend); reserved/retired/taken handles one answer; under-13/under-18 one wire shape; foreign/nonexistent resource one absence via predicate-folded ownership (SilentRej-L4 discharged, §3); quota deny on anonymous mail surfaces stays silent |
| One limit surface (token law 4) | cooldown + export + device quotas all serialize S1's `LimitReached`; no second deny shape exists |
| 4A policy per mutation + resource target | §3 table; boot refusal extended to target binding — the Auth-F3 lock, inherited by every future slice by registration |
| Idempotent-under-race writes | signup complete (verifiedToken single-consumption + 23505 re-read), code confirm (guarded-UPDATE CAS), refresh rotation (family CAS → one winner, reuse alarm), deletion request/cancel (state-guarded UPDATE), single-active export (partial unique index) — each with a forced-race test |
| Deterministic math in pure libs | `AgeMath` + `HandleRules` in `Svac.DomainCore.Deterministic` (IO-free by existing arch test); golden vectors: leap-day birthdays (Feb-29 → Mar-1 rule stated in the vector file), turns-18-today passes / tomorrow fails, turns-13 boundary, future-date = invalid input never a verdict, NFKC-fold cases; **the vector file is shared verbatim with both client test suites (a lint compares file-to-file — client and server can never disagree on a boundary birthday)**; no LLM anywhere |
| Consent WRITTEN by a surface | attestation + ToS + push categories written by real S3 surfaces through `IConsentLedgerWriter`; the 5 S17 kinds + Marketing declared with ZERO writers (nothing may honor them); category 8 unrepresentable in the type AND the CHECK |
| Buy-vs-build / vendor seams | `IEmailSender` in DomainCore.Contracts; real SMTP transport (Mailpit dev); prod-unconfigured throws (L18); transport never in 9A; S4 subsumes by one DI line |
| Foreign-event skip (§8 cl.7) | `consent_current` + `push_category_consents` projections ship the hermetic skip test (identity's first stream consumers — non-vacuous: fed a ledger event → watermark advances, no row) |
| i18n ×4 (DR-7.7) | §1d key list ×4; email templates server-rendered in recipient locale; canonical timestamps in responses; i18n-lint armed |
| Real-or-honestly-dark (L6) | avatar dark until S10/S11 (no dead affordance — skip-only); `irl_access_state` writer-less; suspend/ban verbs internal-only; reserved-list fandom-1k deferred to its source of truth; nothing fake |
| Analytics written AND received | `identity.signup_started/verified/completed/refused_age(anonymous)`, `export_requested/ready`, `deletion_requested/completed` behavioral events; E2E reads them BACK with watermark assert |
| Messy-input quarantine (L23) | strict parse at the door: birthdate shape, RFC-lite email + confusable rejection, handle charset lock, code length-lock → per-field validation Problem, never a 500 |
| Edge guard (L17) | no path rewrites added; N/A-with-note |

## 9. Dependency classification (every not-yet-built system S3 touches)

| Dependency | Class | Handling |
|---|---|---|
| S1 substrate (3A/4A/9A/10A/13A, IFieldEncryptor+birthdate purpose, IFieldKeyVault, RequestContext, Deterministic, DevSeams) | built | consumed as-is; S3 is the first PII-scale consumer — itself a test of S1's seams; Phase-2a additive mutations called out in §0 with byte-identical proofs |
| Email delivery spine (S4) | **seam-now** | `IEmailSender` (§1b): template-keyed contract frozen; Mailpit-backed real SMTP now; S4 replaces one DI registration with the delivery-tracked impl; taxonomy events already on 3A so S4's fan-out consumes without identity changes |
| Push delivery APNs/FCM (S4) | swap-safe | S3 stores tokens + consent only; zero delivery code exists to swap |
| Consent cards + revocation cascades (S17) | **seam-now** | `IConsentLedgerWriter` + closed `ConsentKind` + frozen payload + rebuildable projection; S17 adds surfaces and cascade suites, not shapes |
| Moderation drivers of suspend/ban + custody holds (S12) | **seam-now** | `IAccountLifecycle` verbs + staff 4A rows exist un-mapped; the 13A custody-hold check is consulted + recorded from day one, red-fixtured with a test-registered hold; `irl_access_state` column writer-less |
| Profiles / tag taxonomy (S10) | not read — deliberate | free-text `fandom_tag` on the account row + `IAccountDirectory` import path; reserved-handle fandom-1k arrives with the taxonomy |
| Media pipeline (S11) | **deferred by design** | avatar endpoint does not exist; arrives as ONE additive contract change (endpoint + scan gate together) at S10/S11 |
| Verification / minors L1–L4 (S18) | not read | S3 is attestation-only by ratified posture (T2r); encrypted birthdate + attestation event are what S18 verifies against |
| Admin desk (S5) | swap-safe | audit/behavioral events + statutory-clock config flowing now; desk tiles render later |
| Azure Key Vault (OQ-3 infra) | seam-now (inherited) | birthdate encryption + HMAC named secrets ride S1's IFieldEncryptor/IFieldKeyVault posture unchanged |
| Blob storage | **deliberately unused** | export artifact = Postgres bytea behind module-internal `IExportArtifactStore`; promotion = one DI swap when exports grow media (S10/S11) |
| Redis | deliberately unused | sessions are Postgres reads; promotion = read-through cache behind the session validator, one class, when a hot path is proven |
| SMS vendor | not read | phone deferred with reason (§0) |
| Clients (iOS/Android) | **not in build scope** | contract designed to the S7 gateway mapping (§1d); activation = the pre-approved contract-regen + one conforming type, at the next client slice |
| MUST-BUILD-FIRST blocking S3 | **none** | S1 + S2 landed; Mailpit already in compose; S3 starts now |

## 10. Outcome moved + evidence at sign-off (THE HARDENED GATE for S3)

Ledger row: **"signup→verified→delete E2E green."** Evidence, all verified twice, never on agent word (L10/L28):

1. **Build + tests:** `dotnet build` 0/0; Architecture suite green with NEW rules red-fixture-proven BOTH directions: target-binding boot refusal (both directions), export⋈purge cross-gate, birthdate-response scan, trust-DTO new patterns, identity module boundary, category-8 unrepresentability, DevSeams never-in-prod (incl. the E2E sweep trigger), SMTP prod-throw. **S1's two skip-annotated lens tests UN-SKIPPED and green — Auth-F3 and SilentRej-L4 retired; the deferred-findings ledger updated.** Domain-core Phase-2a byte-identical proof (S1/S2 suites untouched-green + S1 E2E rerun BEFORE identity builders fanned out). Tests.Identity green <2s: AgeMath/HandleRules golden vectors, forced-race suite (§8), purge-completeness extension (seed→every class→assert-zero + tombstone shape + Unprotect-fails-post-shred + the keyed-by-email purge-reaches-it tests), export-completeness (seed→export→every registered store present in the zip + manifest matches registry), enumeration-uniformity (existing vs fresh email byte-identical 202; reserved vs taken handle identical), category-8 absence (PUT /…/8 ≡ PUT /…/17).
2. **Contract:** `contracts/openapi.v0.json` regenerated with the §1c delta; contract-lint green on REAL paths (trust-field rule's first non-vacuous run, one LimitReached, bearer semantics filled); drift gate green; `message-keys.json` + ×4 catalogs lint green; `export-registry.json` + `purge-registry.json` drift-gated; ef-gate idempotent on the new migration chain.
3. **THE LIVE E2E** (`backend/e2e/identity.e2e.mjs` against compose; real endpoints + REAL Mailpit (REST oracle at :8025) + real 3A events; SQL/stub bypasses BANNED; `grace_days=0` via bounds-legal founder test config): handle-availability → email challenge → **code fetched from Mailpit** → confirm → complete (18th-birthday boundary vector) → session works (`GET /v1/me`) → refresh rotation → **reuse drill: old refresh ⇒ family revoked + audit event read back + `email.sessions_revoked` in Mailpit** → device + push-category consent write → consent event read BACK off `events_consent` → category-8 drill (PUT /…/8 ≡ PUT /…/17 byte-identical) → handle change → immediate second change denied as `LimitReached` with correct `resetsAt` → **IDOR drill live: second account's sessionId/exportId ⇒ absence byte-identical to random ids, victim rows untouched** → email change + old-address notice in Mailpit → export requested → worker → `email.export_ready` in Mailpit → zip downloaded, per-store files + manifest asserted against export-registry (incl. consent + ledger contributions) → deletion requested → `email.deletion_scheduled` in Mailpit → **grace law drill: same session still reads /v1/me + export download OK, settings PATCH denies as absence** → cancel → re-request → sweep triggered (DevSeams-only diagnostic trigger, S1 canary pattern, never in the shipped contract) → `email.deletion_completed` in Mailpit → **purge read-back: accounts tombstoned, handle in retired set and NOT re-registrable, email freed, sessions/devices/challenges zero, consent pseudonymized, artifact gone, Unprotect(birthdate) fails, purge_run receipts incl. the events_heatmap_provenance full-history verb** → old access token → 401 → **under-18 AND under-13 refusal drills: wire responses byte-identical, timing delta bounded, DB row count unchanged, challenge row provably destroyed (under-13), refusal event carries zero identifiers** → behavioral funnel events read back with watermark advanced → fresh-boot clause (`down -v` → `up`, zero errors/restarts) → post-E2E zero-exception log sweep on all instances. Suite run TWICE, identical.
4. **Minor-protection lens (standing):** the drills above + golden-vector file-to-file lint vs the client vector copies + floors-are-constants assertion (no 9A key matches `age|floor|minor`).
5. **Metric wired:** signup→verified conversion + export SLA clock + deletion completion flowing on behavioral/audit streams, verified RECEIVED; renders as S0-interim Actions status until S5's desk tiles.
6. **UX-coherence crawl:** N/A-with-note for client surfaces (no client wiring in scope — §1d); wire-level dignity/absence laws asserted by the tests above (clause-5 note recorded, playbook pattern).

## 11. Open questions for Julien (genuine forks only; recommendation on each)

- **OQ-1 — Passwordless-forever ratification (permanent product posture, founder-visible).** This contract makes email-code the ONLY consumer credential: no password exists anywhere (nothing in the ratified signup minimum names one; the web spec says "email-based"; it deletes the entire password-reset/stuffing/breach surface; email compromise ⇒ account compromise is the accepted trade, mitigated by session listing + revoke + rotation alarms). Adding a password or passkeys later is additive — nothing forecloses — but login UX is brand-visible product identity. **Recommendation: ratify passwordless-only; sessions are long-lived, so login is rare. All three architects independently converged here.**
- **OQ-2 — Handle afterlife (user-facing founder posture).** On deletion, is the freed handle (a) permanently retired ("that name is gone forever" — strongest impersonation defense, but deletion becomes a handle-squatting weapon: register a coveted name, delete, it is dead), or (b) quarantined then released (`identity.handle.retirement_days`)? The contract builds (b) with founder-config v0 365d and bounds reaching ~100 years, so (a) is one config edit — reversible either way until launch. **Recommendation: (b) at 365 days.**
- **OQ-3 — Ban-evasion memory vs maximal erasure (permanent privacy posture; mirrors S1 OQ-1).** When a BANNED account exercises deletion (GDPR says it may), do we (a) retain a salted-HMAC email ref (+ push-token hash) in a separate `identity.ban_evasion_refs` store, `lawful_basis=legitimate_interest`, founder-scope retention ceiling, consulted at signup to refuse re-registration (the enforcement-integrity posture the A5 "device-ban links" row implies); or (b) purge everything (maximal deletion — a ban is undone by delete+re-signup)? If (a): one table + a signup check + 13A NotApplicable(reason) row — in-slice, small. **Recommendation: (a), built reversibly (S0 pattern) — nothing irreversible until the first production ban-deletion.**

No other question forks this slice. Grace value, cooldown mechanics, export format, enumeration postures, encryption tier, marketing-consent placement, and the Auth-F3 design are resolved below with stated reasoning, ops-/founder-revisable from the desk or by later additive contract changes.

## 12. Judge's synthesis record (conflicts + the one ruling contradiction)

1. **Signup flow: email-verified-FIRST + atomic complete (P2/P3) over P1's create-unverified-account-then-verify.** P1's shape persists a birthdate and PII keyed to an email the submitter has NOT proven they own, creates squattable unverified rows needing a TTL GC, and gives the minor no-store posture a pre-verification table to leak into. Email-first means no account row, no birthdate, nothing exists before the email is owned and the birthdate is adult-proven — `email_challenges` has no birthdate column BY DESIGN (P3). P3's separate confirm step (verifiedToken) adopted over P2's code-in-complete: the code is validated at the code-entry screen (instant feedback, the design/05 state), not three steps later.
2. **Refresh rotation + family reuse detection (P2/P3) over P1's single-token-no-rotation.** P1 is right that server-side revocability answers the *revocation* threat — but rotation answers a different one: **detection**. A stolen long-lived opaque token is a silently valid credential until idle expiry; rotation makes parallel use a detectable, family-revoking, user-notified event. For a safety-critical identity module the alarm is worth the moving part; P1's own concession ("one nullable column + a grace check — the schema doesn't fight it") confirms the cost is bounded. Opaque server-side sessions themselves were unanimous; JWT rejected 3-0 for the §1b reasons.
3. **Seam placement: `IEmailSender`, `IExportContributor`, `IConsentLedgerWriter` in `Svac.DomainCore.Contracts` (P1/P2) over P3's `Svac.Identity.Contracts`.** S4 re-registers the email impl, S6/S10/S17 register export contributors and consent writers — if those interfaces live in identity, every registrant references the identity module forever (a 1A dependency-direction smell); the buy-side/registry family home is domain-core (IPaymentService + 13A precedents). P3's `Withheld(basisRef)` export disposition and the export⋈purge cross-gate adopted verbatim — the strongest single idea in the three proposals, alongside P2's identical gate framing.
4. **Auth-F3 mechanism: P2/P3's shared design adopted** (they converged independently: closed target-binding union, TargetRule table column, engine-side ownership resolvers, boot refusal both directions, DenyAsAbsence, predicate-folded ownership discharging SilentRej-L4). P1's `/v1/me/*` URL law adopted on top (no account id expressible in any consumer route) — defense in depth. P1's `accountState` actor axis adopted; it is also what makes the grace rights-set (§2) table data instead of handler code.
5. **Export artifact: Postgres bytea behind module-internal `IExportArtifactStore` (P1) over blob container (P2/P3).** An S3 export is kilobytes of JSON; a blob container adds a second store, SAS/leaked-link surface, and orphan-sweep machinery two slices before the media pipeline that will own blob discipline (S1 already scoped OrphanedBlob to "activates S11"). Azurite being in compose does not change what the artifact needs. Promotion = one DI swap. P2's `manifest.json` receipt adopted into the job row.
6. **Email column: plaintext (P1/P2) over P3's `contact_email` encryption + HMAC lookup.** S1's CLOSED IFieldEncryptor purpose enum (`special_category, birthdate, verification_audit`) is the ratified encryption tier made structural; email is the login lookup and S5's audited admin-search key. P3's design is recorded as the documented upgrade path (HMAC column + ciphertext, one migration) — a real option, not a requirement. PROF OQ5 resolved.
7. **Age floors: code constants 18/13 (P1/P2) over P3's 9A `[18,25]`-bounded 18-floor.** P3's bounds trick does prevent lowering — but a constant is unreachable from every desk, every config bug, and every future bounds edit; there is no jurisdictional requirement to raise the floor on the table, and if one arrives it is a code change with a golden vector, which is the right ceremony for a child-safety floor. Refusal shape: one wire shape for both floors, zero persistence, under-13 same-tx challenge hard-delete, anonymous counter event — all three proposals merged; the dedicated `signup.refused_age_floor` key (P1/P2) over P3's fully-generic failure (the submitter supplied the birthdate; the key leaks nothing new, and never distinguishes 13 from 18).
8. **Grace semantics: synthesis replacing both P1 and P3.** P1's fully-active grace leaves a user who asked to disappear visible for 14 days; P3's revoke-and-restore ransoms the export against the cancel. Adopted: logical `deleted` at request (visibility cascades fire immediately via the state event) + sessions/login remain valid but policy-restricted to the RIGHTS SET (me/export/deletion/sessions/logout) via the accountState axis. Cancel is the ONE `deleted→active` transition, grace-only; post-purge deleted is terminal — the §1c table's states and semantics are preserved without inventing a fifth state.
9. **Deletion-completion email: sent (P2/P3) over P1's deliberate silence.** P1's objection (emailing a purged address re-creates PII) dissolves by ordering: the completion mail is the last outbound act BEFORE the shred, while the address still exists — and an erasure confirmation is the receipt a data subject is owed.
10. **Push consent: account-level per-category with category 8 UNREPRESENTABLE (P1) over P2's per-device payload and P3's CHECK-only immutability.** Taxonomy consent is per-account; devices are delivery addresses. Absence law in its purest form: `PUT /…/8` is 404 wire-identical to `PUT /…/17`; the type, the CHECK, and the route all make category 8 non-existent rather than locked.
11. **Client activation OUT of build scope (P1) over P2/P3's thin-activation inclusion.** The ledger row owns `backend/modules/identity` only; the ledger is the authority. The §1d mapping + versioned-contract callout satisfies the S7 seam clause; activation is the pre-approved shape at the next client slice, with Maestro evidence there. HARDENED-GATE clause 5 records the N/A-with-note.
12. **Marketing consent at signup: RULING CONTRADICTION — P3 loses the point automatically.** P3 writes `Marketing` consent in the signup tx; DR-7.1 (founder-ratified 2026-07-09, quoted in profilemodel §13) rules "all consents at feature first-touch", and the S7-built signup shell has no marketing step. Marketing is a declared, UNWRITTEN ConsentKind until its surface ships (S4 opt-in moment or S17 consent center) — per the consent-written-by-a-surface seam, nothing may honor it before then. P2's proposed OQ dissolves the same way: the ruling already decides it; recorded here so it never resurfaces.
13. **Adopted unanimous or near-unanimous points as written:** opaque sessions; passwordless email-code (→ OQ-1); avatar honestly dark until S10/S11 (supersedes the S7 "lands with S3" note — recorded); `/v1/me/*`; one challenge machine, HMAC'd codes (P2/P3's keyed HMAC over P1's salted SHA — a 6-digit space must not be offline-brutable); cooldown-not-a-quota rendering the one LimitReached shape; grace 14d [0,30] founder config; email-HMAC quota actor; anti-enumeration 202/already-registered-mail; reserved≡retired≡taken; `AgeMath`/`HandleRules` in DomainCore.Deterministic (P1's placement — the assembly exists and is the ratified home) with P3's client-shared vector file; `irl_access_state` column writer-less now (P2/P3 over P1 — the nouns spec names the axis on the account noun; one column now, no migration later); staff lifecycle rows internal-only with no HTTP mapping (P2/P3); custody-hold consulted + recorded from day one; deletion_jobs receipt pseudonymized; evals N/A-with-reason; handle-check GET (P1/P2 — handles are public by nature; the limiter and quota cover probing); new-device email dropped with stated reasoning (§7).

---

## 13. RATIFICATION (orchestrator, 2026-07-11 — Julien's in-absence pre-approval)

Contract **RATIFIED**. Julien was live for the review but stepped away at the OQ checkpoint; his standing rule pre-approves in-absence ratification of the current slice's contract. All three OQs are reversible with the judge's recommendation, which I independently endorse. Recorded below; re-openable at any time before launch.

**Scope + the five tension resolutions ratified as written.** Email-verified-FIRST atomic `complete` (no PII before adult-proven; `email_challenges` has no birthdate column); opaque server-side revocable sessions + refresh rotation with family reuse-detection (JWT rejected); Auth-F3 solved as a general target-binding + ownership-resolver + boot-refusal mechanism that also discharges SilentRej-L4 (BOTH deferred findings retire at S3, un-skipping S1's two lens tests); 18/13 minor floors as code constants with the client-shared golden-vector file; two-phase rights-preserving deletion grace with the completion email as the last act before crypto-shred and the first call of S1's full-history heatmap purge verb. The export⋈purge cross-gate is adopted as the structural form of "preference_answers always in export" — it will build-fail S6 until S6 registers its 60-item preference store's export contributor (founder ruling, this session, made a compile-time invariant).

**OQ-1 — Passwordless-only: RATIFIED.** Email one-time code is the sole consumer credential; no password anywhere. Deletes the reset/stuffing/breach class entirely; passkeys/password remain a purely additive future upgrade (nothing to migrate FROM). All three architects + judge converged; I concur.

**OQ-2 — Handle afterlife: RATIFIED (b), 365-day quarantine then release.** `identity.handle.retirement_days` founder-config v0 365, bounds reaching ~100 years so permanent retirement is one desk edit. Blocks impersonation churn without turning deletion into a handle-squatting weapon.

**OQ-3 — Ban-evasion memory: RATIFIED (a), retain salted-HMAC email ref, built reversibly.** One `identity.ban_evasion_refs`-class store (salted-HMAC email + push-token hash), `lawful_basis=legitimate_interest`, founder-scope retention ceiling, consulted at signup to refuse re-registration; 13A `NotApplicable(reason)` row. Built reversibly per the S0/S1 pattern — nothing irreversible until the first production ban-deletion. Mirrors S1 OQ-1 (ratified conservative-reversible).

**WATCH ITEM carried to Phase 3 (not a blocker):** email stored plaintext (§12 item 6). The PII/residency security lens MUST explicitly bless-or-fix it against the documented HMAC-column upgrade path — it does not get to ride silent. Recorded so the security phase is accountable for the call.

**BUILD-ORDERING (binds the parallel plan):** S3's Phase-2a mutation of DONE units (`Svac.DomainCore.Contracts.Policy`, `.Hosting`, and the `IEmailSender`/`IExportContributor`/`IConsentLedgerWriter`/`AgeMath`/`HandleRules` additions) is the shared substrate S5 (bearer/Entra resolver) and S6 (resource-scoped result reads) both build ON. Therefore the S3 domain-core surgery is designed ONCE with the S5 + S6 contracts in hand — the S3 BUILD is held at ratification until both panels land and their domain-core needs are reconciled against §0's Phase-2a delta, so the substrate is mutated a single time, not three. Panels run parallel; builds serialize S3→(S5,S6). Byte-identical-behavior proof on S1/S2 suites + S1 E2E is the gate before identity builders fan out (SLICE_PLAYBOOK 2a).

**Carried into future contracts (unchanged):** Auth-F3 and SilentRej-L4 RETIRED here (no longer carried). Concurrency-F5 → S14. S2's three defers (S2-A / CONC-S2-4a / CONC-S2-5) → their named slices, untouched by S3.

Ratified. Phases 1–3 (scaffold → build → security) proceed to THE HARDENED GATE after the S5/S6 reconciliation; the slice then STOPS at DONE for Julien to /compact (stop-after-slice).

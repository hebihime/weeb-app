# SECURITY_REVIEW_S3.md — identity-accounts

Phase-3 adversarial review of slice S3 (identity), five lenses on the best model (fable), read-only,
triaged by the orchestrator. Per-lens full detail in the run's scratchpad (`sec-lens-*.md`). The build
reached THE HARDENED GATE before this review (signup→verified→delete E2E green); this phase hardens it.

**Disposition rule (SLICE_PLAYBOOK):** CRITICAL/HIGH fixed in-slice, each via a now-green regression
test. Cheap trust/residency/minor-protection/statutory MEDIUMs fixed too. Remaining MEDIUM/LOW deferred
with a `[Fact(Skip="deferred: SECURITY_REVIEW_S3.md <id>")]` proof test and carried to the named slice.
One HIGH (heatmap) is a founder escalation, not a code fix.

## Mandated verdicts

- **Email stored plaintext (§13 watch item): BLESSED, conditional.** No password exists (passwordless);
  codes are keyed-HMAC, tokens SHA-256, birthdate field-encrypted — a dump yields email+handle+timestamps,
  not credentials. The HMAC-lookup upgrade path is real (already proven twice in-repo: `BanEvasionRefs`,
  `EmailQuotaActor`); one migration, exact-match login survives, S5 admin substring-search dies (recorded
  cost). **Conditions: PII-5 and PII-7 below must land** so plaintext email's blast radius stays confined.
- **Heatmap retention posture (PII-4): RULED 2026-07-12 (founder) — ANONYMIZE-AT-WRITE.** The escalation
  is resolved: heatmap data is anonymized at write, so retaining it past `account_deletion` is lawful
  (GDPR Recital 26 — genuinely anonymous data falls outside the Regulation; retained despite the
  originating user's erasure wish). The `NotApplicable`-on-deletion (§1c) and `export-registry`
  `NotExportable` dispositions therefore stand as correct, not as a gap. Intent: **keep the analytics
  signal (cell/density/pattern), sever the subject** — a deleted account's contributions stay on the map,
  just unattributed. **This discharges PII-4 IFF the S9/S14 write path clears a two-sided bar — a
  build-time acceptance condition, not a live S3 bug (zero heatmap writers until S9/S14):** (1)
  *irreversibility* — the subject link is dropped at write OR crypto-shredded at deletion; a
  held-elsewhere salt makes the data *pseudonymous*, still personal data, Art.17 still applies; (2)
  *no singling-out* — coordinate+time+rare-attribute coarsened/bucketed to a k-anonymity floor so no
  retained cell re-identifies one person via the mosaic effect, while staying granular enough to remain
  actionable. That coarsening tension is the S9/S14 design work. The stream name "provenance" is the trap:
  provenance must not be subject-traceable post-deletion. **S9/S14 Phase-0 must verify BOTH irreversibility
  and non-singling-out (not merely "no plaintext id in the row") before the first provenance row.**
  Carried to S9/S14 Phase-0 as a ruled constraint.

## FIX NOW — CRITICAL / HIGH (each with a now-green regression test)

| id | sev | finding | fix |
|---|---|---|---|
| CONC-1 | **CRITICAL** | Cancel-vs-worker race physically purges a just-**canceled** account: worker `state→executing` is an unguarded UPDATE-by-PK, no row lock, acts on a stale entity → crypto-shred/tombstone after a committed 204 cancel | guarded CAS claim (`WHERE state IN ('scheduled','held') RETURNING`) + re-`SELECT … FOR UPDATE` the account + re-verify `deleted`/`tombstoned_at IS NULL`/due inside ONE tx before any irreversible step |
| CONC-2 / PII-2 | HIGH | Worker has no mutual-exclusion; a crash after `executing` commits strands the job (sweep only re-scans `scheduled\|held`) → account half-purged forever, completion email already sent, GDPR clock silently broken | the CONC-1 CAS gives one winner; lease-based re-sweep of stale `executing`; try/catch → retryable state |
| PII-1 | HIGH | Crypto-shred runs FIRST not LAST (registry unions sources in DI order, core before identity) — mid-run failure destroys the key while the accounts row (plaintext email/handle) is still un-tombstoned: subject un-erased AND un-recoverable (violates §2(5)) | `PurgePipeline.Run` hoists all CryptoShred cells to the end regardless of source order; gate test asserts the shred report is last |
| CONC-3 | HIGH | `PostgresEventStore.Append` computes `MAX(seq)+1` unlocked and never retries for `AnyVersion` — an attacker times concurrent same-account audit appends to lose the seq race and roll back the refresh-reuse family-revoke tx (suppress the theft alarm, repeatably) | retry `MAX+1` on unique violation for `AnyVersion` (or `pg_advisory_xact_lock`); keep the throw only for `Exact`; test: two concurrent same-account appends both commit |
| PII-3 / CONC-4 | HIGH | Grace-window identity takeover: Phase-L drops the row from both partial unique indexes, so handle AND email are claimable during the 14-day grace; `CancelDeletion` then dies on uncaught 23505 → the cancel right is permanently destroyed and the squatter keeps the identity. `IssueForLogin` also still resolves by email with `TombstonedAt==null` (only `IssueForSignup` was fixed) | gate handle+email availability on `tombstoned_at`, not `account_state`; `IssueForLogin` filters `AccountState != 'deleted'`; login/replay resolve deterministically (replay by the challenge row's `account_id`); `CancelDeletion` catches 23505 |
| MAIL-1 | HIGH | Account-existence timing oracle in prod: `TimingFloor` pads UP (floor, not ceiling) but mail is sent in-band (`await SendAsync`); a live account's SMTP round-trip (100ms–1s on a real relay) far exceeds the 60ms-floored absent/banned path. Mailpit (<60ms) masks it in dev/CI/E2E. Same asymmetry on signup-`confirm`, `/v1/me/email`(+confirm), and `POST /v1/auth/session` (unfloored) | dispatch outbound mail OFF the request path (in-proc `Channel` + `BackgroundService` outbox; the 3A event already fires, S4 owns real delivery); floor the confirm/email/auth-session endpoints; equalize backed-vs-unbacked work (dummy HMAC / same lock path on the absent-row branch) |
| OPS-2 | HIGH | SMTP "fail-closed at startup" is a LAZY factory throw (`AddScoped<IEmailSender>(_ => throw)`), never resolved at boot → a prod deploy with no SMTP boots clean, passes `/health`, then 500s on first signup. Contract + comments claim startup-throw; false (contrast the correct `ProdFieldKeyVaultGuard`) | explicit `SmtpConfiguredGuard.Enforce(...)` before `app.Run()` (the S2 TRUST-BREAK-3 pattern: an unresolvable typed dependency, not a factory); + the missing startup-throw test |
| OPS-1 | HIGH | Anonymous rate limiter keys on `Connection.RemoteIpAddress` with no `UseForwardedHeaders`; behind Azure Container Apps' Envoy ingress every request shows the proxy IP → "100/min/IP" collapses to a global 100/min bucket (one attacker locks out signup/login for everyone) | `UseForwardedHeaders` scoped to the ingress network (`KnownNetworks`/`KnownProxies`), key on the real client IP; do not naively trust XFF |

## FIX NOW — MEDIUM (cheap trust / residency / statutory / erasure-completeness)

| id | sev | finding | fix |
|---|---|---|---|
| AUTH-1 | MED | Export read/download scoped only by the policy filter+resolver (correct now) but the queries lack a defense-in-depth `AccountId==actor` predicate, and GET reads have no boot-refusal backstop → latent PII-zip IDOR on any refactor dropping `.RequirePolicyAction` | fold `&& AccountId == actor` into `GetMeExport` + `GetReadyZipAsync` |
| PII-6 | MED | Tombstone leaves `BirthdateEnc`/`Locale`/`LastActiveAt`/`EmailVerifiedAt`/`AttestedAdultAt`/`CreatedAt`/`Region` standing; contract says every PII column NULLed | NULL/sentinel all PII columns; test enumerates every column |
| PII-5 | MED | Old-email challenge rows survive erasure (purge matches account_id OR *current* email; a changed-email signup row carries neither) — the exact S2 invocation-id scar shape | stamp `account_id` at consumption (or purge by all historical emails); add a changed-email purge-reaches-it seed test |
| PII-7 | MED | Export artifact writes the DECRYPTED birthdate into a plaintext-zip bytea and never nulls `Artifact` on expiry (no sweep) → plaintext PII-at-rest, unbounded, defeating the encryption tier | null `Artifact` on expiry; encrypt the artifact bytea under the subject's field key so crypto-shred covers it |
| PII-9 | MED | Worker re-creates a raw-account-keyed `events_behavioral` row AFTER the pipeline hard-deleted that stream for the subject → never re-purged | key the completion behavioral event on the pseudonym / anonymous counter |
| PII-8 | MED | `DevKeyringFieldKeyVault` `_destroyed` is in-memory over a deterministic seed → restart resurrects "destroyed" keys, Unprotect succeeds again (dev-only, but the only shipped vault) | persist destroyed key names |
| OPS-3 | MED | `ConfigRegistry.SetValue` bounds check is a hardcoded switch over 3 AimlRouter keys; identity keys hit `default:break` → the statutory export floor (`daily_cap` ≥1) and grace `[0,30]` are NOT enforced on the real write path | generalize `SetValue` to enforce `row.BoundsJson`; add identity bounds to the manifest; red-fixture test |
| OPS-6 | LOW→fix | `handle-availability` GET has no limiter (unbounded scrape + triple-`AnyAsync` amplification) | apply the anonymous limiter |

## DEFER — MEDIUM/LOW (Skip-annotated proof test, carried)

| id | sev | finding | carry to |
|---|---|---|---|
| CONC-5 / AUTH-5 | MED | Session-cap eviction is check-then-act → transient `max+1` sessions (no privilege gain) | S14 (with the quota-tx work) |
| OPS-4 | MED | `SetValue` doesn't enforce `row.Scope` (founder key writable at ops authority) — 4A already gates the caller; no config-write surface exists | S5 (admin config desk) |
| OPS-5 | LOW | Config dual-key: manifest seeds both the human `identity.*_cap` and enforced `quota.*.cap`; editing the intuitive one is a no-op | S5 (collapse to one key with the desk) |
| AUTH-4 | LOW | Logout doesn't clear the device push token; sessions minted `device_id=null` so the cascade can't fire | S4 (notification delivery) |
| CONC-6/7, PII-11 | LOW | Deletion/export recovery-path 500s (`effectiveAt!.Value`, export loser `FirstAsync`); ban-evasion region=caller-not-subject; retirement cutoff not applied in `HandleChangeService` | S12 (moderation surfaces) |

## DOCUMENTED — accepted residuals (no fix, honest note)

- **SREJ-4:** ban-evasion refusal is a self-ban oracle — an adult birthdate + refusal implies a ban. Scope
  is self-discovery only (reaching the ban check needs mailbox control via a verifiedToken); enforcement
  is intact. Inherent to any probeable ban list; the §11 "no oracle" phrasing is corrected to "no
  cross-account enumeration oracle."
- **OPS-7:** the DevSeams sweep/grace endpoints are anonymous+destructive in Development only (prod boot
  hard-fails DevSeams anywhere but Development). Acceptable under the S1 canary pattern; loopback-binding
  is a future nicety.

## Verified sound (attacked, held)

Per-subject field-key isolation (real-Postgres tested); keyed ban-evasion HMAC + signup consult; the
S2-scar purge orderings for the same-email case; the export⋈purge cross-gate (red-fixtured both
directions + runtime backstop); authed OwnedResource export download with absence semantics; staff-actor
redaction in the audit export; region never client-settable; lawful_basis resolved-and-stamped on all 12
identity tables; receipt pseudonymization; CSPRNG `sst_`/`srt_` minting + SHA-256 hash-only storage;
atomic `FOR UPDATE` refresh rotation + working family reuse detection; `IdentityAtomicScope` genuinely
atomic cross-schema (one physical connection/tx bound to both DbContexts — the flagged concern was a
non-issue); minor floors are code constants unreachable from any 9A key; birthdate never in a response
graph; DenyStandard never reachable by a consumer; DevSeams truly prod-unreachable (double-gated + absent
from the OpenAPI emitter).

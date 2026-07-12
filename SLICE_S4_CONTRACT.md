# SLICE S4 CONTRACT — notifications-spine (LOCKED)

**Gate:** G0 · **Actor:** user ("every state change reaches them") + system ("email fallback works") · **Kills:** B7 · **Ledger outcome:** delivery-tracked notice lands via push AND email fallback (BUILD.md §7 S4 row) · **Authoritative spec:** surfaces doc Part 1 (`~/.gstack/projects/weeb-app/jp-unknown-design-20260708-surfaces.md`) — the nine-category taxonomy, the closure rule, category-8 immutability, quiet-hours-except-8, every-push-deep-links, lock-screen privacy · **Design artifacts:** design/02 5.9 (Inbox: Notifications area, pinned undismissable cat-8 rows, per-tab badges), 5.42 (push + email anatomy, "this category can't be muted" footer), design/06 (categories + quiet hours; account & safety visibly locked on), design/05 (just-in-time permission — client concern, out of scope here), DESIGN.md token laws 3/4/5.

Synthesized from three proposals by the design judge (P1 simplicity, P2 extraction-grade seams, P3 privacy/i18n/residency). Per-conflict adoptions and the TWO ruling contradictions are recorded in §12. Two open questions remain (§11); everything else is resolved with stated reasoning.

**Governing theses (grafted):**

- **From P1 (simplicity):** S4 adds ZERO new intake verbs. The 3A event substrate IS the notification intake: a module that mutates state already appends its event in the same tx (the ratified outbox seam); S4 is a projection over those streams plus channel adapters. Producers never call a notification API, so "forgot to notify" is structurally impossible for any event with a registered binding, and S3 — a DONE unit — is delivered-for without touching one line of its behavior, exactly as its ratified §1b/§7 promised.
- **From P2 (extraction-grade):** S4 is the LAST moment the notification system can be made a pure 3A consumer. The taxonomy is typed CODE (a closed registry, additive registration files per producing module), the closure rule is a CI lint plus an arch-test caller allowlist, and every vendor (APNs, FCM, SMTP relay) sits behind one interface each. Dependency direction: notifications → producer contracts, never the reverse; identity never learns notifications exists.
- **From P3 (privacy):** the notification system is the single largest cross-subject PII conduit in the product — it leaves the platform (APNs/FCM/SMTP) and lands on lock screens strangers read. Three invariants are made structural now: (1) **no PII is ever denormalized into a notification** — payloads are `message_key + typed variables` where person-variables are opaque refs resolved at render/read time, so purging a subject never sweeps other users' inboxes; (2) **content never leaves the server un-minimized** — the SERVER decides the lock-screen render mode (token law 5 as server truth); (3) **category-8 immutability and its quiet-hours/consent exemptions are unrepresentable, not checked** (extends S3's `PushCategory(8)` pattern into every S4 table, type, and route).

---

## 0. Scope ruling

**S4 owns `backend/modules/notifications/**`** on the S2/S3 module template exactly: `Svac.Notifications.Contracts` (public) + `Svac.Notifications` (internal, schema `notifications`, own `NotificationsDbContext`) + `backend/tests/Svac.Tests.Notifications` + `backend/modules/notifications/config/notifications.config.json` + `backend/e2e/notifications.e2e.mjs`. Plus:

1. **The taxonomy as code** — `Svac.Notifications.Contracts` registry types (§1a) + additive registration files (`Rows/Identity.rows.cs` now; `Rows/<Module>.rows.cs` per future producer) + the emitted, drift-gated artifact **`backend/modules/notifications/taxonomy.json`** (the `purge-registry.json` pattern).
2. **`tools/notification-lint/`** — the closure lint (Node 24, zero npm deps, `node:test`, gate lane, same family as ddl-lint/contract-lint), CI-wired (§7).
3. **Narrow Phase-2a additive mutations of DONE units** (SLICE_PLAYBOOK 2a: mutate → byte-identical proof → rerun S1/S2/S3/S5/S7 suites + S3 E2E → THEN fan out):
   - **`Svac.Identity.Contracts`** — two additive seams (§1b): `IDeliveryDirectory` (read: delivery targets for a recipient) and `IPushTokenFeedback` (identity-owned token-invalidation write). Identity implements; S4 calls. No FK, no join, no PII duplicated into a second store.
   - **DI re-registration in the public host:** `IEmailSender` → `TrackedEmailSender` (a decorator over the existing `SmtpEmailSender`) — the EXACT subsumption S3 §1b pre-ratified: "the interface, template keys, and call sites survive verbatim." Every send (S3's transactional mail included) now writes a `notifications.deliveries` row. One DI line changes; nothing else in identity moves (§12 ruling 3).
   - **`Svac.DomainCore.Deterministic`** — `QuietHoursMath` (pure, IO-free; golden vectors: cross-midnight windows, DST spring/fall inside a window, tz-invalid rejection) + retry-backoff arithmetic.
4. **Contract artifacts:** `contracts/openapi.v0.json` delta (§1c); `contracts/message-keys.json` additions ×4 (§1e); NEW **`contracts/deep-links.json`** (§1d); first **`i18n/server/{en,es,pt,zh-Hans}.json`** render catalogs (push/email are server-rendered — §1e executes S1 §12.12's recorded delegation as drift-test-only, NO 9A mirror).
5. **One admin-host desk tile** (delivery metrics, additive on the S5 dashboard skeleton, reads `INotificationMetrics` from the contracts assembly) — BUILD.md §5 "delivery metrics on desk" honored now, not deferred (§12 ruling 14).

**S4 does NOT own:** client activation — the push-permission moment/OS prompt choreography (DR-7.1 "after first value"; design/05), the inbox and prefs screens, APNs/FCM client registration UX: all the next client slice's first task against this frozen contract (S3 §12.11 precedent; the S7 inbox scaffold + priming screens sit gallery-only waiting; HARDENED-GATE clause 5 = N/A-with-note, wire-level laws asserted by tests) · the Inbox's Matches/Requests tabs (S13/S23 content; S4 owns only the Notifications area's data) · the battle-challenge modal (the surfaces spec's own words: a real-time interaction, NOT a notification — S22, whose TTL push rides THIS spine's transport seam as a registration, not a schema change) · the 12A-r forced romantic modal (S20, same reason) · UGC auto-translation inside notifications (T8 — zero UGC producers exist; the `Translatable` variable slot is the seam, S13 wires the router call and ITS eval) · SignalR live badges (pull inbox is honest at G0; promotion = one nudge publisher at S13, additive, no schema change) · category-9 marketing senders and the opt-in surface (no sender exists; DR-7.1 + S3 §12.12 route the opt-in to feature first-touch / S17's consent center; delivery is structurally refused absent a granted consent row; no unsubscribe endpoint ships for a sender that cannot send — honest-dark, recorded) · operator paging (S5 §7 disposition 14 — a different channel class, wired by S12; S4 inherits a clean baseline) · device/push-token registration and per-category push CONSENT surfaces + storage (S3 built them; S4 reads consent via its OWN stream projection, never identity's table).

**Real-or-honestly-dark:** the registry seeds ALL nine categories' rows verbatim from the surfaces spec, each carrying `OwningSlice`; only rows whose producer events exist today get bindings (S3's six category-8 rows, §7). Unbound rows are declared-future and visible in `taxonomy.json`; the lint fails any binding without a row — the closure rule made mechanical. The un-lintable half ("every user-visible state change HAS a row") stays where it already lives: the mandatory §7 section of every slice contract, diffed against `taxonomy.json` at review.

**Evals: N/A with reason (unanimous across proposals)** — zero latent surface. Routing, rendering, scheduling, escalation are all deterministic space (a registry + templates + window math). The one future latent input (T8 translation of UGC variables) arrives with S13's router call and its eval. Golden vectors + the live E2E are the quality story.

## 1. Module API surface + OpenAPI delta

### 1a. The taxonomy as code (the extraction centerpiece — P2, with P1/P3's lint folded in)

```
// Svac.Notifications.Contracts
enum NotificationCategory { Connections=1, BattleChallenges=2, InvitesEvents=3, CrewItems=4,
                            Messages=5, IrlLifecycleSurveys=6, ContestsQuestsEconomy=7,
                            AccountSafety=8, Marketing=9 }   // CLOSED — the ratified nine, forever

sealed record NotificationRow {
  string RowKey;                        // e.g. "identity.export_ready"
  NotificationCategory Category;
  ChannelPlan Channels;                 // Inbox: Yes | No (No requires a stated reason — §7)
                                        // Push:  PerConsent | Never   (cat 8: consent has no row to
                                        //        consult — bypass is typed, not flagged)
                                        // Email: ProducerDirect | FallbackOnPushFailure | Always | Never
  bool GateCritical;                    // escalation machinery applies
  Batching Batching;                    // Immediate | Batched(configKeyRef) — the lint fails ANY
                                        //   Batched row until S13 lands the first batched row WITH
                                        //   its 9A entry (no dead tunable, no dead machinery)
  PushRender PushRender;                // Full | SenderOnly | Generic — token law 5, SERVER-decided (§1f)
  string TitleKey; string? BodyKey;     // registered in message-keys.json ×4
  string DeepLinkKey;                   // must resolve in contracts/deep-links.json (lint)
  string OwningSlice;
}
// RespectsQuietHours is COMPUTED: Category != AccountSafety. Not row data — "except category 8"
// is structural, not per-row judgment. Same for consent: PushCategory(8) does not exist as a
// ConsentKind (domain-core, S3), so a cat-8 consent read does not compile.

sealed record EventBinding {
  string EventType;                     // e.g. "identity.export_ready" on the Audit stream
  string RowKey;
  RecipientSelector Recipients;         // StreamSubject | FromPayload(jsonPointer) — closed union
  ModelBuilder Model;                   // payload → typed Vars (§1f: opaque refs + canonical scalars ONLY)
  bool EmailByProducer;                 // true ⇔ the producer sends this row's email itself (S3's
                                        //   ratified sends) — the lint proves no double-send path exists
}
```

Rows + bindings live in additive registration files per producing module (union-merge friendly, L29), compiled into one registry the way the 4A table and 13A registry are, and emitted as drift-gated `taxonomy.json`. **No enqueue/notify verb exists anywhere in the contract, by design.** A future producer integrates by (1) appending its 3A event as it already must and (2) adding a `Rows/<Module>.rows.cs` registration + template keys. That is the entire integration contract for every future slice.

### 1b. The pipeline: two projections, one dispatcher, two identity seams

```
// Svac.Notifications (internal)
// 1. NotificationFanoutProjection — IProjection, ConsumerId "notifications.fanout", Stream Audit.
//    THE first cross-module external stream consumer: §8-clause-7 foreign-event skip harness ships
//    non-vacuously, exactly as S1 reserved and S3 §1c deferred to "the first external consumer (S4)".
//    For each bound event, in one tx: insert inbox_items (idempotent — §2) + deliveries rows per
//    planned channel after consent/quiet-hours/address evaluation; suppressions are TERMINAL rows
//    with a reason, never silence. CanHandle = binding-table membership.
// 2. ConsentProjection — ConsumerId "notifications.consent", Stream Consent: maintains
//    notifications.push_consent from consent.recorded PushCategory events. Rebuildable; zero identity
//    reads; category 8 unrepresentable (same CHECK as identity's own projection). Second non-vacuous
//    skip test for free. Absent row = default: ON for categories 1–7, OFF for 9 (opt-in only).
// 3. DeliveryDispatcher — hosted worker, polls due deliveries (deliver_after + partial index IS the
//    queue — no queue table, no Redis; §2). Claim = guarded UPDATE CAS, idempotent under race and
//    under two instances. Push: retry max_attempts with backoff on transient failure; TokenInvalid →
//    IPushTokenFeedback.MarkInvalid (identity clears ITS row, audited — single store per fact, no
//    shadow suppression table); terminal push failure OR zero deliverable devices on a GateCritical
//    row with Email=FallbackOnPushFailure ⇒ create the email delivery (the escalation), rendered
//    server-side in the recipient's locale. Quiet hours DEFER, never drop: deliver_after = window
//    end (§12 ruling 6). Category 8 bypasses consent and quiet hours by type.

// Svac.Identity.Contracts — Phase-2a ADDITIVE seams (the only new cross-module surface):
IDeliveryDirectory {
  Task<DeliveryProfile?> GetDeliveryProfile(OpaqueId accountId, ct);
    // { Email, Locale, Devices: [{DeviceId, Platform, PushToken?}] } — null = absence (tombstoned/unknown)
}
IPushTokenFeedback {
  Task MarkInvalid(OpaqueId deviceId, string reasonKey, ct);   // vendor said token dead → identity
    // clears its own store, audited; S4 never writes another module's rows
}
// Consent is deliberately NOT an interface: S4's own consent-stream projection (event-sourced by
// design, zero identity coupling, free skip test).
```

**Vendor seams (buy-side law, one interface per vendor):**

```
// Svac.Notifications.Contracts
IPushTransport { Platform Supports; Task<PushResult> SendAsync(PushAddress, PushPayload, ct); }
// PushResult (closed): Sent(providerRef) | TokenInvalid | TransientFailure(reasonKey) | Failed(reasonKey)
// PushPayload { RenderedTitle, RenderedBody,        — server-rendered in recipient locale (§1e);
//                                                     for PushRender=Generic these are the
//                                                     notif.private.* strings, NEVER content
//               DeepLink,                           — REQUIRED, ∈ contracts/deep-links.json
//               CollapseId,                         — = notification_id: OS-level dedupe under
//                                                     at-least-once dispatch
//               Category }                          — 1..9
// Impls: ApnsPushTransport (token-based .p8 JWT, HTTP/2) + FcmPushTransport (HTTP v1); credentials
// via Key Vault posture (2A) when OQ-3 lands; selection is environment/config, NEVER 9A. PROD BOOT
// WITH A BOUND PUSH CHANNEL AND NO CONFIGURED TRANSPORT THROWS at startup (L18 family, arch-tested
// exactly like IEmailSender/IPaymentService). Dev/E2E: DevSeamsPushTransport captures full payloads
// to notifications.dev_push_capture behind the DevSeams flag (never in prod DI — existing arch-test
// family) with a DevSeams-only diagnostic read endpoint: the "Mailpit for push", the E2E's oracle.

TrackedEmailSender : IEmailSender      // decorator over SmtpEmailSender — the S3-ratified one-line
// subsumption. Same interface, same template keys, same call sites; every send now writes a
// deliveries row (channel=email, template_key, outcome, provider ref). S3's mails are retroactively
// delivery-tracked. No new email interface; the SMTP transport is untouched; a prod relay swap
// stays one config change.

INotificationMetrics { DeliverySummary(sinceUtc); }   // the desk-tile read seam, computed from
                                                       // deliveries rows (received, not emitted)
```

### 1c. OpenAPI delta (all consumer routes `/v1/me/*`, S3's URL law — no account id expressible; reuse `CursorPage`, `Problem`, `OpaqueId`; new components `NotificationItem`, `NotificationPrefs`; ids `ntf_`, `dlv_`)

| Route | Verb | Auth | Shape | Notes |
|---|---|---|---|---|
| `/v1/me/notifications` | GET `?cursor=` | user | → `CursorPage<NotificationItem>` | chronological; `NotificationItem {notificationId, category, messageKey, variables, deepLink, createdAt, readAt?, pinned}` — client localizes from its ×4 catalog (DR-7.7); `variables` = opaque refs + canonical scalars ONLY (§1f); person-refs resolved at read time via `IAccountDirectory` — a tombstoned subject renders as the reserved `subject.removed` key, never a stale handle; `pinned` derived `category==8`, pinned rows sort first; dismissed rows absent |
| `/v1/me/notifications/unread-count` | GET | user | → `{notifications: int}` | the 5.9 per-tab badge; additive fields (matches, requests) arrive WITH their slices — never reserved-fake |
| `/v1/me/notifications/read` | POST | user | `{notificationIds[]}` → 204 | batch mark-read, idempotent; foreign/nonexistent ids silently skipped inside the self-scoped predicate (no oracle) |
| `/v1/me/notifications/read-all` | POST | user | 204 | |
| `/v1/me/notifications/{id}` | DELETE | user | 204 · 404-absence | dismiss. **A category-8 id returns 404 byte-identical to a foreign/nonexistent id** — undismissable as ABSENCE via the ownership predicate (`WHERE id=@id AND account=@actor AND category<>8`): cat-8, foreign, and nonexistent are ONE zero-row branch, one timing profile |
| `/v1/me/notification-prefs` | GET | user | → rows for categories 1–7, 9 | quiet-hours prefs; **category 8 ABSENT from the readable set** (absence law) |
| `/v1/me/notification-prefs/{category}` | PUT | user | `{quietStart, quietEnd, timeZone}` → 204 · 404 | per-category quiet hours; **`PUT /…/8` is 404 wire-identical to `PUT /…/17`** (S3's exact pattern); timeZone = IANA id validated against the platform tz db; overnight windows legal |
| `/v1/me/notification-prefs/{category}` | DELETE | user | 204 · 404-absence | clear window; `/8` ≡ `/17` |

Push-consent toggles stay on S3's existing `/v1/me/push-consents` routes — S4 adds NO second consent surface (one truth). Design/06 reconciliation recorded: "account & safety visibly locked on" renders CLIENT-side as a static informational row — token law 3 forbids a disabled control, and the server offers no affordance a toggle could even bind to. Request DTOs carry zero trust fields (arch scan extended: `pinned|category|gate_critical|delivery`).

### 1d. Deep links — `contracts/deep-links.json` (NEW contract artifact)

Backend-owned, consumed read-only by both clients (the OpenAPI-contract pattern): `{deep_link_key → per-platform route}`. S4 keys: `inbox`, `settings.export`, `settings.deletion`, `settings.sessions`, `settings.account`. Closed catalog. Lint, both directions: every row's key resolves; every catalog key is used by ≥1 row (no dead links). Client law recorded for the activation slice: unknown key → open inbox, never a dead tap. Web fallbacks are S9's mapping later.

### 1e. i18n — server-rendered push/email, client-rendered inbox

- **Push and email text is rendered SERVER-side in the recipient's locale** (from `IDeliveryDirectory.Locale`) out of `i18n/server/{en,es,pt,zh-Hans}.json`. Reasoning (§12 ruling 9): OS notification surfaces have no fallback logic — a new row key pushed to an old client build is broken text forever; server render kills the version-drift failure mode entirely, and the Generic-mode minimization (§1f) must be server-side anyway. Inbox items stay `messageKey + variables`, clients localize (DR-7.7, unanimous).
- **Executes S1 §12.12's recorded delegation:** S4 is the first server-side runtime locale consumer. **No 9A mirror is created** — `i18n/locales.json` stays the one truth; a drift test asserts server catalogs ≡ locales.json ≡ the `/v1/client-config` locale set.
- **`contracts/message-keys.json` additions ×4 (i18n-lint armed; every key has an emitter):** per deliverable row — `notif.export_ready.{title,body}`, `notif.deletion_scheduled.*`, `notif.deletion_canceled.*`, `notif.sessions_revoked.*`, `notif.email_changed.*` (inbox, client catalogs) and matching `push.*` pairs (server catalogs) — plus the reserved privacy set `notif.private.title`, `notif.private.body` (the Generic render), `subject.removed`, the NEW `email.deletion_cancel_notice.*` (§7's escalation row — the one email S3 never sent), and the category-8 footer keys per 5.42 ("sent for account security — this category can't be muted · unsubscribe from everything else"). Existing S3 email template keys are REUSED, not duplicated. Neutral-plain register on every cat-8 string (token law: danger and candy never on safety surfaces); the client rendering guidance is recorded in the row's registry entry. The closure lint cross-checks rows ↔ keys ↔ both catalog sets.

### 1f. The no-PII + lock-screen laws (P3, structural)

- **Variables law:** `Vars: map<name, SubjectRef(opaque usr_/crw_/… — resolved at render/read, NEVER a stored handle) | Scalar(canonical value — client localizes) | Translatable(text — T8 seam, zero producers at S4)>`. A gate test asserts no stored `variables` value matches handle/email/free-text patterns — the no-denormalized-PII law enforced, not assumed. Purging a subject never requires sweeping other users' inboxes; the counterpart drill (§10) proves it.
- **Lock-screen privacy (token law 5) as server truth:** the row's `PushRender` decides what enters the vendor payload: `Full` (rendered title+body) · `SenderOnly` (title only) · `Generic` (the `notif.private.*` strings — sender class only, never content). Full content NEVER enters an APNs/FCM payload for non-Full rows — minimization at the wire, not a display flag the OS may ignore. **`IPrivacyModeResolver` seam, conservative default:** Generic for any row carrying a person variable, until S19 registers real Con-Mode presence — the privacy-safe direction when the input is unknown, tested on both branches with a fake resolver. All six S4 rows: `Full` (no person variables, no UGC; cat-8 neutral register).

## 2. Schema DDL (schema `notifications`, owned solely by NotificationsDbContext; region + lawful_basis NOT NULL on every subject row — L21; the fan-out system actor INHERITS the recipient's region per S1's subject-inheritance rule; ddl-lint pii-patterns += `notifications.*`, red-fixture-proven; zero cross-module FKs or joins — `account_id`/`device_ref` are opaque refs)

```sql
CREATE SCHEMA notifications;

CREATE TABLE notifications.inbox_items (        -- THE durable record (surfaces Part 1) = the logical notice
  notification_id text PRIMARY KEY,             -- ntf_ ULID, deterministic from (source_event_id, row_key, recipient)
  account_id      text NOT NULL,                -- recipient; opaque usr_, NO FK (1A)
  row_key         text NOT NULL,                -- registry-validated at write
  category        smallint NOT NULL CHECK (category BETWEEN 1 AND 9),
  source_event_id text NOT NULL,                -- 3A provenance
  message_key     text NOT NULL,
  variables       jsonb NOT NULL,               -- opaque refs + canonical scalars ONLY (§1f gate test)
  deep_link       text NOT NULL,
  created_at      timestamptz NOT NULL,
  read_at         timestamptz, dismissed_at timestamptz,
  CONSTRAINT cat8_undismissable CHECK (NOT (category = 8 AND dismissed_at IS NOT NULL)),
  region text NOT NULL, lawful_basis text NOT NULL,
  UNIQUE (source_event_id, row_key, account_id) -- at-least-once replay → exactly-one notice, in the database
);
CREATE INDEX ix_inbox_account ON notifications.inbox_items (account_id, created_at DESC)
  WHERE dismissed_at IS NULL;

CREATE TABLE notifications.deliveries (         -- per-channel tracking + THE dispatch queue, one table
  delivery_id     text PRIMARY KEY,             -- dlv_ ULID
  notification_id text REFERENCES notifications.inbox_items(notification_id),
                                                -- NULL for tracked producer email (S3's transactional
                                                --   mails have no inbox row BY DESIGN; attributed via template_key)
  account_id      text,                         -- NULL only for pre-account transactional mail
  channel         text NOT NULL CHECK (channel IN ('push','email')),
  device_ref      text,                         -- opaque dev_ ref, no FK
  template_key    text,
  state           text NOT NULL CHECK (state IN ('scheduled','sent','failed','escalated','suppressed','token_invalid')),
  reason_key      text,                         -- suppress/hold provenance: consent | quiet_hours | no_address | payload_invalid | …
  attempts        int NOT NULL DEFAULT 0,
  deliver_after   timestamptz NOT NULL,         -- quiet-hours/batching hold + retry schedule; partial index = the queue
  provider_ref    text,
  created_at timestamptz NOT NULL, resolved_at timestamptz,
  region text NOT NULL, lawful_basis text NOT NULL
);
CREATE INDEX ix_deliveries_due     ON notifications.deliveries (deliver_after) WHERE state = 'scheduled';
CREATE INDEX ix_deliveries_metrics ON notifications.deliveries (channel, state, created_at);

CREATE TABLE notifications.prefs (              -- quiet hours (design/06); category 8 UNREPRESENTABLE
  account_id  text NOT NULL,
  category    smallint NOT NULL CHECK (category BETWEEN 1 AND 9 AND category <> 8),
  quiet_start time NOT NULL, quiet_end time NOT NULL,   -- local wall clock; overnight windows legal
  time_zone   text NOT NULL,                    -- IANA; DST resolved via tz db (QuietHoursMath)
  updated_at  timestamptz NOT NULL,
  region text NOT NULL, lawful_basis text NOT NULL,
  PRIMARY KEY (account_id, category)
);

CREATE TABLE notifications.push_consent (       -- module-OWN rebuildable projection of events_consent
  account_id text NOT NULL,
  category smallint NOT NULL CHECK (category BETWEEN 1 AND 9 AND category <> 8),
  enabled boolean NOT NULL, updated_at timestamptz NOT NULL,
  region text NOT NULL, lawful_basis text NOT NULL,
  PRIMARY KEY (account_id, category)
);  -- notifications NEVER reads identity.push_category_consents; the consent STREAM is the shared
    -- truth, each consumer projects its own. Absent row = ON for 1–7, OFF for 9.
```

Notes: the inbox insert IS the inbox delivery (no deliveries row for the inbox channel). Suppressed sends are terminal `deliveries` rows with a reason, never silence — metrics and audit need the negative space. No queue table, no Redis. NO shadow token-suppression table: `TokenInvalid` clears the token in `identity.devices` via `IPushTokenFeedback` — single store per fact; the next dispatch simply finds no address. Push tokens, emails, locales, consents are never copied into this schema — read at dispatch time through `IDeliveryDirectory` (a deleted account's pending deliveries resolve to null-profile → suppressed, recorded). `notifications.dev_push_capture` exists under DevSeams only (never in prod migrations' seed path; arch-tested).

## 3. 4A policy entries (S3's chokepoint machinery consumed as-is; matrix + boot-refusal suites regenerate)

| action | actors | TargetRule | accountState axis | denyMode | notes |
|---|---|---|---|---|---|
| `notifications.inbox.list` / `.unread_count` | user | SelfOnly | any | DenyAsAbsence | IsReadPath; readable in grace (a deleting user still sees "deletion scheduled" — rights-adjacent reads survive, S3 grace law) |
| `notifications.inbox.mark_read` / `.mark_all_read` | user | SelfOnly (ids folded into the self-scoped predicate) | active·suspended | DenyAsAbsence | not in the S3 grace rights set |
| `notifications.inbox.dismiss` | user | OwnedResource(notification) — resolver treats category-8 rows as ownerless: cat-8, foreign, nonexistent are ONE zero-row branch | active·suspended | DenyAsAbsence | |
| `notifications.prefs.read` | user | SelfOnly | any | DenyAsAbsence | IsReadPath |
| `notifications.prefs.set` / `.clear` | user | SelfOnly | active·suspended | DenyAsAbsence | prefs are product settings, not rights — denied in grace; category 8 has no row to gate (unrepresentable in table + route 404) |
| `notifications.dispatch.execute` | system only (the worker) | ActionScoped | — | DenyStandard | internal verb, NO HTTP mapping (S3 `identity.deletion.execute` precedent); `requires_reason=false` — delivery rows are provenance. ONE system verb: escalation is a dispatch outcome, not a second verb (§12 ruling 13) |

Ownership resolver registered for `notification` (indexed single-row lookup, predicate-folded). Everything else is SelfOnly — deliberate surface minimization: one resolver, no new boot-refusal cases beyond the generated set.

## 4. 9A config entries (`notifications.config.json`, S1 union-merge; every entry has a live S4 consumer — the ratified dead-tunable lint holds)

| key | scope | type | v0 | bounds | reason? | consumer |
|---|---|---|---|---|---|---|
| `notifications.dispatch.poll_seconds` | ops | int | 5 | [1,60] | false | dispatcher loop |
| `notifications.dispatch.batch_size` | ops | int | 500 | [50,5000] | false | fan-out/dispatch page size |
| `notifications.push.max_attempts` | ops | int | 3 | [1,10] | false | retry ladder before terminal failure |
| `notifications.push.retry_backoff_seconds` | ops | int | 30 | [5,600] | false | retry scheduling (exponential base) |
| `notifications.inbox.retention_days` | founder | int | 365 | [30,3650] | true | 13A retention_expiry sweep of READ, non-cat-8, non-dismissed-pending items; cat 8 retained until account deletion (durable record; OQ-1) |
| `notifications.deliveries.retention_days` | founder | int | 365 | [90,3650] | true | 13A sweep of resolved delivery rows — the statutory-dispatch evidence window (OQ-2) |

**Structurally NOT config, each a refusal:** category-8 immutability and its quiet-hours/consent exemptions (unrepresentable in types, CHECKs, and routes — a desk tunable that could silence safety notices must be impossible; the S3 age-floor logic) · the taxonomy registry itself (checked-in, linted, code-reviewed — a desk edit must not invent or silence a notification class) · `GateCritical` flags and `PushRender` modes (safety semantics, registry code) · transport selection and credentials (environment/Key Vault, DevSeams-family law, S3 §1b precedent) · batching windows (NO entry exists — the row-declared `Batched(configKeyRef)` mechanism ships, its first 9A entry arrives WITH S13's first batched row; the lint fails any Batched row until then) · escalation delay (none — the retry ladder IS the delay; §12 ruling 12).

## 5. 10A quota keys

**Zero live keys — asserted with reasoning (P2's position, adopted over P1/P3's fallback quota):** every notice is system-initiated off a domain event already gated by ITS producer's 4A/10A rows; consumer mutations here (read/dismiss/prefs) are self-scoped idempotent single-row writes; notification VOLUME is bounded upstream by producers' own quotas (where "budgets" live per the premium matrix). Mail-bombing via escalation is structurally bounded WITHOUT a quota: the deterministic notification id means one logical notice can create at most one escalation email delivery, and `max_attempts` bounds retries. P1/P3's `notifications.email_fallback.daily` key would be a DEAD TUNABLE at S4 — category 8 bypasses it structurally (statutory notices always deliver) and no non-8 gate-critical row exists — which the S1-ratified no-dead-config lint forbids. The key's naming convention is reserved in the registry doc; it arrives with the first non-cat-8 gate-critical row (S21's D17 surveys). Stated so nobody retrofits a second brake here, and so nobody adds a per-recipient push cap either (it would eventually silence category 1 legitimately; per-category consent + quiet hours are the user's real levers).

## 6. 13A store registrations + export registry (the S3 export⋈purge cross-gate FAILS THE BUILD until these land, by design)

| store | account_deletion | statutory_erasure | minor_purge | consent_revocation | retention_expiry | orphaned_blob |
|---|---|---|---|---|---|---|
| `notifications.inbox_items` | Delete (recipient-keyed; **counterpart purges structurally unnecessary — no other subject's PII was ever denormalized in; the §1f variables-hygiene gate test is what makes this claim safe**) | Delete | Delete | NotApplicable (consent gates DELIVERY, not the record of what the platform told you — reason registered) | `inbox.retention_days` sweep (read, non-cat-8) | n/a |
| `notifications.deliveries` | **Pseudonymize** (HMAC re-key account ref — delivery receipts are compliance evidence a statutory notice was dispatched; the S3 deletion_jobs precedent; safe because rows carry zero content and zero raw addresses by construction) | Pseudonymize | Delete | n/a | `deliveries.retention_days` sweep | n/a |
| `notifications.prefs` | Delete | Delete | Delete | n/a | n/a | n/a |
| `notifications.push_consent` | Delete (projection; stream keeps S1 OQ-1a posture) | Delete | Delete | recompute rows for revoked categories (mirrors identity's projection registration) | n/a | n/a |
| `notifications.dev_push_capture` | NotApplicable with reason (DevSeams-only, never exists in prod; arch-tested) | | | | dev-only TTL | n/a |

**Export contributors, same commit:** `inbox_items` (the recipient's notification history IS Art. 15 subject data) and `prefs`; `deliveries` = `NotExportable(reason: transport receipts — derived operational telemetry, zero content; the subject-facing record is the exported inbox item)`; `push_consent` = `NotExportable(derived; source consent events already exported by identity's events_consent contributor)`. Purge-completeness suite extends: seed → every class → zero residue / asserted pseudonymized shape; plus the keyed-by-something-else drill (deliveries keyed by delivery_id reached via account_id — the S2 invocation-id scar) and the counterpart drill (delete subject X → recipient Y's row about X still renders as `subject.removed` — proves the no-denormalization law rather than assuming it).

## 7. Notification taxonomy rows (closure rule — the baseline goes from asserted-zero to REAL)

The registry seeds ALL NINE category rows verbatim from the surfaces spec with `OwningSlice`. **Six bindings ship — every producer event that exists today, all category 8** (pinned, undismissable, neutral-plain, quiet-hours-exempt, consent-exempt, GateCritical — design/02 5.9 + 5.42 exactly):

| binding (event → row) | inbox | push | email (ChannelPlan) | notes |
|---|---|---|---|---|
| `identity.export_ready` → data-export ready | yes | yes | ProducerDirect (`EmailByProducer` — lint proves no double-send) | S3's send survives verbatim (ratified §1b); push+inbox added "at S4 on the same event" as S3 §7 promised |
| `identity.deletion_scheduled` → deletion scheduled | yes | yes | ProducerDirect | in-tx sequencing is the producer's law |
| `identity.deletion_canceled` → deletion canceled | yes | yes | **FallbackOnPushFailure** | **THE escalation row + the E2E vehicle for the ledger outcome.** S3 sends NO email for this row, so it exercises the fallback machinery honestly — and it genuinely should escalate: a canceled deletion you didn't cancel is an account-takeover signal. New key `email.deletion_cancel_notice.*` |
| `identity.deletion_completed` → deletion completed | **no** | **no** | ProducerDirect ONLY | fires as the last act around the shred; devices are purged and the account's inbox is purged by the same pipeline — an inbox row would be fabricated surface that the purge-completeness suite would then have to special-case (§12 ruling 5) |
| `identity.session_family_revoked` → sessions revoked | yes | yes | ProducerDirect | the theft alarm keeps its email; push+inbox added |
| `identity.email_changed` → email changed | yes | yes | ProducerDirect (to the OLD address) | the tripwire targets the old mailbox; push to still-owned devices is a live takeover alarm the old mailbox may miss (§12 ruling 8) |

Categories 1–7 and 9 ship as spec-transcribed rows with ZERO bindings and named owning slices (S13, S14, S15, S18, S19, S21, S22, S27, S33; 9 → S17-era) — asserted by the lint baseline (exactly six bindings). Category-9 delivery is structurally refused absent a granted consent row (opt-in only; no writer exists until its DR-7.1 first-touch surface; no unsubscribe endpoint for a sender that cannot send). **Recorded non-rows:** S3's transactional auth mails (precede an account / ARE the authentication act — outside the taxonomy per S3 §7, now delivery-tracked via `TrackedEmailSender`) · battle-challenge modal (S22) and 12A-r romantic modal (S20) — real-time interactions, not notifications, per the spec's own words · operator paging (S5 disposition 14).

**The closure lint (`tools/notification-lint`, gate lane, red-fixtured):** every binding references exactly one row; every row has exactly one category; every bound row's keys exist ×4 in BOTH catalog sets (client message-keys, server render catalogs); every DeepLinkKey resolves and every catalog key is used; cat-8 rows must be GateCritical; cat-9 rows must be opt-in; any `Batched` row fails until S13; `EmailByProducer` rows must have no fallback plan (no double-send); `taxonomy.json` drift-gated. **Plus the arch-test caller allowlist (the 15A-router rule pattern applied to delivery):** `IEmailSender` callers ∈ {identity's enumerated transactional sites, notifications}; `IPushTransport` referenced by the notifications module ONLY — the only write paths into `inbox_items`/`deliveries` are registry-keyed, so no notification can exist without a row.

## 8. BUILD.md §9 seams made concrete

| Seam | S4 concrete form |
|---|---|
| Schema-per-context (1A) | fourth module on the template; schema `notifications`; identity read via `IDeliveryDirectory`/`IPushTokenFeedback` contracts + own stream projections; producers never reference notifications (3A-only ingress); zero cross-module joins/FKs; correct dependency direction — identity never learns notifications exists |
| Transactional outbox (3A) | the whole slice IS the outbox's consumer half: producer event + mutation are one tx upstream; fan-out is a watermarked projection; notification-worthiness costs producers ZERO new writes — why the spine retrofits onto S3 without touching it |
| Foreign-event skip (§8 cl.7) | TWO non-vacuous harnesses: fanout (audit stream) + consent (consent stream) — the first cross-module external consumers, the exact case clause 7 exists for; fed a ledger event → watermark advances, zero rows |
| Server-authoritative trust (L20) | render mode, category, GateCritical, pinned, exemptions — all server-decided from types, never client input; request DTOs trust-field-free (arch scan extended) |
| Silent rejection / absence (token law 3) | cat-8 dismiss ≡ foreign ≡ nonexistent (one predicate, one timing profile); `PUT /prefs/8` ≡ `PUT /prefs/17`; category 8 absent from every readable/mutable set; suppressions recorded internally, never surfaced |
| One limit surface (token law 4) | no user-facing limit exists in this slice (stated; zero quota keys) — no second deny shape introduced |
| Lock-screen privacy (token law 5) | `PushRender` + `IPrivacyModeResolver` (conservative default: Generic when a person variable is present), wire-level minimization, both branches tested; Con-Mode is a resolver registration at S19, not a payload redesign |
| Buy-vs-build / vendor seams | `IPushTransport` ×2 impls + DevSeams capture transport (never in prod DI, arch-tested); `TrackedEmailSender` decorator = the S3-ratified one-DI-line subsumption, honored verbatim |
| Money/gate-door secrets fail CLOSED (L18) | prod boot with a bound push channel + unconfigured APNs/FCM throws; SMTP posture inherited |
| Deterministic math in pure libs | `QuietHoursMath` golden vectors (cross-midnight, DST both directions, no-DST zones, tz-invalid); backoff arithmetic pure; no LLM anywhere |
| Region-first PII (L21) | region + lawful_basis NOT NULL ×4 tables; fan-out inherits the RECIPIENT's region; ddl-lint extended, red-fixtured |
| i18n ×4 (DR-7.7) | push/email server-rendered in recipient locale from `i18n/server/` catalogs (S1 §12.12 delegation executed, drift-test, no 9A mirror); inbox keys + canonical variables, clients localize; i18n-lint armed |
| Consent WRITTEN, not just honored | S4 honors ONLY consents S3's surfaces write (PushCategory rows via the stream); cat-9 refused until a writer exists |
| Analytics written AND received | `notifications.dispatched/sent/failed/escalated/suppressed` behavioral events read BACK in the E2E; `INotificationMetrics` computes from deliveries rows and renders the S5 desk tile whose numbers the E2E asserts MOVE — the §3 G3 "100% owned-channel reachability" metric's data source, flowing three gates early |
| Real-or-honestly-dark (L6) | unbound rows declared with owning slices; deletion_completed declares no inbox/push rather than pretending; batching honest-dark (mechanism typed, zero rows, lint-fenced); marketing has no sender, no toggle, no unsubscribe here; no reserved-fake payload fields (S21 actions / S22 ttl arrive as additive registrations) |
| Idempotent-under-race | deterministic ntf_ id + UNIQUE (source_event_id, row_key, recipient); dispatcher claim = guarded UPDATE CAS (two-instance forced-race test); mark-read/prefs idempotent upserts |
| Messy input (L23) | vendor feedback (TokenInvalid, malformed) → recorded outcome + identity-owned token clear, never a crash-loop; unknown event payload shape → dead-letter `failed(payload_invalid)` delivery row, watermark still advances |

## 9. Dependency classification (every not-yet-built or vendor system S4 touches)

| Dependency | Class | Handling |
|---|---|---|
| S1 substrate (3A/4A/9A/10A/13A, Deterministic, DevSeams, RequestContext) | built | consumed as-is; Phase-2a Deterministic addition only; zero engine mutations (one resolver registration) |
| S3 identity | built + narrow Phase-2a | additive `IDeliveryDirectory` + `IPushTokenFeedback` in `Svac.Identity.Contracts`; `IEmailSender` DI re-registration (pre-ratified); **zero behavior migrations — all S3 call sites survive verbatim**; byte-identical proof + S1/S2/S3/S5/S7 suites + S3 E2E rerun green BEFORE builders fan out |
| APNs vendor | **seam-now (buy)** | `ApnsPushTransport` behind `IPushTransport`; .p8 key = Key Vault posture (OQ-3 gates prod deploy only); DevSeams capture in dev/E2E; prod-unconfigured throws; real-device proof belongs to the client activation slice + S26 store package |
| FCM vendor | **seam-now (buy)** | `FcmPushTransport`, second impl of the SAME interface — swap/add = one DI line |
| Email relay (prod) | swap-safe | `SmtpEmailSender` untouched under the decorator; Mailpit dev unchanged; prod relay = config |
| S5 admin desk | built | additive delivery-metrics tile on the dashboard skeleton reading `INotificationMetrics`; operator paging stays disposition 14 (S12) |
| SignalR (live badge/nudge) | deliberately unused | pull inbox + poll dispatcher at G0; promotion = one nudge publisher at S13, additive, no schema change |
| T8 translation (UGC in notifications) | seam-now | `Translatable` variable slot, pass-through renderer, zero producers; S13 wires the router call + ITS 13A cache registration + eval |
| Con-Mode presence (S19) | seam-now | `IPrivacyModeResolver`, conservative default (§1f) — the privacy rule cannot arrive late because it defaults ON |
| Quiet-hours/batching engine | built now, honestly scoped | quiet hours fully live (prefs + dispatcher + golden vectors); batching = typed mechanism, fixture-tested, zero shipped rows, lint-fenced until S13 |
| D17 actions (S21) · battle modal (S22) · romantic modal (S20) | out / future registrations | additive registrations against this registry when they land; modals are NOT notifications by spec |
| Marketing opt-in surface + cat-9 senders (S17-era) | not read — deliberate | declared, writer-less, structurally refused (DR-7.1; S3 §12.12) |
| Clients (iOS/Android, S7 scaffolds) | not in build scope — contract frozen | inbox/prefs endpoints, deep-links.json, priming timing (DR-7.1) all pinned; activation = generated-client regen + wiring at the next client slice with Maestro evidence; HARDENED-GATE clause 5 = N/A-with-note (S3 precedent) |
| Web funnel (S9) | not read | deep-links.json is the shared artifact it maps web fallbacks onto later |
| **MUST-BUILD-FIRST blocking S4** | **none** | S1 + S3 landed; Mailpit in compose; S4 starts now |

## 10. Outcome moved + evidence at sign-off (THE HARDENED GATE for S4)

Ledger row: **"delivery-tracked notice lands via push AND email fallback"** — B7 dies (S3 built the email door; S4 lands the taxonomy spine, the tracking, and the fallback machinery B7 actually names).

1. **Build + tests:** arch suite green with new rules red-fixture-proven: push-transport-outside-module ban, `IEmailSender` caller allowlist, DevSeams-transport-never-in-prod, prod-unconfigured-throws, trust-DTO extension, module-boundary non-reference both directions, category-8 unrepresentability at all four layers (type, CHECK, route, payload). `Svac.Tests.Notifications` <2s: QuietHoursMath golden vectors, registry round-trip + taxonomy.json drift, fanout idempotency under forced race, two-instance dispatcher claim race, escalation state machine, variables-hygiene gate test, both foreign-event skip harnesses, privacy-resolver both branches.
2. **Deterministic tools:** `tools/notification-lint` green + red-fixtured (binding without row, missing ×4 key in either catalog set, deep-link off-catalog or dead, Batched row, cat-9 non-opt-in, EmailByProducer double-send); i18n-lint, contract-lint, ddl-lint, ef-gate, locales drift test green.
3. **THE LIVE E2E** (`backend/e2e/notifications.e2e.mjs`, compose, two API instances, real Mailpit + DevSeams push capture): signup (S3 flow) → register device with push token → set quiet hours on category 3 + **404-on-8 drill** (`PUT /prefs/8` ≡ `/17` byte-identical) → request export → worker → `identity.export_ready` → **push captured with server-rendered ES-locale text + deepLink + CollapseId AND inbox item via `GET /v1/me/notifications`** (unread-count = 1, no PII literal in variables) → batch mark-read → **quiet-hours drill:** synthetic non-8 fixture row inside the window → delivery `scheduled` with `deliver_after` = window end, released on time; cat-8 event during the SAME window delivers immediately (exemption proven) → consent-off drill (disable a category via S3's route → consent projection converges → push `suppressed(consent)` recorded, inbox still lands — the durable-record law) → **THE email-fallback drill: second account, NO push token → request deletion, cancel → `identity.deletion_canceled` escalates → `email.deletion_cancel_notice` in Mailpit in recipient locale + delivery rows walk scheduled→escalated→sent** (the ledger outcome, both halves) → forced-transport-failure drill (DevSeams failure mode → retries exhaust → escalation fires) → token-invalid drill (transport returns TokenInvalid → `IPushTokenFeedback` → identity device token cleared + audited, next dispatch finds no address) → cat-8 dismiss ≡ foreign ≡ nonexistent byte-identical → counterpart drill (delete subject X → recipient Y's row renders `subject.removed`) → full deletion rerun: `deletion_completed` email in Mailpit, NO inbox/push artifacts → **purge read-back:** inbox/prefs/consents zero, deliveries pseudonymized, receipts survive → behavioral events read back, watermark advanced → desk tile renders MOVED numbers → foreign-event skip → fresh-boot clause (`down -v` → `up`, zero errors/restarts) → post-E2E zero-exception log sweep, both instances. **Suite run TWICE, identical.**
4. **S3 equivalence:** S1/S2/S3/S5/S7 suites untouched-green + S3 E2E rerun green with the decorator registered (every Mailpit assertion passes through `TrackedEmailSender`, now with delivery rows behind it).
5. **Metric wired:** delivery success rate by channel + escalation rate + suppression breakdown on the S5 desk tile, computed from `deliveries` rows (received, not emitted) — the G3 "100% owned-channel reachability" metric's source, flowing three gates early.

## 11. Open questions for Julien (genuine forks only; recommendation on each)

- **OQ-1 — Inbox retention posture (permanent product/privacy posture):** (a) sweep READ, non-cat-8 items at `inbox.retention_days` (v0 365; founder scope, bounds to 10 years — "never" is one desk edit); cat 8 retained until account deletion. (b) retain everything indefinitely (the maximal reading of "durable record" / "time only adds value"). **Recommendation: (a)** — a notification list is operational, not a memory object; the artifact-bearing surfaces (threads, meetup artifacts) are where "time adds value" lives.
- **OQ-2 — Delivery-receipt evidence window** (`deliveries.retention_days`, v0 365, founder scope): the period we can PROVE a statutory notice was dispatched. **Recommendation: ratify 365 conservative-reversible (S0/S1 interim pattern); counsel's L-1 pass may adjust.** Rows carry zero content and zero raw addresses, so the privacy cost of a longer window is minimal by construction.

## 12. Judge's synthesis record (conflicts + the two ruling contradictions)

1. **Architecture — unanimous, adopted:** fan-out is a 3A consumer; no notify RPC exists; producers integrate by event + registration. All three proposals independently converged; locked.
2. **Taxonomy representation: P2's typed code registry** (additive per-module registration files, emitted drift-gated `taxonomy.json`) over P1/P3's checked-in JSON — types make illegal rows uncompilable and match the 4A/13A registry idiom; P1/P3's Node closure lint and P2's arch-test caller allowlist BOTH adopted (the lint checks artifacts, the arch test closes the write path).
3. **RULING CONTRADICTION — the S3 email seam.** S3 §1b (ratified) reads: "S4 subsumes by ONE DI re-registration…; the interface, template keys, and call sites survive verbatim — that is the definition of seam-now." **P3's wholesale replacement of S3's six producer sends contradicts this and loses the point automatically. P2's single `export_ready` migration contradicts the same clause and loses that point too** (it is also a user-visible regression: a push-capable user would stop receiving the export email, whose link is the cross-device access path). Ruling: `TrackedEmailSender` decorator only (P1); all S3 sends survive verbatim, marked `EmailByProducer` with a no-double-send lint. Consequence: P3's `deliveries.notification_id NOT NULL` is also rejected (tracked producer mail has no inbox row); nullable per P1/P2.
4. **Escalation vehicle: P1's `deletion_canceled` row** (Email: FallbackOnPushFailure) — the one row S3 sends no email for, so the fallback machinery is exercised honestly with zero S3 mutation, and escalation is genuinely correct product behavior (a canceled deletion you didn't cancel is a takeover signal). P2's ChannelPlan vocabulary (ProducerDirect | FallbackOnPushFailure | Always | Never) adopted as the general mechanism.
5. **`deletion_completed`: P2's shape** (email only, no inbox/push) over P1's inbox-receipt — the completion event fires around the shred; the account's own inbox is purged by the same pipeline, so an inbox row is fabricated surface the purge-completeness suite would have to special-case. P1's honesty instinct (declare undeliverable rather than pretend) preserved in the row's stated reason.
6. **Quiet hours: DEFER (P2/P3) over P1's drop-to-inbox.** With `deliver_after`-as-queue (P2), release-at-window-end costs zero extra machinery — P1's own objection ("one scheduler more") dissolves, and deferral matches platform-native quiet-hours semantics (design/06). Not an OQ: P1 flagged it as a fork only because its design priced deferral wrong.
7. **Read state: per-item `read_at` (P2/P3) over P1's watermark** — design 5.9 shows per-row unread styling and per-tab badges; a watermark cannot represent partial reads. P1's fork resolved, not forwarded. Dismiss EXISTS for non-8 (P2/P3) over P1's no-verb-at-all — 5.9's "pinned undismissable safety rows" implies dismissability elsewhere; cat-8 refusal is absence (one predicate branch).
8. **`email_changed` keeps push (P1) over P2's Push: Never** — devices still belong to the victim after a hostile email change; push is the live takeover alarm the old mailbox may miss.
9. **Push/email server-rendered in recipient locale (P3)** over P1/P2's client-side loc-keys — OS surfaces have no fallback for a key an old client build lacks; server render kills the version-drift failure mode, and Generic-mode wire minimization must be server-side anyway. Inbox stays keys + canonical variables, client-localized (unanimous, DR-7.7). Executes S1 §12.12's delegation as drift-test-only, no 9A mirror.
10. **P3's privacy laws adopted wholesale:** no-PII-denormalization variables law + hygiene gate test + `subject.removed` render + counterpart drill; `PushRender` (subsumes P1's LockScreenBody and P2's ContentPolicy) + `IPrivacyModeResolver` conservative default; recipient-region inheritance.
11. **Identity seams: P3's split** (`IDeliveryDirectory` read + `IPushTokenFeedback` write) with P2's semantics, over P1's read-only `IContactDirectory` + shadow suppression table — token invalidation belongs in identity's own store (single store per fact, audited); P1's `push_suppressions` table is deleted from the design entirely. Consent deliberately NOT an interface (unanimous): module-own stream projection.
12. **Idempotency: deterministic ntf_ id from (source_event_id, row_key, recipient) + UNIQUE (P2/P3)** over P1's bare unique source_event_id, which breaks the first multi-recipient row (S14). P2's closed `RecipientSelector` adopted. P2's `email_fallback.after_minutes` config dropped — the retry ladder is the delay; a second delay knob is a near-dead tunable.
13. **RULING CONTRADICTION — dead tunables.** S1 §4 (ratified): "a config key with no registered consumer fails a lint." **P3's live batch-window map `{5:15}` with zero category-5 rows, and P1/P3's `email_fallback.daily` quota (structurally bypassed by cat-8 and with zero non-8 fallback rows at S4) both contradict it and lose automatically.** Ruling: zero 10A keys (P2, §5); batching = typed mechanism with no config until S13 (P2, with P1's lint teeth). P2's separate `fallback.execute` system verb also dropped — one worker, one verb; delivery rows are the provenance.
14. **Desk tile ships now (P1)** over P2/P3's deferral — BUILD.md §5 names "delivery metrics on desk" in the S4 row and S5's skeleton is built; the analytics law demands received-not-emitted proof, which the tile provides. P2/P3's behavioral events ALSO adopted (the G3 metric's stream).
15. **DevSeams push capture table + diagnostic read endpoint (P1)** — the "Mailpit for push"; a full-payload oracle lets the E2E assert rendered text, deep link, and CollapseId, which P2's providerRef-only loopback cannot.
16. **Reserved payload fields dropped (real-or-honestly-dark):** P1's `Actions[]` and P3's `priority`/`ttl` are additive internal shapes with zero users; S21/S22 add them as registrations. The genuinely hard-to-retrofit part — wire minimization — IS locked now via `PushRender`.
17. **Prefs API: per-category PUT/DELETE (P1/P3)** over P2's whole-doc PUT — it yields the 404-on-8 ≡ 404-on-17 drill in S3's exact idiom. Unread count: P2's dedicated endpoint with additive fields. Grace semantics: reads any-state (P1/P3), all mutations active·suspended (P2's prefs reasoning extended uniformly — none are in the S3 grace rights set).
18. **13A deliveries verb: Pseudonymize (P1/P3)** over P2's Delete — receipts are the evidence a statutory notice was dispatched (deletion_jobs precedent), and P3's zero-content-by-construction rows make retention safe. Retention knobs → OQ-1/OQ-2 (the only genuinely-Julien forks left; all three proposals flagged retention).

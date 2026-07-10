# SLICE S7 CONTRACT — client-skeletons (S7i ios · S7a android) (LOCKED)

**Gate:** G0 · **Actor journey finished:** user opens either app, either brand, and sees the DR-1.1 nav + the 5.14a signup shell — every screen honest, every state designed · **Units owned:** `ios/`, `android/`, root `maestro/` (new), `design/tokens.v1.json` + `tools/token-lint/` (new), `tools/egress-lint/` (new), graduation edits to `.github/workflows/ios.yml` / `android.yml`, arming of `tools/i18n-lint` · **Depends:** S0 (CI/flavors), S1 (contract v0) · **Blockers killed:** none new — makes B17's consumption half real; B18 stays dead by construction · **Ledger outcome:** brand-smoke Maestro ×4 listings green.

Synthesized from three proposals by the design judge. Per-conflict adoptions and the ruling contradictions are recorded in §13. One open question remains (§14); everything else is resolved with stated reasoning.

**Governing theses (grafted):**

- **From P1 (simplicity):** S7 is the S2 of the client side — a pure-scaffold slice whose backend delta is ZERO in every playbook dimension, **asserted by drift gates, not assumed**. Two native shells × two flavors, one token layer, one generated client proving the contract seam with one real call, the 5.13 state kit, the Maestro harness. No backend module, no store, no mutation, no permission ask, no fake success path anywhere.
- **From P2 (extraction-grade seams):** the clients are pure consumers whose internal layering (DesignKit / ApiKit / AppShell / Features, same names both platforms, dependency-direction linted) makes every later client slice an adapter registration, never shell surgery. Every future vendor or endpoint gets a typed seam now or an explicit ZERO with reasoning.
- **From P3 (privacy + i18n + residency):** S7 is the only moment the client half of the privacy invariants can be made structural. A client with zero persistent identifiers, zero third-party egress, zero region authority, one error surface, and a kit in which "disabled" does not exist cannot leak what it never holds and cannot render what the server never sent. i18n ×4 from the first commit is the seam that cannot be retrofitted.

**This slice contains zero latent-space surfaces.** No prompts, no model calls, no eval suite owed; the quality lane is the snapshot suite + Maestro ×4. The AI/ML Router is untouched.

---

## 0. Scope ruling

S7 owns:

- `ios/**`, `android/**` — both greenfield, brand-flavored from the first commit (7A, B18 discipline: S0's gate makes a flavorless first commit red).
- `maestro/**` at root — the 14A SHARED cross-platform E2E harness (root holds glue per CLAUDE.md; it cannot live inside either client). The ios.yml guard currently checks `ios/.maestro`; the graduation edit repoints it to `maestro/` — lanes B/C own those workflow files per CODEOWNERS.
- `design/tokens.v1.json` — machine-readable mirror of DESIGN.md's ratified token values (versioned like `contracts/openapi.v0.json`; S9 web inherits it free). DESIGN.md stays the authoritative prose.
- `tools/token-lint/`, `tools/egress-lint/` — node:test, zero npm deps, S0 tool pattern, red-fixture-proven both directions.
- Arming `tools/i18n-lint` with `.xcstrings` + Android `strings.xml` parsers (golden-vectored) — the activation event SLICE_S1_CONTRACT §1c planned.
- Graduating the four guarded jobs in `ios.yml`/`android.yml` (build-test, maestro-brand-smoke, codegen-openapi-client, string-catalog lint) from skip-with-note to real required runs. That graduation IS the slice's CI deliverable.

S7 does NOT:

- add, change, or read any backend code — `backend/**` untouched, proven by drift gates byte-identical (§2–§8);
- complete signup (S3 owns accounts; the shell terminates at a client-side gateway seam, §9d);
- ask for ANY OS permission (DR-7.1: never at first launch; push registration is S3/S4; no location, no camera — avatar-or-skip renders the picker affordance whose flow lands with S3);
- persist anything on device (§3): no keychain entries, no local DB, no caches of server data, no analytics SDK, no install UUID;
- build deck, DMs, anti-screenshot, deep links, dark-mode rendering, IAP, or web anything (S14/S13/S9/S26);
- render any fabricated data (L6): every tab shows its designed zero-data state; no fake counts, no seed people, no fake success;
- create prompts or evals (zero latent surfaces);
- touch `contracts/**` beyond proving zero drift (a client PR editing `contracts/` is a violation by CODEOWNERS ownership — B17/D9).

**The one structural law this slice makes true:** both apps, both flavors, build/test/run from single per-platform trees with brand as pure build flavor, consuming the OpenAPI contract read-only through generated clients — and the ×4 brand-smoke lane goes green and stays a required gate forever.

## 1. Client architecture (module API surface, client-internal)

### 1a. Stack (confirmables, decided — routine eng calls, not Julien forks)

- **iOS (`ios/`):** Swift 6 / SwiftUI, min iOS 17. Project single-sourced via **XcodeGen** (`ios/project.yml`); generated pbxproj git-ignored. Reasoning (P2 adopted over P1's checked-in pbxproj): a hand-maintained pbxproj is the highest-merge-conflict artifact in a parallel-agent repo, and CLAUDE.md mandates parallel-session safety; project.yml is diffable and the brand matrix is data. Layer-2 tool choice recorded per search-before-building; reversible in one command (commit the generated project, drop the tool).
- **Android (`android/`):** Kotlin / Jetpack Compose, minSdk 28, Gradle `productFlavors { weeb; friki }` — the idiomatic flavor mechanism IS the 7A requirement, zero custom machinery.
- **Typed clients:** iOS = **apple/swift-openapi-generator** (Apple-official SPM plugin, layer 1, already named in ios.yml's codegen comment); Android = **OpenAPI Generator** Gradle plugin, `kotlin` client with kotlinx-serialization (the standard; Fabrikt rejected — smaller ecosystem, no extra fidelity needed on a 2-path contract). Generated at build time from the checked-in canonical `contracts/openapi.v0.json` (repo file is canonical per contracts/README; the CI artifact `openapi-contract-v0` remains the cross-repo distribution channel and CI compiles against it as the B17 consumption proof). Generated code is never committed — no codegen-drift class exists.

### 1b. Internal layering (both platforms, same names so Maestro flows and reviews transfer)

`DesignKit` (tokens, component kit, state set) · `Strings` (catalogs) · `ApiKit` (generated client + transport + the one error mapper) · `AppShell` (tabs, mode context, routing, signup shell) · `Features/*` (one dir per future slice; only the Signup shell is non-empty at S7). Enforced by a per-platform dependency-direction lint test: Features never import each other; only AppShell composes — the 1A module-isolation discipline in client idiom. iOS = SPM local packages; Android = Gradle modules.

### 1c. Brand flavors (7A: from first commit)

- Brand = build flavor: iOS two schemes/targets over per-brand xcconfig + asset catalogs; Android two productFlavors. Brand is immutable at build time; no runtime brand switch exists, ever.
- **Single source stays `brands/*.json`; flavor files are LINT-VERIFIED against it** by extending `build/scripts/brand-gate.mjs`'s existing `flavorFiles` hook with two small per-platform parsers (xcconfig, gradle flavor block). Lint-verify, not generate — the S0-ratified pattern. (P3's codegen-from-brands step contradicted this ratification and loses the point; §13.)
- Brand delta surface in code is exactly the DESIGN.md table: `brand.primary`, `brand.celebration`, wordmark asset, `string_pack_id`, illustration set hook. Any other code reading a brand key fails token-lint. "Same sticker, different ink" is a build failure, not a review note.
- Bundle id / applicationId: canonical values in `brands/*.json` stay BLANK pending OQ-1 (§14). Dev builds use `.dev`-suffixed provisional ids in flavor files only, explicitly excluded from brand-gate parity until canonical is non-blank, at which point drift fails the gate. Release lane stays fail-closed as proven at S0. Nothing blocks S7 green. (P2's proposal to write canonical ids into `brands/*.json` now is rejected: store identity is a one-way door at first upload and OQ-1 is founder-owned; §13.)

### 1d. Network posture — one live call, fail-closed by absence

- **Consumed surface, exactly:** `GET /health` (debug diagnostics only) and `GET /v1/client-config` (apiVersion, locales, defaultLocale).
- **Debug/dev builds** call the compose backend and render `GetClientConfig` data on a **debug-build-only diagnostics screen** — the smallest thing proving contract → codegen → transport → running stack end to end (the hardened-gate live E2E in native form). Boot check: bundled catalogs ⊇ server `locales`; mismatch renders the neutral contract-mismatch state (5.13b family), never a crash.
- **Release builds contain no configured backend URL and make zero network calls** — fail-closed by absence, the client analog of money-door fail-closed. A release-config test asserts the URL and the cleartext exceptions are absent.
- No client-config cache, no TTL store (P2's cache dropped: it violates the zero-persistence inventory §3 and is S3+ behavior).
- **Tolerant readers, asserted:** both generated-client wrappers ignore unknown response fields (test with an extended fixture payload). Named beneficiary: a future additive `min_supported_client_version` on `ClientConfigResponse` (force-upgrade, needed before S26) — recorded in TODOS for the server slice that first serves it; no client UpdatePolicy machinery ships at S7 (the contract-mismatch state is the only honest version gate that exists yet).

### 1e. One error mapper per client (P3, adopted whole)

Total consumer error taxonomy, a single choke point: 2xx render · 429 → the ONE LimitReached surface (`limit_reached.generic`) · every other status → the ONE generic Problem surface (`error.generic`, neutral register) · transport-offline → 5.13b connectivity states. No per-status UI branches exist to write. **404-uniformity test:** feed the mapper 403/404/410 on a consumer read; assert byte-identical rendered state, zero user-visible or log-visible distinction (contract-lint already bans 403 on consumer reads server-side; this makes the client incapable of exploiting a leak). DesignKit ships NO "pending…" indicator component for deny/void-class operations — the leak-shaped component does not exist for R5/S20 to misuse.

## 2. OpenAPI delta

**ZERO paths, zero components, zero new message keys — asserted, not assumed** (S2 §1c precedent): CI proves `contracts/openapi.v0.json` and `contracts/message-keys.json` byte-identical across S7; backend arch suite untouched and green. If a skeleton need surfaces that the contract lacks, that is a versioned contract change negotiated with lane A, never a client workaround.

## 3. Schema DDL

**ZERO server tables.** `ddl-lint` and `ef-gate` prove zero delta.

**Device-local data inventory (the client analogue of a 13A registration), exhaustive at S7:**

| Store | Contents | PII | Wipe story |
|---|---|---|---|
| App sandbox (static) | fonts (OFL), catalogs, compiled assets | none | app uninstall |
| UserDefaults / DataStore | nothing written | none | n/a |
| Keychain / Keystore | no entries (no auth yet) | none | n/a |

No persistent client-generated identifier of any kind: no IDFA, no ad ID, no install UUID, no vendor ID read. A boot is anonymous by construction (server stamps ActorKind=anonymous, region from edge inference per S1). Signup form state is in-memory only and dies with the process. **Enforced two ways:** a client test asserts no persistence framework (CoreData/SwiftData/Room/DataStore) is linked into either app target; a gate test asserts that after a Maestro boot-and-browse run the app container holds zero files outside the static-asset allowlist. Client-side principle made enforceable now: any future on-device cache of server data must name the server store it mirrors and inherit its purge class; the inventory test is what makes an unregistered client cache red.

## 4. 4A policy entries

**ZERO.** S7 ships no mutation endpoint and no server verb; the generated 4A matrix suite asserts exactly this delta (absence as a test — a stray endpoint is a violation, not a gap). Client discipline made structural instead: entitlement logic is never encoded client-side — renderers key on DTO field PRESENCE (generated optionals), so a client can never disagree with the policy engine.

## 5. 9A config entries

**ZERO.** The tempting candidates are all dead tunables at S7: min-client-version (no released client), feature flags (no features), backend URL (a compile-time build-config value — a desk edit must never repoint a client fleet). `GET /v1/client-config` remains the single boot truth; no client-side mirror of any tunable exists (S1 §12.12 precedent). Future keys named, NOT created (created by the slice that first serves them): `client.min_supported_version.{ios,android}`.

## 6. 10A quota keys

**ZERO.** No quota-consuming action exists. The `LimitReached` schema arrives typed in both generated clients for free via codegen; the ONE limit-reached surface (DR-7.3, token law 4) ships NOW as the DesignKit component bound to `limit_reached.generic`, so the first quota-bearing slice mounts it instead of inventing a second deny UI.

## 7. 13A store registrations

**ZERO server-side.** The 13A CI gate re-runs green with nothing new registered. Device side is covered by §3's inventory + tests.

## 8. Notification taxonomy rows

**ZERO rows touched** (taxonomy, closure rule and all, is S4's). The Inbox tab ships the notification-inbox **scaffold** (design §2.9: chronological list shell, category icon slots, per-tab unread badge slots, category-8 pinned/undismissable rendering rule) — the shipped tab renders its designed EMPTY state; the category rendering rules exist as DesignKit components exercised only in the debug gallery + snapshot tests against fixtures, never live data (L6). No push registration, no permission prompt. Lock-screen-privacy rendering (token law 5) is recorded as S4's obligation; nothing to render yet.

## 9. What actually gets built (the deliverable, enumerated)

### 9a. Token layer (DESIGN.md → code, DR-5.1)

- `design/tokens.v1.json`: canonical transcription of DESIGN.md's ratified values — palette including the Choco dark set (carried from the start per DR-6.3, rendered nowhere until its G3 slice; **apps force light mode at S7**), type scale, spacing, radii, motion durations, register rules as token groups, brand-delta keys.
- Platform token layers `DesignKit/Tokens.swift` / `designkit/Tokens.kt`, hand-written, values only from the manifest. `tools/token-lint` verifies (a) manifest ↔ DESIGN.md table values, (b) each platform layer ↔ manifest value-for-value, (c) brand-delta keys ↔ `brands/*.json`, (d) no brand hex leaks outside `brands/*.json` (brand-gate's leak rule now has client trees to bite on).
- **Token laws are type-level:** no locked/grayed/disabled decorative variant exists in DesignKit (law 3 — absence, not disablement; sole ratified exception: create-crew Premium secondary CTA in the Crews empty state, allowlisted with citation). No danger token reachable from playful-register components (law 2). No decay/expiry-shame token exists (law 6). Black-900 absent from neutral components. The variant enums simply lack the cases; a deterministic kit test proves no `disabled` state token resolves.
- Fonts: M PLUS Rounded 1c OFL bundled in both apps, never fetched. zh-Hans stack (DR-6.2 per-script, P3): M PLUS Rounded 1c for Latin/kana + explicit Han fallback — PingFang SC (iOS system) / Noto Sans SC (Android, bundled); a per-locale rendering snapshot test catches tofu and wrong-variant Han. Icons: Font Awesome Pro, Solid/Regular per register, wrapped in ONE `Iconography` layer per platform (buy seam — swappable asset dependency, never call-site glyph literals). DR-6.4 phone-only locked size classes set in project config now.
- Components built at S7: person-card frame (anatomy only, fixture-driven in gallery; gesture-equivalent button slots reserved per DR-6.1), chip, badge, modal, sheet, row, synergy pill (render-absent below band), StateView (§9e), the one LimitReached surface, the one generic Problem surface.

### 9b. App shell (DR-1.1, LOCKED)

- Fixed five tabs: Connect · Explore · Crews · Inbox · Profile. **Quests tab does not exist** (pre-G4 absence — no "coming soon").
- `ModeContext` closed/sealed type: `Online` (default, ONLY constructible case at S7) | `ConPresent(conRef)` | `Nakama` — declared closed now so S19/S34 add a case + a renderer, not a nav rewrite. Mode is context, not place; no manual mode switch UI exists or ever will.
- Trunk test baked into the one shared shell layout and asserted in Maestro on every screen: brand mark + active tab always; mode chip renders only when non-Online (i.e., never at S7 — asserted, since a visible chip here would be fabrication).
- Each tab renders its designed honest zero-data state: Connect = pre-first deck posture · Explore = between-contests rail (no fake dates) · Crews = "get invited" hero + the create-as-Premium sole secondary-CTA exception · Inbox = matches-empty + Requests section slot (DR-2.2, empty) + notification-inbox scaffold (§8) · Profile = own-profile preview-first empty. Every empty state routes to a live action, never a dead end.

### 9c. Signup shell (5.14a)

Handle → verified-email step → birthdate attest (neutral-plain register: 400/500/700 only, no candy, no mascot; hard under-13 refusal copy) → avatar-or-skip → one fandom tag. Fully navigable with real client-side validation (handle charset, birthdate) and keyed strings ×4. Submission crosses the **`SignupGateway`** seam (protocol/interface per client, ~5 members mirroring the 5.14a fields — the S3 seam). **S7 registers only `UnavailableSignupGateway`, in ALL build configurations:** every submit resolves to the one generic could-not-send pattern (`error.could_not_send`). No debug mock success path exists (P1 over P2/P3's debug stubs, §13) — the Maestro flow walks to the honest gateway-refusal state, which is the journey's designed end. No fake success path ever exists to remove later. Notification permission is NOT asked here (5.14: after first value moment); DR-7.1 pre-permission priming screens ship as DesignKit components with fixture copy in the gallery only, unused until S4. The 5.14b short-test prompt likewise ships in the state kit only, unreachable in the release flow until S3.

### 9d. 5.13 state-set scaffold (all 23 states)

One `StateView` component (illustration slot [FA duotone], title 800, body 500, single CTA slot) + a typed state catalog enumerating every state in design 06 + DR-2.1 §3: 5.13a empties (deck/matches/nakama/cosplay) · 5.13b gate/pending/presence/battle-pause/connectivity + contract-mismatch · 5.13c dignity screens (age-estimation lockout, counterpart protection — neutral register, verified register-pure by token-lint) · plus 5.12 pre-first-con. Every state keyed ×4 locales ×2 brand packs NOW (the copy is the retrofit cost). States reachable at S7 render live in the tabs (the empties); the rest are reachable ONLY through a **debug-build-only state gallery** — never compiled into release (the L15 posture: design-DISPLAY for reconciliation and the §8.5 UX crawl, never shipped showcase). Every state snapshot-tested per flavor per locale. The ledger's count (23) is the acceptance number; if faithful transcription yields a different enumeration, that is a NAMED Phase-1 checkpoint flag, never a silent adaptation.

### 9e. i18n ×4 (the seam that cannot be retrofitted)

- Catalogs per platform: one `common` catalog (neutral register + shared keys) + per-brand overlay keyed by `string_pack_id` (weeb = en/ja-led voice, friki = es/pt-led voice per DESIGN.md) × 4 locales (en, es, pt, zh-Hans). iOS `.xcstrings`, Android per-flavor `res/values-*/strings.xml`.
- `tools/i18n-lint` ARMS: every `contracts/message-keys.json` key must exist ×4 in each client's catalogs, plus client-local key parity ×4. New parsers golden-vectored.
- Zero hardcoded strings: SwiftLint rule / Android Lint `HardcodedText`, build-failing from commit one.
- DR-6.2 layout: ES string tested first, +30% width headroom, pseudo-locale enabled in debug builds, chips wrap never mid-word truncate, one-line ellipsis with tap-for-full on names.
- DR-7.7: locale follows device locale with fallback chain to `defaultLocale`; **no in-app language picker at S7** (device-only — one less stored preference, one less fingerprinting bit; consistent with §3 zero persistence). Locale transmitted as `Accept-Language` only; it feeds `RequestContext.Locale` server-side and is never conflated with Region.

### 9f. Privacy + residency plumbing (client half of L20/L21)

- **Region is never client-authored:** no region picker, no locale-to-region inference client-side, no geo permission. The client's only contribution is the connection itself (edge inference, provenance-recorded per S1).
- **Zero third-party egress, enforced:** `tools/egress-lint` scans both client trees for URL/host literals and dependency manifests (Package.resolved, gradle lockfiles) against an allowlist (API host config, weeb.app, friki.app). Build-failing. No Firebase, no Crashlytics, no analytics SDK — crash telemetry = platform-native opt-in channels only. Fonts bundled, never fetched.
- **Transport:** ATS full enforcement (iOS), `usesCleartextTraffic=false` (Android); localhost/10.0.2.2 cleartext exceptions in DEBUG configurations only, with release-config absence tests (dev conveniences never in release — DevSeams posture).
- **Privacy declarations as code:** `PrivacyInfo.xcprivacy` (NSPrivacyTracking=false, collected-data-types=[], required-reason APIs enumerated) + a Play Data Safety source file, both gate-tested against the §3 inventory so declaration drift = red build. Truthful from day one because there is nothing to declare.
- **L20 second oracle:** a client-side test greps generated request models for the trust-field pattern set (contract-lint upstream is the first oracle; this catches a codegen-path leak). Additionally: zero non-generated request models exist in ApiKit consumers (lint).

### 9g. Maestro harness (14A) + a11y baseline (DR-6.1, G0 mandate)

- `maestro/flows/brand-smoke/`, parameterized by `APP_ID`/`BRAND`: launch → correct wordmark + `brand.primary` surface → five tabs present and navigable, Quests absent → no mode chip → signup shell walk to the gateway-refusal state → a11y assertions (VoiceOver/TalkBack labels on every tab and CTA) → ES-locale smoke. Runs ×4 (iOS/Android × Weeb/Friki) in the committed CI matrix slots (macOS runners iOS, emulator lane Android). This suite is the 14A parity foundation every later slice extends.
- A11y baseline in scope now: full element labeling, dynamic-type/font-scale-safe shell layouts (tested at largest size), reduced-motion honored (celebrations degrade to static frame + haptic), 44pt targets, modals screen-reader navigable. On-card gesture-button equivalents land with the deck (S14/S19) — the card anatomy reserves the slots now; obligation recorded.

## 10. BUILD.md §9 seams made concrete

| Seam | S7 concrete form | Enforced by |
|---|---|---|
| Contracts at the boundary (B17/D9) | clients consume `contracts/openapi.v0.json` READ-ONLY via build-time codegen; CI compiles generated clients against artifact `openapi-contract-v0` | CODEOWNERS + drift gates + codegen CI step |
| Module isolation (1A, client idiom) | DesignKit/ApiKit/AppShell/Features layering, no cross-feature imports; OpaqueId used verbatim, never parsed | per-platform dependency-direction lint test |
| Server-authoritative trust (L20) | no hand-rolled request models; generated-request-model trust-field grep as second oracle | client lint tests + contract-lint upstream |
| i18n seam | 2 brand packs × 4 locales per platform from first commit; i18n-lint ACTIVE; hardcoded-string lint; zh-Hans Han stack; pseudo-loc | `tools/i18n-lint` + platform lints in CI |
| Region-first PII (L21) | client never authors region; Accept-Language only; anonymous boot; zero device identifiers | egress-lint + §3 inventory tests |
| Brand flavor (7A/B18) | flavors lint-verified against `brands/*.json`; leak rule live on client trees | `brand-gate.mjs` flavorFiles + token-lint |
| Design system (DR-5.1) | `design/tokens.v1.json` + token-lint; registers enforced at the token layer | `tools/token-lint` |
| Absence, not disablement (law 3) | kit has NO disabled/locked variant (type-level; sole exception create-crew CTA, allowlisted) | kit test + snapshot suite |
| One deny shape (DR-7.3) | single LimitReached component bound to `limit_reached.generic`, shipped before any quota exists | message-keys binding test |
| Silent rejection unobservable | one error mapper, 404-uniformity test, no pending-chrome component | mapper tests |
| Real-or-honestly-dark (L6) | zero fabricated data; UnavailableSignupGateway in all configs; debug gallery never ships | release-config tests + UX crawl |
| One interface per vendor / DevSeams | `Iconography` (FA Pro), font bundle, Maestro (bought harness, 14A); debug-only cleartext with release-absence tests | interface layer + release-config tests |
| Analytics written AND received | deliberately ZERO client analytics at S7 — no account, no consent surface, nothing lawful to emit (P2's receiver-less buffer rejected, §13). The boot's `client-config` request lands one received behavioral event server-side (S1 already emits + reads back) — that is the runtime trace | live E2E behavioral read-back |
| Money-door fail-closed analog | release builds have no backend URL → no network path exists to misconfigure | release-config test |
| Deterministic math in pure libs | n/a — no math in the skeleton; token values are data | — |

## 11. Dependency classification (every not-yet-built system this slice reads or abuts)

| System | Class | S7 posture |
|---|---|---|
| S0 CI/flavor gates · S1 contract v0 + `/v1/client-config` on compose | **BUILT** | consumed as-is; S7 is the first real consumer of the contract artifact chain — itself a test of S1's seam |
| S3 identity endpoints | **seam-now** | `SignupGateway` + `UnavailableSignupGateway` (honest refusal in all configs; no fake success ever). Activation = contract regen + one impl per client; shell UI does not change |
| S4 push/notifications | **not read — deliberate** | no permission ask, no token registration; inbox scaffold renders empty; priming screens gallery-only |
| S6 ANIME short form | **not read — deliberate** | 5.14b prompt in state kit only; unreachable in release flow until S3 |
| S8/S19 con registry / mode | **swap-safe** | `ModeContext` closed, only `Online` constructible; no chip renders |
| S13 anti-screenshot | **swap-safe** | no DM/deck surface exists to mount `SecureSurfacePolicy`; obligation recorded, no dead code shipped |
| Behavioral analytics ingest (client-side) | **not read — deliberate** | zero client emitter until an account + consent surface exists (S3/S17); boot trace is server-side |
| Force-upgrade kill switch | **seam-now (cheap half only)** | tolerant-reader test + contract-mismatch state; `min_supported_client_version` recorded in TODOS for the serving slice |
| S5 Metrics & Ops desk | **swap-safe** | outcome renders in CI + retro (S2 §10.6 interim posture); interim read via `backend/e2e/substrate.e2e.mjs` pattern for the boot event |
| Store listing / bundle ids (OQ-1) | **must-decide-before-first-store-upload** | provisional `.dev` ids excluded from parity; canonical fields stay blank; release lane fail-closed (§14) |
| FA Pro license kit + M PLUS OFL files | **must-obtain (assets)** | blocking asset checklist item for Julien; no code seam needed |
| Apple/Google developer accounts | **not needed for G0** | Maestro runs on simulator/emulator; TestFlight/Play lanes stay guarded |

**MUST-BUILD-FIRST blocking S7: none.** S7i and S7a run in parallel worktrees (disjoint trees; coordinator owns git). Shared artifacts (`maestro/`, `design/tokens.v1.json`, workflow graduation edits) are created once by a shared-wiring pre-step, then the lanes are disjoint.

## 12. Tests, gate, and evidence at sign-off

Gate lane (deterministic, <2s where local): token-lint · brand-gate (new flavor parsers) · egress-lint · i18n-lint (ACTIVE) · secret-scan · dependency-direction + no-hand-rolled-request-model lints · unit tests for ModeContext, tolerant reads, error mapper (incl. 404-uniformity), signup validation, zero-persistence-framework assertion, privacy-manifest truth tests. All new lint rules red-fixture-proven both directions.

Evidence at sign-off (ledger row: **brand-smoke Maestro ×4 listings green**):

1. `xcodebuild test` ×2 flavors, `./gradlew test` ×2 flavors, zero failures; snapshot suite covers the full state kit per flavor per locale.
2. Maestro brand-smoke ×4 green in CI, including the a11y assertions; the four guarded jobs now real and required.
3. Live E2E (native form): simulator/emulator app against the compose backend renders `GetClientConfig` on the debug diagnostics screen — read BACK, never emit-only; the boot lands one received behavioral event (verified via the substrate E2E pattern); post-run log sweep zero exceptions on the backend instance.
4. Drift gates byte-identical: `openapi.v0.json`, `message-keys.json`, DDL, purge registry; backend arch suite untouched-green; container-inventory test green after the Maestro run.
5. UX-coherence crawl against design 01/05/06 + Index: trunk test on every screen, no leaked brief labels ("5.13", "5.14a"), absence law + register separation + one-deny-shape verified by eye; the debug gallery is the crawl surface for non-reachable states.
6. Hardened-gate clause 3 (prod web artifact): N/A-with-note, native-only slice. Clause 4 two-instance realtime: N/A, no realtime surface. Clause 6: suite ×2 under capped parallelism. Clause 7: S7i/S7a merge serially `--no-ff`, full suite + both lanes' Maestro on the combined tree.

## 13. Adoption record + ruling contradictions (the judge's per-conflict calls)

| Conflict | Adopted | Reasoning |
|---|---|---|
| iOS project source | **P2 XcodeGen** | pbxproj is the worst merge artifact in a parallel-agent repo (CLAUDE.md parallel-session-safety); project.yml is diffable data; reversible in one command. Layer-2 justification recorded |
| Brand flavor mechanism | **P1/P2 lint-verify** | P3's codegen-from-`brands/*.json` **contradicts the S0-ratified lint-verify-not-generate brand-gate pattern** — loses automatically |
| Token manifest | **P2's `design/tokens.v1.json` + `tools/token-lint/`** | versioned like the contract; tool lives with the other multi-file lints in `tools/`; P1's mechanism identical in substance. P3's "static codegen from DESIGN.md" loses to the same lint-verify ratification |
| Client layering | **P2/P3 four-layer split** | two of three converge; the dependency lint is the 1A analog; later slices mount into Features/* instead of shell surgery. P1's no-split is right for backend modules but the split here is contract, not speculation |
| Signup seam behavior | **P1 UnavailableSignupGateway in ALL configs** | P2/P3's debug mock success creates a fake-success path L6 exists to ban; brand-smoke only needs the honest refusal, which IS the designed journey end. DevSeams stays available to S3 for its real staging impl |
| Client analytics | **P1/P3 ZERO** | P2's receiver-less in-memory buffer contradicts the §9 seam "analytics written AND received" and precedes any consent surface (S17). The boot's server-side behavioral event is the trace |
| Device persistence | **P1/P3 ZERO, twice-tested** | P2's reserved keychain slot + config cache are S3's job and would falsify the privacy manifests |
| Release network posture | **P1 no-backend-URL fail-closed** | P3's boot check + mismatch state adopted as logic exercised in debug/Maestro; P2's TTL cache dropped (persistence) |
| Bundle ids | **P1/P3 provisional, canonical blank** | P2's write-canonical-now conflicts with S0's ratified fail-closed pending-OQ-1 posture; store identity is a one-way door and founder-owned (§14) |
| Maestro location | **all three: root `maestro/`** | 14A says shared; ios.yml's `ios/.maestro` guard is repointed in the graduation edit |
| UpdatePolicy machinery | **rejected (P2)** | S7 binaries never reach a store; tolerant-reader test + contract-mismatch state are the cheap honest half; the rest lands with the slice that serves the field |
| Error mapper / 404-uniformity / no-pending-chrome | **P3 whole** | structural silent-rejection posture at near-zero cost; the leak-shaped component never existing beats reviewing for it forever |
| Egress-lint + privacy manifests + zh-Hans stack + no language picker | **P3 whole** | the client half of L20/L21 is only cheap now; declarations-as-code start truthful because there is nothing to declare |
| 23-state acceptance + checkpoint flag on count drift | **P1** | faithful-transcription-or-named-flag beats silent adaptation |
| Dark mode | **force light at S7; Choco tokens carried in manifest** | DR-6.3: tokens from the start, rendering at its G3 slice; a system-dark render of an unstyled surface is design drift |

## 14. Open questions (genuinely Julien's)

**OQ-S7-1 — bundle identifiers (OQ-1 carry-over).** `brands/*.json` has empty `bundle_id_ios` / `application_id_android`. They are a one-way door at first TestFlight/Play upload, not at S7. This contract proceeds with `.dev`-suffixed provisional ids excluded from brand-gate parity and canonical fields blank; the release lane stays fail-closed. **Needed from Julien:** ratify the canonical ids (or the naming pattern, e.g. `app.weeb.client` / `app.friki.client`) with OQ-1 before S26's submission package. Nothing in S7 blocks on the answer.

**Julien action (not a fork):** procure/confirm the Font Awesome Pro license kit files for bundling; M PLUS Rounded 1c OFL files need no action beyond download.

Everything else is resolved above with reasoning; the XcodeGen and tokens.v1.json calls are recorded as reversible and are ratified-by-silence per the standing pattern unless Julien objects at contract review.

---

## 15. RATIFICATION (orchestrator, 2026-07-10 — Julien's in-absence pre-approval)

Contract **RATIFIED** with two corrections that bind the build, plus a build-sequencing directive. Julien's standing rule: in-absence ratification of the current slice's contract is pre-approved; it does not auto-start the next slice.

**Scope ratified as written.** Two native shells × two flavors, one token layer, the state kit, the Maestro ×4 harness, i18n ×4 — and **zero backend delta, drift-gate-asserted (§2–§8).** That zero-backend property is exactly what makes S7 safe to run as a parallel lane against S1's frozen contract v0: it touches `ios/`, `android/`, `maestro/`, `design/tokens.v1.json`, `tools/{token-lint,egress-lint}`, arms `tools/i18n-lint`, and graduates `ios.yml`/`android.yml` — none of which the backend lane (S2) touches. The one structural law (both apps, both flavors, contract-read-only, ×4 brand-smoke required forever) is the deliverable.

**XcodeGen + `design/tokens.v1.json` — RATIFIED (not left to silence).** Both are reversible layer-2 calls with recorded justification (pbxproj is the worst parallel-agent merge artifact; the token manifest versions like the contract). Affirmed.

**OQ-S7-1 (bundle identifiers) — RESOLVED by Julien 2026-07-10.** Julien ratified the recommended pattern. Canonical ids are now set in `brands/*.json`: **`app.weeb.client`** (Weeb) and **`app.friki.client`** (Friki), same value for `bundle_id_ios` and `application_id_android` per brand (reverse-DNS of `weeb.app`/`friki.app` + `.client`). No longer blank, so brand-gate parity now enforces canonical (non-blank) against the flavor files; dev builds use the `.client.dev` suffix in flavor files only, and drift against canonical fails the gate. The release lane stays fail-closed. `store_listing_refs` stay pending store-account provisioning (a separate gate, not OQ-S7-1). This closes the OQ-1 carry-over; nothing store-side is a one-way door until first upload at S26.

**CORRECTION 1 — the signup-shell birthdate gate enforces the 18+ attestation floor, not merely under-13.** The native apps are actor A2 (18+); per the ledger, **the S9 web funnel is the ONLY under-18-visible surface** (A1, T10-A). §9c names only a "hard under-13 refusal," which is the COPPA absolute floor, not the app's floor. The build MUST make the 5.14a birthdate step refuse any attested age **under 18** (routing to the designed neutral-plain refusal state), with attested **under-13** rendering the distinct hard COPPA-refusal copy as a sub-case. Client-side attestation only (server-side 18+ enforcement + estimation-first verification are S3/S18); but an 18+ app whose signup shell lets a 15-year-old through the form is a fabricated-honesty defect (L6) and a minor-protection gap, caught now while it costs one validation rule and one keyed string set.

**CORRECTION 2 — Font Awesome Pro is an asset swap, never a build blocker.** §9a already wraps icons in a single swappable `Iconography` layer (buy seam). The build MUST ship that layer against a **bundled fallback glyph source** (the FA free set, or a minimal bundled placeholder set) whenever the FA Pro license kit is not present in-repo, so `xcodebuild test` / `./gradlew test` / Maestro ×4 (§12 evidence) go green without the license asset. The real FA Pro kit is then one asset+DI swap behind the `Iconography` seam, recorded as a **Julien asset task** — never a call-site change. M PLUS Rounded 1c is OFL: build agents bundle it directly, no action needed. Absence of the Pro kit does not gate S7 green.

**BUILD-SEQUENCING DIRECTIVE (parallel-lane discipline, BUILD.md §8 clause 7 + CLAUDE.md parallel-session-safety).** S7's Phase 0 ran commit-free and in parallel with S2's build — that parallel win is banked. S7's **build** phases (which `git add`/commit) share the single working tree with the in-flight S2 build, so the two commit-bearing workflows do NOT run simultaneously: **S7's build launches only after S2's build completes** and its tree is quiet. Within the S7 run, S7i and S7a still fan out (parallel `ios`/`android` contexts) and merge serially `--no-ff` per clause 7. This honors the parallel intent at the design phase without racing the git index. (If S2's build stalls, the fallback is a dedicated S7 lane branch/worktree; not needed while S2 is ahead.)

**Carried findings.** Auth-F3 remains N/A to this slice — S7 ships zero server verbs and zero mutation endpoints (§4), so there is no resource-scoped authorization surface; it stays carried to the first **client-reachable, resource-scoped 4A** slice (unchanged from the S2 ratification). No security defers are expected to originate in S7 (client-only, zero backend). The S7 Phase-3 auth/IDOR lens must instead CONFIRM the client encodes zero entitlement logic (renders on DTO field presence, §4); the PII lens verifies zero device identifiers + egress-lint + truthful privacy manifests (§3/§9f); the minor-protection lens verifies Correction 1's 18+ floor and the dignity states.

Ratified. S7's build is queued behind S2's; I release it when S2's tree goes quiet.

— RATIFIED 2026-07-10 (orchestrator, in-absence pre-approval) —

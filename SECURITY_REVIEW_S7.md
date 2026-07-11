# SECURITY_REVIEW_S7.md — slice S7 (client-skeletons) Phase-3 review + remediation record

**Slice:** S7 — two native client shells (iOS `ios/`, Android `android/`) × two flavors (Weeb/Friki),
one token layer, the state kit, the Maestro ×4 harness, the client half of the L20/L21 privacy
invariants. **Zero backend delta** (drift-gate-asserted, contract §2–§8).

**Scope reviewed:** `ios/**`, `android/**`, `maestro/**`, `tools/{token-lint,egress-lint,i18n-lint,fa-map-lint}/**`,
`design/tokens.v1.json`, `design/fa-glyph-map.json`, `brands/**`, `i18n/**`, `contracts/**` (read-only
consumption). Generated code (`**/generated/**`, `.build/`, `build/`) excluded except where a base-URL
seam was inspected.

**Verdict: PASS with 2 remediated findings, both fixed in this slice with tests. No deferrals originate
in S7.** The contract (§254) scoped this Phase-3 review to three confirmation lenses for a client-only
slice; all three confirm. The two findings below are genuine client-code defects found during the
review and fixed the same session (regression tests in the diff), not carried.

Per the S7 contract (§254): **Auth-F3 remains N/A / carried** — S7 ships zero server verbs and zero
mutation endpoints, so there is no resource-scoped authorization surface; it stays carried to the first
client-reachable, resource-scoped 4A slice (unchanged from the S2 ratification).

---

## 1. The three Phase-3 lenses — CONFIRMED

### Lens A (auth / IDOR): the client encodes ZERO entitlement logic — CONFIRMED

The client must never decide what a user is allowed to do; it renders on DTO field *presence* (generated
optionals), so it can never disagree with the 4A policy engine.

- No client computes an allow/deny/premium/quota/reputation decision. The sweep for
  `entitlement|isAllowed|isPremium|quota|reputation|canAccess|authorize|tier|paywall` over both trees
  returns only (a) view labels/test-ids (`crews.create.premium.cta` — a labelled CTA, the sole
  §DESIGN-law-3 exception, not a gate) and (b) read-only rendering of server-echoed display data.
- `ios/ApiKit/Sources/ApiKit/ErrorMapper.swift:19-21,101-107` maps a **server** `LimitReached` payload
  (`quotaKey`, `premiumExtends`) into `LimitReachedInfo` purely to render the one deny surface — the
  client renders what the server sent, it does not compute the limit.
- `android/designkit/.../state/LimitReachedSurface.kt:9` — reputation-scaled and freemium caps render
  through one identical surface; no client branching on standing (token law 4).
- `ios/DesignKit/Sources/DesignKit/StateCatalog.swift:11-12` — reputation "standing" and Premium
  paywall/lapsed states are explicitly out of S7 (S14/S16/S35); nothing renders them.

### Lens B (PII / privacy): zero identifiers, zero egress, truthful manifests, zero persistence — CONFIRMED

All six load-bearing artifacts were read directly for this review, not taken on report.

- **Zero device identifiers / trackers.** No `identifierForVendor|advertisingIdentifier|UIDevice|
  DeviceCheck|appAttest` (iOS) and no `Settings.Secure|ANDROID_ID|getDeviceId|TelephonyManager|
  advertisingId|Build.SERIAL|IMEI|MAC` (Android). No analytics/crash SDK anywhere;
  `android/app/src/main/kotlin/app/client/WeebApplication.kt` is an empty `Application()` — no SDK
  bootstrapping. Enforced by `tools/egress-lint` (tracker denylist over dependency manifests).
- **Zero third-party egress.** The only URL hosts anywhere in the trees are dev-loopback
  (`127.0.0.1`, `10.0.2.2`) in **debug-only** scope and SPM/Gradle package-resolution coordinates in
  manifests. `contracts/openapi.v0.json` (and the iOS copy) carry **no `servers` block**, so the
  generated client bakes in no base URL — it is injected at runtime by the caller, and the only caller
  is the debug diagnostics screen. `iOS ApiKit/Transport.swift:44-46` returns `nil` on the release path
  (fail-closed). Enforced by `tools/egress-lint` (host allowlist over `.swift/.kt`, **now also** over
  manifests — see SEC-S7-F2).
- **iOS ATS.** `ios/App/Sources/Resources/Info-Release.plist` has **no `NSAppTransportSecurity` key**
  (ATS fully enforced by absence); no `NSAllowsArbitraryLoads` exists anywhere. The debug plist's ATS
  exceptions are localhost/127.0.0.1 loopback only. `ReleaseConfigTests` asserts
  `NSAppTransportSecurity == nil` in release.
- **iOS PrivacyInfo** (`PrivacyInfo.xcprivacy`, read in full): `NSPrivacyTracking=false`,
  `NSPrivacyTrackingDomains=[]`, `NSPrivacyCollectedDataTypes=[]`, `NSPrivacyAccessedAPITypes=[]`.
  Truthful: the one required-reason API touched (`UserDefaults` in `AppShell/LaunchLocale.swift`) is
  entirely inside `#if DEBUG` with no `#else`, so the shipping binary contains no such symbol.
  `AppComplianceTests/PrivacyManifestTests.swift` parses the compiled `Bundle.main` resource and asserts
  all of the above.
- **Android network + permissions** (all four files read in full): main
  `network_security_config.xml` = `<base-config cleartextTrafficPermitted="false"/>` with **no** domain
  exceptions; the loopback cleartext exception lives ONLY in the `src/debug/` overlay (localhost /
  127.0.0.1 / 10.0.2.2, `includeSubdomains="false"`). Main `AndroidManifest.xml` declares **zero
  `uses-permission`**, `allowBackup="false"`, `usesCleartextTraffic="false"`; `INTERNET` exists ONLY in
  the `src/debug/` overlay, so release cannot acquire it. `MainActivity` is `exported="true"` — justified
  (it is the LAUNCHER activity), not a finding. `play_data_safety.json`: `tracking=false`,
  `dataCollected=[]`, `dataShared=[]`, `dataEncryptedInTransit=true`. `PlayDataSafetyTest.kt` +
  `ReleaseConfigTest.kt` gate the declaration and the release posture.
- **Zero persistence of PII.** Signup fields (handle/email/birthdate/fandom) live only in view state
  (`@State` / `remember { mutableStateOf }`); avatar is never captured (`uploadAvatar(nil)` /
  `avatarRef = null`); the birthdate is handed to `UnavailableSignupGateway` which discards it with
  `.couldNotSend`. No `UserDefaults`(release)/`SharedPreferences`/`DataStore`/`CoreData`/`SwiftData`/
  `Room`/`Realm`/`Keychain` write exists in product source. Enforced by
  `PersistenceFrameworkAbsenceTests.swift` + `ZeroPersistenceTest.kt`.

### Lens C (minor-protection): 18+ floor + hard COPPA sub-case + dignity states — CONFIRMED (with SEC-S7-F1 fixed)

- The birthdate gate (`ios/Features/Signup/Sources/Signup/Validation.swift`,
  `android/features/signup/.../BirthdateValidator.kt`) refuses any attested age **< 18**
  (`refusedUnder18` / `RefusedUnder18`, neutral-plain refusal state) with **< 13** rendering the distinct
  hard COPPA copy (`refusedUnder13COPPA` / `RefusedCoppaUnder13`) — Correction 1 satisfied. Elapsed-year
  math is correct across the "birthday hasn't happened yet" boundary; parsing is strict
  (`isLenient=false` + a `^\d{4}-\d{2}-\d{2}$` shape guard on iOS; `ISO_LOCAL_DATE` on Android). The
  17/18/one-day-before-18/12/13/30 boundaries each have a test on both platforms.
- Refusals route to a dead-end neutral-register state with a `DignityShield` glyph — no candy, no
  mascot, no humiliating copy (DESIGN.md Voice / token law).
- **Absence, not disablement.** The signup submit action does not exist until a fandom tag is chosen
  (no greyed/disabled button); `NoDisabledStateTests.swift` / `NoDisabledTokenTest.kt` forbid any
  disabled/locked token, cross-checked against `design/tokens.v1.json` `forbidden_token_groups`.
- The one gap in this lens (future-date misclassification) is **SEC-S7-F1 below, fixed in this slice.**

---

## 2. Remediated (`fixNow`) — 2 findings, tests in the diff

### SEC-S7-F1 (MEDIUM, minor-protection) — a future birthdate was mis-rendered as the hard COPPA refusal

`BirthdateValidation.evaluate` (iOS) / `BirthdateValidator.evaluate` (Android) computed elapsed years
with no lower bound. A birthdate in the **future** (a typo / fat-fingered year) yields **negative**
elapsed years, which trips `years < 13` and renders the hard **COPPA-under-13** refusal copy on what is
actually a data-entry error. Fail-closed (the user is refused, never let through), but it fabricates a
minor-protection verdict from bad input and shows the wrong, heavier copy — in the exact subsystem
Correction 1 elevated. No test covered a future date.

- **Fix (iOS):** `Validation.swift:57-62` — guard `if birthdate > now { return .invalidFormat }` before
  the age computation. `evaluate(text:)` routes through the same chokepoint.
- **Fix (Android):** `BirthdateValidator.kt:27-34` — added `AgeGateResult.Invalid`; guard
  `if (birthdate.isAfter(today)) return AgeGateResult.Invalid` before `Period.between`. `SignupFlow.kt`
  routes `Invalid` back to the birthdate step (re-enter), never to an age-refusal state.
- **Tests:** `ValidationTests.swift` — `futureDateIsInvalidNotCOPPA` (future Date + future text →
  `.invalidFormat`; and the `now == birthdate` boundary is still age-0 COPPA, proving the guard fires
  strictly in the future). `BirthdateValidatorTest.kt` — `a future birthdate is Invalid, never a COPPA
  verdict` (+ the same today-boundary assertion). Verified locally: iOS `swift test` 12/12 pass;
  Android `:features:signup:test` all four variants pass.

### SEC-S7-F2 (LOW-MEDIUM, egress) — egress-lint never host-checked dependency manifests

`tools/egress-lint` ran its runtime host-allowlist only over `.swift/.kt` source; `*.gradle.kts` /
`Package.swift` / `Package.resolved` were scanned **only** against the tracker denylist. Consequence: a
prod / egress host hardcoded in a build file (e.g. a `buildConfigField` URL in `build.gradle.kts`) would
**pass egress-lint silently**, dodging the source scan. Benign today (the only manifest host literals are
`10.0.2.2` (allowlisted) and package-resolution coordinates), but a real hole in the P3 "zero
third-party egress, enforced" guarantee.

- **Fix:** `egress-lint.mjs` — added `PACKAGE_HOST_ALLOWLIST` (package registries + source forges) and
  `findManifestEgressHosts`, wired into `main`. A manifest may reference package-resolution hosts and the
  app allowlist (weeb.app/friki.app/loopback); **any other URL host in a build file now fails the lint.**
- **Tests:** `egress-lint.test.mjs` — GREEN (manifest with only package + loopback hosts passes) and RED
  both directions (a `tracking-vendor.com` URL in a `.gradle.kts` is caught; a suffix-spoofed
  `weeb.app.evil.com` is caught; `github.com`/`maven` still pass). 14 tests pass; the real tree
  (103 files) stays green.

---

## 3. Compliance control recorded — Font Awesome Pro kept out of the public repo

Not a code vulnerability, but a licensing / asset-leak control worth recording because it was exercised
this slice. Font Awesome **Pro** (paid commercial license) was placed locally to back the icon seam. FA's
Pro license forbids committing Pro files to a **public** repository, and `hebihime/weeb-app` is public.
Control applied:

- The `.otf` binaries are **hard-ignored** (`.gitignore` `**/FontAwesome*.otf` / `**/fontawesome*.otf` /
  `**/Font Awesome*.otf`, case-covered for case-sensitive CI). Verified: `git check-ignore` matches all
  four placed files; `git status` shows no `.otf`. Also satisfies CLAUDE.md's no-binaries-in-repo rule
  (git-lfs is not installed).
- Only **data** is committed: `design/fa-glyph-map.json` (the 27-glyph → FA-name + codepoint map,
  generated deterministically from the licensed metadata) and pointer docs `ios/DesignKit/FONTS.md` /
  `android/designkit/FONTS.md`. `tools/fa-map-lint` keeps the map in exact sync with both `Glyph` enums.
- **Open decision (yours):** how the licensed fonts reach CI / shipped builds (private repo + LFS, or
  CI-secret injection). Until then the icon seam keeps resolving its zero-license bundled fallback
  (SF Symbols / Compose Material Icons), so builds stay green with no licensed asset present.

---

## 4. Accepted / INFO — no action this slice

- **Client email validation is nominal** (`email.contains("@")` on Android `SignupFlow.kt:103`; no format
  check on iOS). Acceptable for a shell — the server is authoritative and no email leaves the process
  (`UnavailableSignupGateway`). Recorded; not a finding.

---

## 5. Gate result (actual, verified locally)

| Check | Result |
|---|---|
| iOS `swift test` (Signup: Validation + gateway) | 12 tests, 3 suites, **pass** (incl. SEC-S7-F1) |
| iOS `swift build` DesignKit | **pass**, no unhandled-resource warning (fonts relocated out of `Sources/`) |
| Android `:features:signup:test` (weeb+friki × debug+release) | **pass** (incl. SEC-S7-F1) |
| `node --test` egress-lint | 14 pass (incl. SEC-S7-F2 both directions) |
| `node --test` fa-map-lint | 9 pass; real map ↔ iOS ↔ Android Glyph enums in sync (27) |
| `node --test` token-lint + i18n-lint + brand-gate | 55 pass |
| egress-lint real tree | OK — 103 files, zero disallowed egress, zero tracker SDKs |

The full native matrix (xcodebuild test ×2 flavors, gradlew test ×2, Maestro brand-smoke ×4, codegen,
string-catalog) runs on CI against the pushed commit; this review's two fixes touch only pure client
logic + the node lint, none of the build-config / source-set surfaces that require the emulator/simulator
lanes to re-validate.

---

## 6. Dependency provenance — CONFIRMED pinned + trusted

- **iOS SPM:** only Apple first-party OpenAPI packages declared (`swift-openapi-generator/runtime/
  urlsession`); transitive `mattpolzin/OpenAPIKit`, `jpsim/Yams` are standard generator deps. All pinned
  in the committed `Package.resolved` (exact revisions), origins github.com/apple + those two.
- **Android Gradle:** `settings.gradle.kts` sets `repositoriesMode = FAIL_ON_PROJECT_REPOS`, resolution
  limited to `google()` + `mavenCentral()` (+ `gradlePluginPortal()` for plugins). All plugin/lib
  versions pinned (AGP 8.5.2, Kotlin 2.0.20, openapi-generator 7.9.0, roborazzi 1.30.1, okhttp 4.12.0,
  compose-bom 2024.09.00, …). No arbitrary Maven URLs.
- **OpenAPI codegen:** client regenerated at build time from the checked-in canonical
  `contracts/openapi.v0.json`; generated code gitignored; `NoHandRolledRequestModelTest` /
  `ContractShapeTest` forbid hand-rolled request models.

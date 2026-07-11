# maestro/ — 14A shared cross-platform E2E harness (SLICE_S7_CONTRACT.md §9g)

One brand-smoke flow, run ×4 (iOS/Android × Weeb/Friki). It lives at repo root because 14A says the
E2E harness is SHARED — it cannot live inside either client. `ios.yml` / `android.yml` graduated their
`maestro-brand-smoke` jobs to run these flows (the guard moved from `ios/.maestro` to `maestro/`).

This suite is the parity foundation every later client slice extends. A flow written once must pass on
both platforms, so the two native shells expose an **identical accessibility-ID contract** (below):
iOS `accessibilityIdentifier` == Android `Modifier.testTag` == the ID string in the flow YAML. Each ID
doubles as the element's accessibility label, so asserting visibility also proves VoiceOver / TalkBack
labelling (the DR-6.1 a11y baseline).

## Running one leg

```
maestro test maestro/flows/brand-smoke/smoke.yaml \
  -e APP_ID=app.weeb.client.dev \
  -e BRAND=weeb \
  -e WORDMARK=Weeb \
  -e ES_HANDLE_TITLE="Elige tu usuario"
```

The four CI legs (in `ios.yml` / `android.yml` matrices):

| Leg | APP_ID | BRAND | WORDMARK |
|---|---|---|---|
| iOS Weeb | app.weeb.client.dev | weeb | Weeb |
| iOS Friki | app.friki.client.dev | friki | Friki |
| Android Weeb | app.weeb.client.dev | weeb | Weeb |
| Android Friki | app.friki.client.dev | friki | Friki |

`.dev` suffix = the debug/provisional bundle id (§1c); the canonical `app.{weeb,friki}.client` base is
what `brand-gate` verifies against `brands/*.json`.

## Accessibility-ID contract (both platforms implement these verbatim)

**Shell (AppShell):**
`brand.wordmark` · `tab.connect` · `tab.explore` · `tab.crews` · `tab.inbox` · `tab.profile`
Must NOT exist at S7: `tab.quests` (pre-G4), `mode.chip` (only Online is constructible).

**Tab zero-data states (honest empties, no fabricated data):**
`state.connect.empty` · `state.explore.empty` · `state.crews.empty` · `state.inbox.empty` · `state.profile.empty`
`crews.create.premium.cta` — the sole ratified law-3 disabled/secondary-CTA exception.

**Signup shell (Features/Signup, 5.14a):**
`signup.start` · `signup.handle` + `signup.handle.next` · `signup.email` + `signup.email.next` ·
`signup.birthdate` + `signup.birthdate.next` · `signup.avatar.skip` · `signup.fandom` +
`signup.fandom.option.0` · `signup.submit`

**State kit (5.13 — the one reached in the release flow):**
`state.error.could_not_send` — the honest gateway-refusal end (UnavailableSignupGateway, all configs).

## What the flow proves (the ledger row: brand-smoke Maestro ×4 green)

1. Correct flavor launched: wordmark visible + carries the brand name.
2. Trunk test on every screen: brand mark + five tabs; Quests absent; no mode chip.
3. Each tab navigable to its designed honest empty state (never fake counts/people — L6).
4. Signup shell walks to the honest could-not-send refusal — no fake success path exists to remove later.
5. a11y: every tab + CTA is labelled (assertVisible by id == label present for VoiceOver/TalkBack).
6. ES-locale smoke: the ES handle-step title renders (DR-6.2, ES tested first).

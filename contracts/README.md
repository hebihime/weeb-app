# contracts/ — OpenAPI contract v0

**Canonical location: `contracts/openapi.v0.json`.** Owned by backend (lane A) via `CODEOWNERS`;
consumed READ-ONLY by all three clients (iOS, Android, web), each generating its own typed client (D9,
B17). This file does not exist yet at S0 — S1 (`domain-substrate`) is the first slice that emits it.
Backend CI publishes it as artifact `openapi-contract-v0` once it exists (guarded activation); the
client codegen steps are already committed in `ios.yml` / `android.yml` / `web.yml`, pointed at that
artifact name, and skip with a note until it exists.

## Invariants (build-failing, enforced by `tools/contract-lint/`)

Every rule below traces to a ratified ruling. `tools/contract-lint/contract-lint.mjs` self-skips
(exit 0) until `openapi.v0.json` exists; it has full golden-vector coverage now so S1 inherits a
working, tested gate instead of writing one under deadline.

1. **Server-authoritative trust (L20).** No REQUEST schema may contain a property shaped like
   `verification*`, `reputation*`, `premium*`, `moderation_state`, `age_estimate`, `trust*`, `tier*`.
   These are things the server decides; a client that could set them could forge them.
2. **Absence law (token law 3).** No consumer-facing RESPONSE schema may contain `*_locked`,
   `*_disabled`, `*_gated`, `gate_state`, `locked_reason`, or `upgrade_required`. Below a gate, the
   field is ABSENT — never present-and-false, never a disabled affordance the client has to grey out.
   The allowlist requires a ruling citation per entry; the sole known exception today is the create-crew
   Premium CTA at S27 (DR-2.2).
3. **One deny shape (10A / DR-7.3).** Every `429` response on a consumer path must `$ref` the single
   shared `LimitReached` schema component. A distinct per-cause deny schema fails the gate — there is
   exactly one "you've hit a limit" UI in the whole product.
4. **Silent-rejection unobservability (R5 / 12A-r / T7-A).** Deny/void-class operations (marked
   `x-deny-void-class: true`) declare no sender-visible `pending`/`void` response field — the sender
   must never be able to distinguish "still pending" from "silently voided." Consumer read paths marked
   `x-consumer-read: true` never declare a `403` — an unauthorized or excluded read gets the same `404`
   shape as a resource that never existed.

## Why this file exists before the contract does

Pinning the coordinate, the artifact name, and the lint rule set now means lanes B/C/D (iOS, Android,
web) never renegotiate where the contract lives or what shape it must have — they just wait on the
guarded activation. S1 fills the seam without touching CI.

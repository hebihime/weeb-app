# infra/ — Azure Bicep (2A verbatim)

One module per Azure service under `modules/`; any 2A exit ramp is a one-module swap. `main.bicep` is
the composition root. `params.{dev,staging,prod}.bicepparam` hold the per-environment values — see
`OPEN QUESTIONS` in `SLICE_S0_CONTRACT.md` (OQ-2) for the region ratification status.

## Residency (L21)

Single `location` parameter per environment; every data-bearing resource (`postgres-flexible`,
`blob-cdn`, `signalr`, `keyvault`) resolves from it — none may declare its own location literal.
`main.bicep` asserts the primary/backup pair is on an in-jurisdiction allowlist
(`westeurope` <-> `northeurope`, both EU) and fails deployment (via a deliberately-failing
`deploymentScripts` resource) on an unlisted pair.

## Edge guard (L17)

`edge-guard.bicep` declares the ingress reject rules (encoded traversal, dot-segments, `/internal`
reach-through) that must 404 BEFORE any rewrite. `infra/edge-guard.test.mjs` asserts the rendered rule
set structurally; `backend/e2e/edge-guard.mjs` is the adversarial reachability script — it self-skips
until a public host exists (S9) and is mandatory at that slice.

## Key Vault posture

Soft-delete + purge protection ON. App identities get crypto-user RBAC only, never key export. The key
name `field-enc-special-category-v1` is reserved now (S0) so S10's envelope encryption of
special-category fields has a home without a rename. No default values on any secret-typed parameter —
an unset secret fails deployment.

## 13A non-stores (recorded here per SLICE_S0_CONTRACT.md §6, so the S1 purge-registry review has a
citation instead of an unexamined gap)

- **CI artifacts/logs:** GitHub-retained, no user data. Workflow policy forbids uploading DB dumps or
  user-shaped fixtures as CI artifacts. Retention: 30 days. Auth: OIDC only — zero long-lived cloud
  credentials live in GitHub secrets.
- **docker-compose dev volumes:** dev-only, destroyed by `docker compose down -v`, exercised by the
  fresh-boot clause (BUILD.md §8 clause 2) on every slice sign-off.
- **Postgres geo-redundant backup (derivative, cross-region — SECURITY_REVIEW_S0.md purge-completeness
  F1):** `modules/postgres-flexible.bicep` sets `geoRedundantBackup: 'Enabled'` and
  `backupRetentionDays: 7`. A geo-backup is a cross-region COPY of every row, including every future
  purge-subject row; a 13A tombstone/purge run over the live server does not touch it. Owner: the S1
  purge-registry review must add an explicit backup-purge/expiry leg (crypto-shred of the field-
  encryption key, or restore-then-purge on any DR drill) before this ships to prod. Until then, the
  7-day backup horizon is the outer bound on "purged from primary but still recoverable from backup" —
  cited here so that bound is a known fact, not an unexamined gap.
- **CDN edge cache (derivative of blob, per-edge — SECURITY_REVIEW_S0.md purge-completeness F2):**
  `modules/blob-cdn.bicep` declares a `Microsoft.Cdn/profiles/endpoints` (Standard_Microsoft) endpoint
  over the media blob origin with default edge TTL and no purge-on-delete wiring. Deleting a blob (the
  S11 orphaned-blob sweep; Art. 17 verbs on DM media, BUILD.md:128-129) does not evict copies already
  cached at the edge. Owner: the S1 purge-registry review must wire a CDN purge call (or a short,
  explicitly-bounded TTL) into every blob-delete path before this ships to prod; until then this is a
  cited, known gap, not an unexamined one.
- **Log Analytics workspace (derivative operator-log stream — SECURITY_REVIEW_S0.md purge-completeness
  F3):** `modules/log-analytics.bicep` sets `retentionInDays: 30`. Every future container app in
  `container-apps-env.bicep` wires its stdout/stderr here, and request-derived operator logs can carry
  user-derived data for the full 30-day window. Owner: the S1 purge-registry review scopes this stream
  explicitly in or out of the purge registry; 30 days is the current outer bound on "logged but not yet
  purged" either way.

## Local facts (update when infra first deploys — BUILD.md §1)

- Azure subscription: not yet provisioned (Founders Hub application is OQ-3, Julien's action).
- OIDC federation: not yet configured. `az bicep build` / `bicep build` and the linter run
  credential-free in CI; `what-if` and `deploy` are guarded on OIDC + the `release`/`dev`/`staging`/
  `prod` GitHub environments existing (§13, Julien-executed).

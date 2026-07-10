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

## Local facts (update when infra first deploys — BUILD.md §1)

- Azure subscription: not yet provisioned (Founders Hub application is OQ-3, Julien's action).
- OIDC federation: not yet configured. `az bicep build` / `bicep build` and the linter run
  credential-free in CI; `what-if` and `deploy` are guarded on OIDC + the `release`/`dev`/`staging`/
  `prod` GitHub environments existing (§13, Julien-executed).

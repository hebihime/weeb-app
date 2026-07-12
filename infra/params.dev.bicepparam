// infra/params.dev.bicepparam — pending OQ-2 ratification (SLICE_S0_CONTRACT.md §14, ratified §15).
// Built with the recommendation (westeurope + northeurope backup pair) as an interim, reversible
// posture: deploys are environment-gated and OIDC is not configured, so nothing provisions until
// Julien confirms OQ-2.
using 'main.bicep'

param environmentName = 'dev'
param location = 'westeurope' // pending OQ-2 ratification
param backupPairLocation = 'northeurope' // pending OQ-2 ratification
param namePrefix = 'svac-dev'
// postgresAdminPassword: read from an environment variable the deploy pipeline sets from the
// `release`/`dev` GitHub environment secret — never a literal in this file (fail-closed, IaC twin of
// L18). The fallback string is never used in a real deployment: main.bicep's own param has no default,
// so a genuinely empty/unset value still fails the deployment at the Postgres resource, not silently.
param postgresAdminPassword = readEnvironmentVariable('POSTGRES_ADMIN_PASSWORD', 'unset-fails-closed-at-deploy')
// adminHostContainerImage (SLICE_S5_CONTRACT.md §0 law d): the real Container Registry does not exist
// before OQ-3 — same fails-closed-sentinel shape as postgresAdminPassword above (not a valid image
// reference, so a deploy attempt fails at the Container App resource, not silently).
param adminHostContainerImage = readEnvironmentVariable('ADMIN_HOST_CONTAINER_IMAGE', 'unset-fails-closed-at-deploy')

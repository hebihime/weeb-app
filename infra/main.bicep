// infra/main.bicep — SLICE_S0_CONTRACT.md §9, 2A verbatim.
// Subscription-agnostic composition root: one module per Azure service (2A exit ramp = one module
// swap). S0 ships the modules and the residency/edge-guard assertions; nothing deploys until an
// environment is OIDC-configured and the deploy job is manually dispatched (§13).

targetScope = 'resourceGroup'

@description('Environment name: dev | staging | prod. Drives naming and SKU sizing per module.')
@allowed(['dev', 'staging', 'prod'])
param environmentName string

@description('Primary Azure region for ALL data-bearing resources. Single source per L21 residency rule — no module may declare its own location literal.')
param location string

@description('In-jurisdiction geo-redundant backup pair for the primary region. Asserted against an allowlist below, never left to memory.')
param backupPairLocation string

@description('Resource name prefix, e.g. "svac-dev".')
param namePrefix string

@secure()
@description('PostgreSQL administrator password. No default — an unset secret fails deployment (fail-closed, IaC twin of L18).')
param postgresAdminPassword string

// Common tags: every module receives these so `environmentName` is a real input (cost tracking /
// ops filtering by environment), not a value that arrives and is never read again.
var commonTags = {
  environment: environmentName
  'managed-by': 'bicep'
}

// --- L21 residency guard: primary/backup pair must be an allowlisted EU pair. ---
var allowedPairs = {
  westeurope: 'northeurope'
  northeurope: 'westeurope'
}
var pairIsValid = contains(allowedPairs, location) && allowedPairs[location] == backupPairLocation

resource residencyGuard 'Microsoft.Resources/deploymentScripts@2023-08-01' = if (!pairIsValid) {
  name: 'INVALID-RESIDENCY-PAIR-WILL-NOT-DEPLOY'
  location: location
  kind: 'AzureCLI'
  properties: {
    azCliVersion: '2.60.0'
    retentionInterval: 'PT1H'
    scriptContent: 'echo "residency pair ${location} -> ${backupPairLocation} is not on the in-jurisdiction allowlist" && exit 1'
  }
}

module logAnalytics 'modules/log-analytics.bicep' = {
  name: 'logAnalytics'
  params: {
    namePrefix: namePrefix
    location: location
    tags: commonTags
  }
}

module keyVault 'modules/keyvault.bicep' = {
  name: 'keyVault'
  params: {
    namePrefix: namePrefix
    location: location
    tags: commonTags
  }
}

module postgres 'modules/postgres-flexible.bicep' = {
  name: 'postgres'
  params: {
    namePrefix: namePrefix
    location: location
    backupPairLocation: backupPairLocation
    administratorLoginPassword: postgresAdminPassword
    tags: commonTags
  }
}

module redis 'modules/redis.bicep' = {
  name: 'redis'
  params: {
    namePrefix: namePrefix
    location: location
    tags: commonTags
  }
}

module blobCdn 'modules/blob-cdn.bicep' = {
  name: 'blobCdn'
  params: {
    namePrefix: namePrefix
    location: location
    tags: commonTags
  }
}

module signalr 'modules/signalr.bicep' = {
  name: 'signalr'
  params: {
    namePrefix: namePrefix
    location: location
    tags: commonTags
  }
}

module containerAppsEnv 'modules/container-apps-env.bicep' = {
  name: 'containerAppsEnv'
  params: {
    namePrefix: namePrefix
    location: location
    logAnalyticsWorkspaceId: logAnalytics.outputs.workspaceId
    tags: commonTags
  }
}

output containerAppsEnvironmentId string = containerAppsEnv.outputs.environmentId
output postgresServerFqdn string = postgres.outputs.serverFqdn
output keyVaultUri string = keyVault.outputs.vaultUri

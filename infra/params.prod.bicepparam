// infra/params.prod.bicepparam — pending OQ-2 ratification (see params.dev.bicepparam header).
using 'main.bicep'

param environmentName = 'prod'
param location = 'westeurope' // pending OQ-2 ratification
param backupPairLocation = 'northeurope' // pending OQ-2 ratification
param namePrefix = 'svac-prod'
// See params.dev.bicepparam for why this reads an env var rather than embedding a secret literal.
param postgresAdminPassword = readEnvironmentVariable('POSTGRES_ADMIN_PASSWORD', 'unset-fails-closed-at-deploy')

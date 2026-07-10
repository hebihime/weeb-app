// infra/modules/postgres-flexible.bicep — Azure Database for PostgreSQL Flexible Server, PostGIS
// enabled. Data-bearing: location comes from the single environment param, never a literal here
// (L21 residency rule, asserted by an IaC test, not memory).
param namePrefix string
param location string
param backupPairLocation string
param tags object = {}

@secure()
param administratorLoginPassword string

param administratorLogin string = 'svac_admin'

resource server 'Microsoft.DBforPostgreSQL/flexibleServers@2023-06-01-preview' = {
  name: '${namePrefix}-pg'
  location: location
  sku: {
    name: 'Standard_D2ds_v5'
    tier: 'GeneralPurpose'
  }
  properties: {
    version: '16'
    administratorLogin: administratorLogin
    administratorLoginPassword: administratorLoginPassword
    storage: { storageSizeGB: 32 }
    backup: {
      backupRetentionDays: 7
      geoRedundantBackup: 'Enabled'
    }
    highAvailability: {
      mode: 'Disabled'
    }
  }
  tags: union(tags, {
    'residency-backup-pair': backupPairLocation
  })
}

resource postgisExtension 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2023-06-01-preview' = {
  parent: server
  name: 'azure.extensions'
  properties: {
    value: 'POSTGIS'
    source: 'user-override'
  }
}

output serverFqdn string = server.properties.fullyQualifiedDomainName
output serverId string = server.id

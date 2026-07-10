// infra/modules/blob-cdn.bicep — Azure Blob storage + CDN endpoint for media/gallery/marker assets.
param namePrefix string
param location string
param tags object = {}

// Storage account names must be 3-24 lowercase alphanumeric chars and globally unique. `take()` on a
// namePrefix-derived string alone can't guarantee the 3-char floor for an arbitrarily short prefix, so
// pad with a fixed literal ("svacmedia") that alone satisfies the minimum, then suffix a short
// resource-group-scoped unique token for global uniqueness.
var storageAccountName = toLower(take('svacmedia${uniqueString(resourceGroup().id, namePrefix)}', 24))

resource storage 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  tags: tags
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
  }
}

resource cdnProfile 'Microsoft.Cdn/profiles@2023-05-01' = {
  name: '${namePrefix}-cdn'
  location: 'Global'
  tags: tags
  sku: { name: 'Standard_Microsoft' }
}

resource cdnEndpoint 'Microsoft.Cdn/profiles/endpoints@2023-05-01' = {
  parent: cdnProfile
  name: '${namePrefix}-media'
  location: 'Global'
  properties: {
    origins: [
      {
        name: 'blob-origin'
        properties: {
          hostName: replace(replace(storage.properties.primaryEndpoints.blob, 'https://', ''), '/', '')
        }
      }
    ]
  }
}

output storageAccountId string = storage.id
output cdnEndpointHostName string = cdnEndpoint.properties.hostName

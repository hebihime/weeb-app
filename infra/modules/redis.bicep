// infra/modules/redis.bicep
param namePrefix string
param location string
param tags object = {}

resource cache 'Microsoft.Cache/redis@2024-03-01' = {
  name: '${namePrefix}-redis'
  location: location
  tags: tags
  properties: {
    sku: { name: 'Basic', family: 'C', capacity: 0 }
    minimumTlsVersion: '1.2'
    publicNetworkAccess: 'Disabled'
  }
}

output hostName string = cache.properties.hostName

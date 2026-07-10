// infra/modules/signalr.bicep — Azure SignalR Service. Dev uses the self-hosted SignalR compose
// service instead (docker-compose.yml); this module is the production exit ramp (2A, 6.1-A), one
// module swap when the self-hosted dev container is retired for an environment.
param namePrefix string
param location string
param tags object = {}

resource signalR 'Microsoft.SignalRService/signalR@2023-08-01-preview' = {
  name: '${namePrefix}-signalr'
  location: location
  tags: tags
  sku: {
    name: 'Standard_S1'
    tier: 'Standard'
    capacity: 1
  }
  kind: 'SignalR'
  properties: {
    features: [
      { flag: 'ServiceMode', value: 'Default' }
    ]
  }
}

output hostName string = signalR.properties.hostName

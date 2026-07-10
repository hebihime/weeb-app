// infra/modules/container-apps-env.bicep — Azure Container Apps environment. The three ASP.NET
// hosts (public/admin/partner) + Blazor admin + background workers all deploy as container apps into
// this one environment (1A: one deployable modular monolith, multiple container-app "front doors").
param namePrefix string
param location string
param logAnalyticsWorkspaceId string
param tags object = {}

resource env 'Microsoft.App/managedEnvironments@2023-05-01' = {
  name: '${namePrefix}-cae'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: reference(logAnalyticsWorkspaceId, '2023-09-01').customerId
        sharedKey: listKeys(logAnalyticsWorkspaceId, '2023-09-01').primarySharedKey
      }
    }
  }
}

output environmentId string = env.id

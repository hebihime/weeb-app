// infra/modules/admin-host-container-app.bicep — SLICE_S5_CONTRACT.md §0 law d / §1a. The admin host's
// own Container App: its own deploy unit, internal-only ingress (never externally reachable, never
// path-mounted under the public host's domain). `bicep build`-validated now (infra.yml's
// bicep-build-lint job globs infra/**/*.bicep at maxdepth 2, so this file is picked up automatically);
// undeployable until OQ-3 (the Azure subscription) — `containerImage`/`postgresConnectionString` carry
// no real default, so a `what-if`/`deploy` attempt before OQ-3 fails closed at the parameter, exactly
// like `postgresAdminPassword` already does on `postgres-flexible.bicep` (§0's own IaC-twin-of-L18
// posture). Deliberately as simple as every other S0 module here: no Key Vault secret indirection, no
// invented Container Registry module — the connection string flows as a plain `@secure()` param exactly
// like `main.bicep` already passes `postgresAdminPassword` straight into `postgres-flexible.bicep`.
param namePrefix string
param location string
param containerAppsEnvironmentId string
param tags object = {}

@description('Fully-qualified image reference, e.g. myregistry.azurecr.io/svac-admin-host:latest. No default — an unset value fails deployment (fail-closed; the real registry does not exist before OQ-3).')
param containerImage string

@secure()
@description('The Core schema Postgres connection string this host migrates schema `admin` against and reads schema `core` config/audit through. No default — an unset secret fails deployment (fail-closed, IaC twin of L18).')
param postgresConnectionString string

// L17/§0 law d — the allowlisted-ingress half: internal-only ingress restricts the Container App to the
// Container Apps environment's own virtual network; this IP range list is the SECOND, defense-in-depth
// layer (an operator-desk-only CIDR allowlist, e.g. an office/VPN egress range) — populated by Julien's
// OQ-3 action (infra/README.md's own "Julien-executed actions" pattern), never a wildcard-open default.
@description('CIDR ranges allowed to reach the admin host even within the internal environment network. Empty = no traffic allowed yet (fail-closed default, populated at OQ-3).')
param allowedIngressCidrs array = []

resource adminHostApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: '${namePrefix}-admin-host'
  location: location
  tags: tags
  properties: {
    managedEnvironmentId: containerAppsEnvironmentId
    configuration: {
      ingress: {
        // Internal ingress: reachable only from inside the Container Apps environment's own network,
        // never from the public internet — the §0 law d "own Container App with internal/allowlisted
        // ingress" requirement's first half. Never path-mounted under a consumer domain (there is no
        // `Microsoft.App/managedEnvironments` path-based routing config here at all).
        external: false
        targetPort: 8080
        transport: 'http'
        ipSecurityRestrictions: [for (cidr, i) in allowedIngressCidrs: {
          name: 'allow-${i}'
          ipAddressRange: cidr
          action: 'Allow'
        }]
      }
      secrets: [
        {
          name: 'postgres-connection-string'
          value: postgresConnectionString
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'admin-host'
          image: containerImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            {
              name: 'ConnectionStrings__Core'
              secretRef: 'postgres-connection-string'
            }
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
            {
              // L18 fail-closed (mirrors ProdFieldKeyVaultGuard.Enforce's own prod-boot posture): never
              // set to "true" outside Development — the app-layer guard throws at startup if it ever is.
              name: 'SVAC_DEVSEAMS_ENABLED'
              value: 'false'
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 3
      }
    }
  }
}

output adminHostFqdn string = adminHostApp.properties.configuration.ingress.fqdn
output adminHostId string = adminHostApp.id

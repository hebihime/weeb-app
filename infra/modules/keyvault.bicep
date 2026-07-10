// infra/modules/keyvault.bicep — SLICE_S0_CONTRACT.md §9 Key Vault posture (P3):
// soft-delete + purge protection ON (crypto-shredding of special-category field keys is a deliberate
// 13A purge verb later, never an accident); app identities crypto-user RBAC only, no key export; key
// name "field-enc-special-category-v1" reserved now so S10's envelope encryption has a home.
param namePrefix string
param location string
param tags object = {}

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: '${namePrefix}-kv'
  location: location
  tags: tags
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: true
    enabledForDeployment: false
    enabledForDiskEncryption: false
    enabledForTemplateDeployment: false
  }
}

// Reserved key name for S10's field-level envelope encryption of special-category rows. Creating the
// key here (not the key material) reserves the name across environments so no later slice has to
// coordinate a rename. Non-exportable by RBAC policy (crypto-user role only, granted per-environment
// at S10, not here).
resource specialCategoryKey 'Microsoft.KeyVault/vaults/keys@2023-07-01' = {
  parent: vault
  name: 'field-enc-special-category-v1'
  properties: {
    kty: 'RSA'
    keySize: 2048
    keyOps: ['encrypt', 'decrypt', 'wrapKey', 'unwrapKey']
  }
}

output vaultUri string = vault.properties.vaultUri
output vaultId string = vault.id

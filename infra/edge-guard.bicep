// infra/edge-guard.bicep — SLICE_S0_CONTRACT.md §9, L17 (S0's half; S9's half is the public host that
// answers backend/e2e/edge-guard.mjs). Ingress normalization-and-reject rules: encoded traversal,
// dot-segments, and /internal reach-through 404 BEFORE any rewrite ever runs. Deployed as an Azure
// Front Door WAF custom-rule policy attached at main.bicep composition time (S9), when a public host
// exists to attach it to. The rules themselves are declared now so the adversarial e2e script
// (backend/e2e/edge-guard.mjs) has a fixed contract to test against from day one.

@description('Environment name, used only for the policy name — no location literal (this resource is not data-bearing; Front Door WAF policies are global).')
param namePrefix string

resource edgeGuardPolicy 'Microsoft.Network/frontdoorwebapplicationfirewallpolicies@2022-05-01' = {
  name: '${namePrefix}edgeguard'
  location: 'global'
  sku: {
    name: 'Premium_AzureFrontDoor'
  }
  properties: {
    policySettings: {
      enabledState: 'Enabled'
      mode: 'Prevention'
    }
    customRules: {
      rules: [
        {
          name: 'RejectEncodedTraversal'
          priority: 1
          enabledState: 'Enabled'
          ruleType: 'MatchRule'
          action: 'Block'
          matchConditions: [
            {
              matchVariable: 'RequestUri'
              operator: 'Contains'
              matchValue: ['%2e', '%2f', '%5c', '%2E', '%2F', '%5C']
              transforms: ['Lowercase']
            }
          ]
        }
        {
          name: 'RejectDotSegments'
          priority: 2
          enabledState: 'Enabled'
          ruleType: 'MatchRule'
          action: 'Block'
          matchConditions: [
            {
              matchVariable: 'RequestUri'
              operator: 'Contains'
              matchValue: ['../', '..\\', '/..', '\\..']
            }
          ]
        }
        {
          name: 'RejectInternalReachThrough'
          priority: 3
          enabledState: 'Enabled'
          ruleType: 'MatchRule'
          action: 'Block'
          matchConditions: [
            {
              matchVariable: 'RequestUri'
              operator: 'Contains'
              matchValue: ['/internal', '/internal/']
              transforms: ['Lowercase']
            }
          ]
        }
      ]
    }
  }
}

output edgeGuardPolicyId string = edgeGuardPolicy.id

targetScope = 'subscription'

@description('The principal ID to assign the role to')
param principalId string

@description('The role definition ID (built-in role GUID)')
param roleDefinitionId string

@description('The principal type')
param principalType string = 'ServicePrincipal'

resource roleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(subscription().id, principalId, roleDefinitionId)
  properties: {
    principalId: principalId
    principalType: principalType
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleDefinitionId)
  }
}

output roleAssignmentId string = roleAssignment.id
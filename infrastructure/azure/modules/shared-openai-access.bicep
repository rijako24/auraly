targetScope = 'resourceGroup'

param accountName string
param principalId string
param identityResourceId string

var cognitiveServicesOpenAiUserRoleId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd')

resource sharedOpenAi 'Microsoft.CognitiveServices/accounts@2024-10-01' existing = {
  name: accountName
}

resource sharedOpenAiUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(sharedOpenAi.id, identityResourceId, cognitiveServicesOpenAiUserRoleId)
  scope: sharedOpenAi
  properties: {
    roleDefinitionId: cognitiveServicesOpenAiUserRoleId
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}

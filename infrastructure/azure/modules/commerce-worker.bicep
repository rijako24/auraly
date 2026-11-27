param environment string
param location string
param suffix string
param tags object
param releaseVersion string
param sqlServerHost string
param databaseName string
param serviceBusName string
param processingQueues object
param webPubSubName string
param webPubSubEndpoint string
param fiscalVaultName string
param fiscalVaultUri string
param insightsConnectionString string
@secure()
param fiscalSecretProtectionKey string

var queueNames = [
  processingQueues.document
  processingQueues.accounting
  processingQueues.fiscal
  processingQueues.reporting
]

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-auraly-engines-${environment}'
  location: location
  tags: tags
}

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: 'staureng${environment}${suffix}'
  location: location
  tags: tags
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    defaultToOAuthAuthentication: true
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

resource blobs 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource packages 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobs
  name: 'commerce-worker-releases'
  properties: { publicAccess: 'None' }
}

var storageRoles = [
  'b7e6dc6d-f1e8-4753-8033-0f276bb0955b' // Storage Blob Data Owner
  '974c5e8b-45b9-4653-ba55-5f855dd0fb88' // Storage Queue Data Contributor
  '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3' // Storage Table Data Contributor
]

resource storageAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for role in storageRoles: {
  name: guid(storage.id, identity.id, role)
  scope: storage
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', role)
  }
}]

resource serviceBus 'Microsoft.ServiceBus/namespaces@2024-01-01' existing = {
  name: serviceBusName
}

resource queues 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' existing = [for name in queueNames: {
  parent: serviceBus
  name: name
}]

var senderRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39')
var receiverRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0')

resource queueSenders 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for (name, i) in queueNames: {
  name: guid(queues[i].id, identity.id, senderRole)
  scope: queues[i]
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: senderRole
  }
}]

resource queueReceivers 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for (name, i) in queueNames: {
  name: guid(queues[i].id, identity.id, receiverRole)
  scope: queues[i]
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: receiverRole
  }
}]

resource fiscalVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: fiscalVaultName
}

resource pubSub 'Microsoft.SignalRService/webPubSub@2024-03-01' existing = {
  name: webPubSubName
}

var secretsUserRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
var webPubSubRole = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '12cf5a90-567b-43ae-8102-96cf46c7d9b4')

resource fiscalSecretsAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(fiscalVault.id, identity.id, secretsUserRole)
  scope: fiscalVault
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: secretsUserRole
  }
}

resource pubSubAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(pubSub.id, identity.id, webPubSubRole)
  scope: pubSub
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: webPubSubRole
  }
}

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: 'plan-func-auraly-engines-${environment}'
  location: location
  tags: tags
  kind: 'functionapp'
  sku: { name: 'FC1', tier: 'FlexConsumption' }
  properties: { reserved: true }
}

resource app 'Microsoft.Web/sites@2024-04-01' = {
  name: 'func-auraly-engines-${environment}-${suffix}'
  location: location
  tags: tags
  kind: 'functionapp,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identity.id}': {} }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: false
    siteConfig: {
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      appSettings: [
        { name: 'AzureWebJobs.ProcessDocument.Disabled', value: 'true' }
        { name: 'AzureWebJobs.ProcessAccounting.Disabled', value: 'true' }
        { name: 'AzureWebJobs.ProcessFiscal.Disabled', value: 'true' }
        { name: 'AzureWebJobs.ProcessSalesReporting.Disabled', value: 'true' }
        { name: 'AzureWebJobs.RecoverDocumentInitialSignals.Disabled', value: 'true' }
        { name: 'DocumentInitialSignalRecoverySchedule', value: '0,15,30,45 * * * * *' }
        { name: 'DocumentProcessingQueueName', value: processingQueues.document }
        { name: 'AccountingProcessingQueueName', value: processingQueues.accounting }
        { name: 'FiscalProcessingQueueName', value: processingQueues.fiscal }
        { name: 'SalesReportingQueueName', value: processingQueues.reporting }
        { name: 'AzureWebJobsStorage__accountName', value: storage.name }
        { name: 'AzureWebJobsStorage__credential', value: 'managedidentity' }
        { name: 'AzureWebJobsStorage__clientId', value: identity.properties.clientId }
        { name: 'ServiceBusConnection__fullyQualifiedNamespace', value: '${serviceBusName}.servicebus.windows.net' }
        { name: 'ServiceBusConnection__credential', value: 'managedidentity' }
        { name: 'ServiceBusConnection__clientId', value: identity.properties.clientId }
        { name: 'ConnectionStrings__Auraly', value: 'Server=tcp:${sqlServerHost},1433;Initial Catalog=${databaseName};Authentication=Active Directory Managed Identity;User Id=${identity.properties.clientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;' }
        { name: 'AZURE_CLIENT_ID', value: identity.properties.clientId }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: insightsConnectionString }
        { name: 'ApplicationInsightsAgent_EXTENSION_VERSION', value: '~3' }
        { name: 'AZURE_FUNCTIONS_ENVIRONMENT', value: environment == 'prod' ? 'Production' : 'Development' }
        { name: 'Release__Version', value: releaseVersion }
        { name: 'Auraly__PosSynchronization__WebPubSub__Endpoint', value: webPubSubEndpoint }
        { name: 'Auraly__PosSynchronization__WebPubSub__Hub', value: 'auraly_pos' }
        { name: 'Auraly__Fiscal__CredentialStore', value: 'AzureKeyVault' }
        { name: 'Auraly__Fiscal__KeyVaultUri', value: fiscalVaultUri }
        { name: 'Auraly__Fiscal__SecretProtectionKey', value: fiscalSecretProtectionKey }
      ]
    }
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storage.properties.primaryEndpoints.blob}${packages.name}'
          authentication: {
            type: 'UserAssignedIdentity'
            userAssignedIdentityResourceId: identity.id
          }
        }
      }
      scaleAndConcurrency: {
        maximumInstanceCount: 40
        instanceMemoryMB: 2048
        alwaysReady: []
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '10.0'
      }
    }
  }
  dependsOn: [
    storageAccess
    queueReceivers
    queueSenders
    fiscalSecretsAccess
    pubSubAccess
  ]
}

output name string = app.name
output principalId string = identity.properties.principalId
output storageAccount string = storage.name
output containerName string = packages.name

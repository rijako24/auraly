targetScope = 'resourceGroup'

@description('Azure region that supports both the text and audio models.')
param location string = resourceGroup().location

@description('Release identifier applied to resource tags.')
param releaseVersion string

param textDeploymentName string = 'gpt-4.1-mini'
param textModelName string = 'gpt-4.1-mini'
param textModelVersion string = '2025-04-14'
param textCapacity int = 10

param audioDeploymentName string = 'whisper'
param audioModelName string = 'whisper'
param audioModelVersion string = '001'
param audioCapacity int = 1
param deployAudio bool = true

var suffix = toLower(take(uniqueString(subscription().id, resourceGroup().id), 8))
var accountName = 'ai-auraly-shared-${suffix}'
var tags = {
  application: 'AURALY'
  environment: 'shared'
  release: releaseVersion
  managedBy: 'Bicep'
}

resource sharedAi 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: accountName
  location: location
  kind: 'AIServices'
  sku: {
    name: 'S0'
  }
  tags: tags
  properties: {
    customSubDomainName: accountName
    disableLocalAuth: true
    publicNetworkAccess: 'Enabled'
  }
}

resource textDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: sharedAi
  name: textDeploymentName
  sku: {
    name: 'GlobalStandard'
    capacity: textCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: textModelName
      version: textModelVersion
    }
    versionUpgradeOption: 'OnceNewDefaultVersionAvailable'
  }
}

resource audioDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = if (deployAudio) {
  parent: sharedAi
  name: audioDeploymentName
  sku: {
    name: 'Standard'
    capacity: audioCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: audioModelName
      version: audioModelVersion
    }
    versionUpgradeOption: 'OnceNewDefaultVersionAvailable'
  }
}

output accountName string = sharedAi.name
output accountResourceId string = sharedAi.id
output endpoint string = sharedAi.properties.endpoint
output textDeploymentName string = textDeployment.name
output audioDeploymentName string = audioDeploymentName

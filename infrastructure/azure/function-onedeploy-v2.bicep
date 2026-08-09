targetScope = 'resourceGroup'

param functionAppName string
param location string

@secure()
param packageUri string

resource functionApp 'Microsoft.Web/sites@2024-04-01' existing = {
  name: functionAppName
}

resource oneDeploy 'Microsoft.Web/sites/extensions@2022-09-01' = {
  parent: functionApp
  name: 'onedeploy'
  location: location
  properties: {
    packageUri: packageUri
    remoteBuild: false
  }
}

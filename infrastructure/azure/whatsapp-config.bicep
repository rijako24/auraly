targetScope = 'resourceGroup'

param appConfigurationName string

@secure()
param verifyToken string

param apiBaseUrl string = 'https://graph.facebook.com/v25.0/'

resource appConfiguration 'Microsoft.AppConfiguration/configurationStores@2024-06-01' existing = {
  name: appConfigurationName
}

resource apiBaseUrlConfig 'Microsoft.AppConfiguration/configurationStores/keyValues@2024-06-01' = {
  parent: appConfiguration
  name: 'WhatsApp:Webhook:ApiBaseUrl'
  properties: {
    value: apiBaseUrl
    contentType: 'text/plain'
  }
}

resource verifyTokenConfig 'Microsoft.AppConfiguration/configurationStores/keyValues@2024-06-01' = {
  parent: appConfiguration
  name: 'WhatsApp:Webhook:VerifyToken'
  properties: {
    value: verifyToken
    contentType: 'text/plain'
  }
}

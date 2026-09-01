targetScope = 'resourceGroup'

@description('Existing Azure SQL logical server name.')
param sqlServerName string

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' existing = {
  name: sqlServerName
}

// Flex Consumption does not expose a complete stable outbound-IP set. SQL
// still authenticates every runtime connection with the environment's managed
// identity; this rule only permits the network path from Azure services.
resource azureServicesFirewallRule 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

output ruleName string = azureServicesFirewallRule.name

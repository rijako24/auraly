targetScope = 'resourceGroup'

@description('Existing Azure SQL logical server name.')
param sqlServerName string

@description('Comma-separated exact outbound IPv4 addresses allowed to reach the SQL server.')
param outboundIpAddressesCsv string

var outboundIpAddresses = split(outboundIpAddressesCsv, ',')

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' existing = {
  name: sqlServerName
}

resource appFirewallRules 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = [for ipAddress in outboundIpAddresses: {
  parent: sqlServer
  name: 'auraly-app-${uniqueString(sqlServer.id, ipAddress)}'
  properties: {
    startIpAddress: ipAddress
    endIpAddress: ipAddress
  }
}]

output ruleCount int = length(outboundIpAddresses)

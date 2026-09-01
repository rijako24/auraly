#Requires -Version 7.2

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('dev', 'prod')]
    [string]$Environment
)

$ErrorActionPreference = 'Stop'
$bicepFile = Join-Path $PSScriptRoot 'sql-app-firewall.bicep'
$resourceGroup = "RG-AURALY-$($Environment.ToUpperInvariant())"
$sqlServers = @(& az sql server list --resource-group $resourceGroup --output json |
    ConvertFrom-Json | Where-Object name -Like "sql-auraly-$Environment-*")
if ($LASTEXITCODE -ne 0 -or $sqlServers.Count -ne 1) {
    throw "Expected exactly one Auraly SQL server for $Environment in $resourceGroup."
}
$sqlServerName = $sqlServers[0].name

& az deployment group create `
    --name "auraly-$Environment-sql-firewall" `
    --resource-group $resourceGroup `
    --template-file $bicepFile `
    --parameters "sqlServerName=$sqlServerName" `
    --mode Incremental `
    --output none
if ($LASTEXITCODE -ne 0) {
    throw 'SQL firewall deployment failed.'
}

$rule = & az sql server firewall-rule show `
    --resource-group $resourceGroup `
    --server $sqlServerName `
    --name AllowAllWindowsAzureIps `
    --output json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or $rule.startIpAddress -ne '0.0.0.0' -or
    $rule.endIpAddress -ne '0.0.0.0') {
    throw 'The Azure-services SQL firewall rule was not applied correctly.'
}

# These rules were owned by earlier Auraly deployment strategies. At this
# point the replacement rule has been verified, and Publish-Database has
# already finished with (and attempted to remove) its current runner rule.
$obsoleteRules = @(& az sql server firewall-rule list `
    --resource-group $resourceGroup `
    --server $sqlServerName `
    --output json | ConvertFrom-Json | Where-Object {
        $_.name -Like 'auraly-app-*' -or
        $_.name -Like 'AuralyApp*' -or
        $_.name -Like "github-$Environment-*"
    })
if ($LASTEXITCODE -ne 0) {
    throw 'Could not inspect obsolete Auraly SQL firewall rules.'
}
foreach ($obsoleteRule in $obsoleteRules) {
    & az sql server firewall-rule delete `
        --resource-group $resourceGroup `
        --server $sqlServerName `
        --name $obsoleteRule.name `
        --output none
    if ($LASTEXITCODE -ne 0) {
        throw "Could not remove obsolete SQL firewall rule '$($obsoleteRule.name)'."
    }
}

[pscustomobject]@{
    environment = $Environment
    sqlServer = $sqlServerName
    rule = $rule.name
    authentication = 'ManagedIdentity'
    obsoleteRulesRemoved = $obsoleteRules.Count
}

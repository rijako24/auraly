#Requires -Version 5.1

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('dev', 'prod')]
    [string]$Environment,

    [switch]$RemoveTemporaryRules
)

$ErrorActionPreference = 'Stop'
$bicepFile = Join-Path $PSScriptRoot 'sql-app-firewall.bicep'
$templateFile = Join-Path ([IO.Path]::GetTempPath()) "auraly-sql-firewall-$PID.json"

$resourceGroup = "RG-AURALY-$($Environment.ToUpperInvariant())"
$webApps = @(Get-AzWebApp -ResourceGroupName $resourceGroup)
$apiApps = @($webApps | Where-Object Name -Like "api-auraly-$Environment-*")
$functionApps = @($webApps | Where-Object Name -Like "func-auraly-$Environment-*")
$sqlServers = @(Get-AzSqlServer -ResourceGroupName $resourceGroup |
    Where-Object ServerName -Like "sql-auraly-$Environment-*")

if ($apiApps.Count -ne 1 -or $functionApps.Count -ne 1 -or $sqlServers.Count -ne 1) {
    throw "Expected exactly one API, Function and SQL server tagged for $Environment in $resourceGroup."
}

$configuration = @{
    ResourceGroup = $resourceGroup
    Api = $apiApps[0].Name
    Function = $functionApps[0].Name
    Sql = $sqlServers[0].ServerName
}

$addresses = foreach ($appName in @($configuration.Api, $configuration.Function)) {
    $app = Get-AzWebApp -ResourceGroupName $configuration.ResourceGroup -Name $appName
    $app.OutboundIpAddresses -split ','
    $app.PossibleOutboundIpAddresses -split ','
}

$addresses = @($addresses |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    ForEach-Object { $_.Trim() } |
    Sort-Object -Unique)

if ($addresses.Count -eq 0) {
    throw "No outbound IP addresses were reported for $Environment."
}

try {
    & az bicep build --file $bicepFile --outfile $templateFile
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $templateFile)) {
        throw 'Bicep compilation failed.'
    }

    $deployment = New-AzResourceGroupDeployment `
    -Name "auraly-$Environment-sql-firewall" `
    -ResourceGroupName $configuration.ResourceGroup `
    -TemplateFile $templateFile `
    -TemplateParameterObject @{
        sqlServerName = $configuration.Sql
        outboundIpAddressesCsv = $addresses -join ','
    } `
    -Mode Incremental
}
finally {
    if (Test-Path -LiteralPath $templateFile) {
        Remove-Item -LiteralPath $templateFile -Force
    }
}

if ($deployment.ProvisioningState -ne 'Succeeded') {
    throw "SQL firewall deployment failed: $($deployment.ProvisioningState)"
}

$stableRules = @(Get-AzSqlServerFirewallRule `
    -ResourceGroupName $configuration.ResourceGroup `
    -ServerName $configuration.Sql |
    Where-Object FirewallRuleName -like 'auraly-app-*')

if ($stableRules.Count -ne $addresses.Count) {
    throw "Expected $($addresses.Count) stable rules, found $($stableRules.Count)."
}

if ($RemoveTemporaryRules) {
    $temporaryRules = @(Get-AzSqlServerFirewallRule `
        -ResourceGroupName $configuration.ResourceGroup `
        -ServerName $configuration.Sql |
        Where-Object FirewallRuleName -like 'AuralyApp*')

    foreach ($rule in $temporaryRules) {
        Remove-AzSqlServerFirewallRule `
            -ResourceGroupName $configuration.ResourceGroup `
            -ServerName $configuration.Sql `
            -FirewallRuleName $rule.FirewallRuleName `
            -Force
    }
}

[pscustomobject]@{
    environment = $Environment
    sqlServer = $configuration.Sql
    allowedOutboundIps = $addresses.Count
    stableRules = $stableRules.Count
    temporaryRulesRemoved = [bool]$RemoveTemporaryRules
}

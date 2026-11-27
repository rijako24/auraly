#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('dev', 'prod')][string]$Environment,
    [Parameter(Mandatory)][ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$')][string]$ReleaseVersion
)

$ErrorActionPreference = 'Stop'
$suffix = if ($Environment -eq 'dev') { 'w5usmo6w' } else { '7sov4nxc' }
$group = "RG-AURALY-$($Environment.ToUpperInvariant())"
$app = "api-auraly-$Environment-$suffix"
$engines = "func-auraly-engines-$Environment-$suffix"
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
& git -C $repo merge-base --is-ancestor HEAD origin/main
if ($LASTEXITCODE -or (& git -C $repo status --porcelain)) {
    throw 'El aprovisionamiento requiere un commit limpio integrado en origin/main.'
}

function Invoke-AzValue {
    param([Parameter(Mandatory)][string[]]$Arguments)
    $value = & az @Arguments --only-show-errors
    if ($LASTEXITCODE) { throw "No se pudo consultar Azure para $($Arguments[0]) $($Arguments[1])." }
    return ("$value").Trim()
}

$existing = Invoke-AzValue -Arguments @('resource','list','-g',$group,'--resource-type','Microsoft.Web/sites',
    '--query',"[?name=='$engines'].name | [0]",'-o','tsv')
if ($existing -eq $engines) {
    Write-Information "La Function de motores de $Environment ya está aprovisionada." -InformationAction Continue
    return
}

$raw = & az webapp config appsettings list -g $group -n $app --query '[].{name:name,value:value}' -o json --only-show-errors
if ($LASTEXITCODE) { throw 'No se pudo leer la configuración de runtime de la API.' }
$settings = @{}
foreach ($setting in ($raw | ConvertFrom-Json)) { $settings[$setting.name] = $setting.value }
$raw = $null
function Get-RuntimeSetting {
    param([string]$Name)
    $value = [string]$settings[$Name]
    if ([string]::IsNullOrWhiteSpace($value)) { throw "La API no contiene la configuración $Name." }
    return $value
}

$insights = Invoke-AzValue -Arguments @('resource','show','-g',$group,'-n',"appi-auraly-$Environment",
    '--resource-type','Microsoft.Insights/components','--query','properties.ConnectionString','-o','tsv')
$webPubSub = Get-RuntimeSetting 'Auraly__PosSynchronization__WebPubSub__Endpoint'
$vaultUri = Get-RuntimeSetting 'Auraly__Fiscal__KeyVaultUri'
$fiscalKey = Get-RuntimeSetting 'Auraly__Fiscal__SecretProtectionKey'
$parameters = @{
    environment = @{ value = $Environment }; location = @{ value = 'eastus2' }; suffix = @{ value = $suffix }
    tags = @{ value = @{ application = 'auraly'; environment = $Environment; component = 'commerce-engines' } }
    releaseVersion = @{ value = $ReleaseVersion }
    sqlServerHost = @{ value = "sql-auraly-$Environment-$suffix.database.windows.net" }
    databaseName = @{ value = "auraly-$Environment" }
    serviceBusName = @{ value = "sb-auraly-$Environment-$suffix" }
    processingQueues = @{ value = @{
        document = Get-RuntimeSetting 'Auraly__DocumentProcessing__ServiceBus__QueueName'
        accounting = Get-RuntimeSetting 'Auraly__Accounting__ServiceBus__QueueName'
        fiscal = Get-RuntimeSetting 'Auraly__Fiscal__ServiceBus__QueueName'
        reporting = Get-RuntimeSetting 'Auraly__SalesReporting__ServiceBus__QueueName'
    }}
    webPubSubName = @{ value = ([uri]$webPubSub).Host.Split('.')[0] }
    webPubSubEndpoint = @{ value = $webPubSub }
    fiscalVaultName = @{ value = ([uri]$vaultUri).Host.Split('.')[0] }
    fiscalVaultUri = @{ value = $vaultUri }
    insightsConnectionString = @{ value = $insights }
    fiscalSecretProtectionKey = @{ value = $fiscalKey }
}
$parameterPath = [IO.Path]::GetTempFileName()
try {
    @{
        '$schema' = 'https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#'
        contentVersion = '1.0.0.0'
        parameters = $parameters
    } | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath $parameterPath -Encoding utf8NoBOM
    & az deployment group validate -g $group `
        --template-file (Join-Path $PSScriptRoot 'modules/commerce-worker.bicep') `
        --parameters "@$parameterPath" --only-show-errors --output none
    if ($LASTEXITCODE) { throw 'La validación del aprovisionamiento de motores falló.' }
    & az deployment group create -g $group --name "auraly-$Environment-commerce-engines" `
        --template-file (Join-Path $PSScriptRoot 'modules/commerce-worker.bicep') `
        --parameters "@$parameterPath" --only-show-errors --output none
    if ($LASTEXITCODE) { throw 'No se pudo aprovisionar la Function de motores.' }
}
finally {
    $fiscalKey = $null
    $settings.Clear()
    $parameters.Clear()
    Remove-Item -LiteralPath $parameterPath -Force
}

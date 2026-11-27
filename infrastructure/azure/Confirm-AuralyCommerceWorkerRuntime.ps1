#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ResourceGroup,
    [Parameter(Mandatory)][string]$AppName,
    [switch]$Enable
)

$ErrorActionPreference = 'Stop'
$healthUri = "https://$AppName.azurewebsites.net/api/health"

function Confirm-WorkerHealth {
    for ($attempt = 1; $attempt -le 18; $attempt++) {
        try {
            $health = Invoke-RestMethod -Uri $healthUri -TimeoutSec 15
            if ($health.status -eq 'healthy' -and $health.service -eq 'auraly-commerce-worker') { return }
        }
        catch {
            if ($attempt -eq 18) { throw "La Function de motores no respondió en $healthUri." }
        }
        Start-Sleep -Seconds 5
    }
    throw "No se confirmó la salud de la Function de motores en $healthUri."
}

Confirm-WorkerHealth
$names = @('ProcessSalesReporting','ProcessAccounting','ProcessDocument','ProcessFiscal','RecoverDocumentInitialSignals')
$settings = & az functionapp config appsettings list --resource-group $ResourceGroup --name $AppName `
    --query "[?starts_with(name, 'AzureWebJobs.')].{name:name,value:value}" --output json --only-show-errors
if ($LASTEXITCODE -ne 0) { throw 'No se pudieron consultar los disparadores de la Function de motores.' }
$settings = $settings | ConvertFrom-Json
foreach ($trigger in $names) {
    $name = "AzureWebJobs.$trigger.Disabled"
    $value = ($settings | Where-Object name -eq $name).value
    if ($value -eq 'false') { continue }
    if ($value -ne 'true') { throw "Falta el ajuste explícito de $trigger en la Function de motores." }
    if (-not $Enable) { throw "Hay que habilitar $trigger antes de publicar la API por separado." }

    & az functionapp config appsettings set --resource-group $ResourceGroup --name $AppName `
        --settings "$name=false" --output none --only-show-errors
    if ($LASTEXITCODE -ne 0) { throw "No se pudo habilitar $trigger en la Function de motores." }
    Confirm-WorkerHealth
}

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ResourceGroup,
    [Parameter(Mandatory)][string]$AppName,
    [Parameter(Mandatory)][string]$PackagePath
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $PackagePath -PathType Leaf)) {
    throw "No existe el paquete de API: $PackagePath"
}

# Linux may keep assemblies mapped while ZIP deployment replaces their files.
# Stop the process first and wait for extraction, not application startup.
& az webapp stop --resource-group $ResourceGroup --name $AppName --output none
if ($LASTEXITCODE -ne 0) { throw 'No se pudo detener la API antes de reemplazar sus archivos.' }
$appJson = & az webapp show --resource-group $ResourceGroup --name $AppName --output json
if ($LASTEXITCODE -ne 0) { throw 'No se pudo consultar el estado de la API.' }
$app = $appJson | ConvertFrom-Json
$state = if ($null -ne $app.state) { $app.state } else { $app.properties.state }
if ("$state".Trim() -ne 'Stopped') {
    throw 'No se confirmó la detención de la API; no se reemplazarán sus archivos.'
}
& az webapp deploy --resource-group $ResourceGroup --name $AppName `
    --src-path $PackagePath --type zip --clean true --restart false `
    --async false --track-status false --timeout 1800000 --output none
if ($LASTEXITCODE -ne 0) {
    # Never restart old or partially extracted binaries after a failed cutover.
    throw 'Falló la publicación del paquete; la API permanece detenida.'
}
& az webapp start --resource-group $ResourceGroup --name $AppName --output none
if ($LASTEXITCODE -ne 0) { throw 'No se pudo iniciar la API después de publicar el paquete completo.' }

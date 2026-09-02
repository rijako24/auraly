#Requires -Version 7.2

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('dev', 'prod')]
    [string]$Environment,

    [Parameter(Mandatory)]
    [string]$ManifestPath,

    [string]$RequestedComponents,

    [switch]$Persist
)

$ErrorActionPreference = 'Stop'
$allowedComponents = @('database', 'function', 'api', 'admin', 'pos-installer')

function ConvertTo-NormalizedComponentList {
    param([AllowNull()][string[]]$Values)

    $normalized = @(
        $Values |
            ForEach-Object { "$_" -split ',' } |
            ForEach-Object { $_.Trim().ToLowerInvariant() } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Sort-Object -Unique)
    $invalid = @($normalized | Where-Object { $_ -notin $allowedComponents })
    if ($invalid.Count -gt 0) {
        throw "Componentes de despliegue no soportados: $($invalid -join ', ')."
    }
    return $normalized
}

if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
    throw "No existe el manifiesto $ManifestPath."
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$requested = @(ConvertTo-NormalizedComponentList @($RequestedComponents))
$recorded = @(ConvertTo-NormalizedComponentList @($manifest.deploymentComponents))

if ($recorded.Count -gt 0) {
    if ($requested.Count -gt 0 -and
        (Compare-Object -ReferenceObject $recorded -DifferenceObject $requested)) {
        throw 'Los componentes solicitados no coinciden con el alcance inmutable del release.'
    }
    $selected = $recorded
}
else {
    if ($requested.Count -eq 0) {
        $context = if ($Environment -eq 'prod') { 'El release legado no registró alcance' } else { 'El release DEV requiere alcance' }
        throw "$context. Indique al menos un componente modificado."
    }
    $selected = $requested
}

if ($Persist) {
    if ($Environment -ne 'dev') {
        throw 'Solo DEV puede fijar el alcance inmutable antes de archivar el release.'
    }
    $manifest | Add-Member -NotePropertyName deploymentComponents -NotePropertyValue @($selected) -Force
    $json = $manifest | ConvertTo-Json -Depth 8
    [IO.File]::WriteAllText(
        (Resolve-Path -LiteralPath $ManifestPath).Path,
        $json,
        [Text.UTF8Encoding]::new($false))
}

[pscustomobject]@{
    Components = @($selected)
    Csv = $selected -join ','
    DeployCloud = @($selected | Where-Object { $_ -in @('database', 'function', 'api', 'pos-installer') }).Count -gt 0
    DeployAdmin = $selected -contains 'admin'
    NeedsSqlPackage = $selected -contains 'database'
}

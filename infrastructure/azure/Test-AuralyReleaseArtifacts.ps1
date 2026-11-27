#Requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReleaseRoot,
    [Parameter(Mandatory)][ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$')][string]$ReleaseVersion
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath (Join-Path $ReleaseRoot 'manifest.json'))) {
    throw 'No existe manifest.json en el release.'
}
$manifest = Get-Content -LiteralPath (Join-Path $ReleaseRoot 'manifest.json') -Raw | ConvertFrom-Json
if ($manifest.product -ne 'AURALY' -or $manifest.version -ne $ReleaseVersion -or $manifest.dirty -isnot [bool] -or $manifest.dirty -or
    $manifest.commit -notmatch '^[a-fA-F0-9]{40}$') {
    throw 'El manifiesto no es un release Auraly limpio de la version solicitada.'
}
$projectionVersion=0
if (-not [int]::TryParse("$($manifest.reportingProjectionVersion)",[ref]$projectionVersion) -or $projectionVersion -lt 1 -or $projectionVersion -gt [int16]::MaxValue) {
    throw 'El release no identifica la versión canónica de reporting.'
}
if (@($manifest.artifacts.name | Sort-Object -Unique).Count -ne @($manifest.artifacts).Count) {
    throw 'El manifiesto contiene artefactos duplicados.'
}
foreach ($artifact in $manifest.artifacts) {
    if ([IO.Path]::GetFileName([string]$artifact.name) -ne $artifact.name -or $artifact.name -match '[\\/]') {
        throw 'El manifiesto contiene una ruta de artefacto no válida.'
    }
    $artifactPath = Join-Path $ReleaseRoot $artifact.name
    if (-not (Test-Path -LiteralPath $artifactPath)) {
        throw "Falta el artefacto $($artifact.name)."
    }
    $hash = (Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $artifact.sha256 -or (Get-Item -LiteralPath $artifactPath).Length -ne $artifact.bytes) {
        throw "El artefacto $($artifact.name) no coincide con el manifiesto."
    }
}
foreach ($requiredName in @(
    "auraly-admin-$ReleaseVersion.zip",
    "auraly-database-$ReleaseVersion.dacpac",
    "auraly-function-$ReleaseVersion.zip",
    "auraly-api-$ReleaseVersion.zip",
    "auraly-reporting-maintenance-$ReleaseVersion.zip",
    "auraly-pos-$ReleaseVersion.exe",
    "auraly-pos-prod-$ReleaseVersion.exe")) {
    if (-not ($manifest.artifacts.name -contains $requiredName)) {
        throw "El release no contiene $requiredName."
    }
}
if ($manifest.enginesFunction -or $manifest.deploymentComponents -contains 'engines-function') {
    $packageName = "auraly-engines-function-$ReleaseVersion.zip"
    if ($manifest.artifacts.name -notcontains $packageName) {
        throw "El release no contiene $packageName."
    }
    $archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $ReleaseRoot $packageName))
    try {
        $entry = $archive.GetEntry('functions.metadata')
        if ($null -eq $entry) { throw 'La Function de motores no contiene sus metadatos.' }
        $reader = [IO.StreamReader]::new($entry.Open())
        try { $bindings = @($reader.ReadToEnd() | ConvertFrom-Json) }
        finally { $reader.Dispose() }
        $expected = @('ProcessDocument','ProcessAccounting','ProcessFiscal','ProcessSalesReporting','RecoverDocumentInitialSignals','Health')
        if (Compare-Object -ReferenceObject $expected -DifferenceObject @($bindings.name)) {
            throw 'Los triggers de la Function de motores no coinciden con el contrato del release.'
        }
        foreach ($name in $expected | Select-Object -First 4) {
            $trigger = @($bindings | Where-Object name -eq $name).bindings | Where-Object type -eq 'serviceBusTrigger'
            if (-not $trigger.isSessionsEnabled -or $trigger.autoCompleteMessages) {
                throw "El binding de $name no conserva las sesiones y el acuse manual."
            }
        }
    }
    finally { $archive.Dispose() }
}
return $manifest

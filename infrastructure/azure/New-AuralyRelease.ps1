#Requires -Version 5.1

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [string]$OutputRoot,

    [switch]$AllowDirty,

    [ValidatePattern('^https://')]
    [string]$PosApiUrl,

    [switch]$SkipAdmin
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot 'artifacts\releases'
}
$outputRootPath = [IO.Path]::GetFullPath($OutputRoot)
$releasePath = Join-Path $outputRootPath $Version

if (-not $outputRootPath.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputRoot debe estar dentro del repositorio.'
}

if (Test-Path -LiteralPath $releasePath) {
    throw "El release $Version ya existe. Los releases son inmutables."
}

$gitStatus = (& git -C $repoRoot status --porcelain=v1)
if (-not $AllowDirty -and $gitStatus) {
    throw 'El arbol de Git tiene cambios. Confirme los cambios antes de crear un release reproducible.'
}

$commit = (& git -C $repoRoot rev-parse HEAD).Trim()
$temporaryPath = Join-Path ([IO.Path]::GetTempPath()) "auraly-release-$Version-$([guid]::NewGuid().ToString('N'))"
$publishPath = Join-Path $temporaryPath 'publish'

function New-DeterministicZip {
    param(
        [Parameter(Mandatory)][string]$SourceDirectory,
        [Parameter(Mandatory)][string]$DestinationPath
    )

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $normalizedTimestamp = [DateTimeOffset]::Parse('2000-01-01T00:00:00Z')
    $stream = [IO.File]::Open($DestinationPath, [IO.FileMode]::CreateNew)
    try {
        $archive = [IO.Compression.ZipArchive]::new(
            $stream,
            [IO.Compression.ZipArchiveMode]::Create,
            $false)
        try {
            Get-ChildItem -LiteralPath $SourceDirectory -File -Recurse |
                Sort-Object { $_.FullName.Substring($SourceDirectory.Length) } |
                ForEach-Object {
                    $relativePath = $_.FullName.Substring($SourceDirectory.Length).
                        TrimStart('\', '/').Replace('\', '/')
                    $entry = $archive.CreateEntry(
                        $relativePath,
                        [IO.Compression.CompressionLevel]::Optimal)
                    $entry.LastWriteTime = $normalizedTimestamp
                    $input = [IO.File]::OpenRead($_.FullName)
                    $output = $entry.Open()
                    try {
                        $input.CopyTo($output)
                    }
                    finally {
                        $output.Dispose()
                        $input.Dispose()
                    }
                }
        }
        finally {
            $archive.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

try {
    New-Item -ItemType Directory -Path $releasePath -Force | Out-Null
    New-Item -ItemType Directory -Path $publishPath -Force | Out-Null

    & dotnet restore (Join-Path $repoRoot 'Auraly.Commerce.sln') --locked-mode
    if ($LASTEXITCODE) { throw 'dotnet restore fallo.' }
    & dotnet restore `
        (Join-Path $repoRoot 'src\API\Auraly.Platform.Worker\Auraly.Platform.Worker.csproj') `
        --locked-mode
    if ($LASTEXITCODE) { throw 'dotnet restore de Function fallo.' }
    & dotnet restore `
        (Join-Path $repoRoot 'src\Tests\Auraly.Platform.Tests\Auraly.Platform.Tests.csproj') `
        --locked-mode
    if ($LASTEXITCODE) { throw 'dotnet restore de regresion legacy fallo.' }

    & dotnet build (Join-Path $repoRoot 'Auraly.Commerce.sln') `
        -c Release --no-restore --warnaserror `
        -p:ContinuousIntegrationBuild=true `
        -p:Deterministic=true `
        "-p:PathMap=$repoRoot=/_/src"
    if ($LASTEXITCODE) { throw 'dotnet build de Auraly Commerce fallo.' }

    & dotnet build `
        (Join-Path $repoRoot 'src\API\Auraly.Platform.Worker\Auraly.Platform.Worker.csproj') `
        -c Release --no-restore --warnaserror `
        -p:ContinuousIntegrationBuild=true `
        -p:Deterministic=true `
        "-p:PathMap=$repoRoot=/_/src"
    if ($LASTEXITCODE) { throw 'dotnet build de Function fallo.' }

    & dotnet build `
        (Join-Path $repoRoot 'src\Tests\Auraly.Platform.Tests\Auraly.Platform.Tests.csproj') `
        -c Release --no-restore --warnaserror `
        -p:ContinuousIntegrationBuild=true `
        -p:Deterministic=true `
        "-p:PathMap=$repoRoot=/_/src"
    if ($LASTEXITCODE) { throw 'dotnet build de regresion legacy fallo.' }

    & dotnet test (Join-Path $repoRoot 'src\Tests\Auraly.Platform.Tests\Auraly.Platform.Tests.csproj') `
        -c Release --no-build --logger 'console;verbosity=minimal'
    if ($LASTEXITCODE) { throw 'La regresion legacy fallo.' }

    & dotnet test (Join-Path $repoRoot 'tests\Auraly.Foundation.Tests\Auraly.Foundation.Tests.csproj') `
        -c Release --no-build --logger 'console;verbosity=minimal'
    if ($LASTEXITCODE) { throw 'Las pruebas de Auraly Foundation fallaron.' }

    & dotnet test (Join-Path $repoRoot 'tests\Auraly.Pos.Edge.Host.Tests\Auraly.Pos.Edge.Host.Tests.csproj') `
        -c Release --no-build --logger 'console;verbosity=minimal'
    if ($LASTEXITCODE) { throw 'Las pruebas de POS Edge fallaron.' }

    $functionPublish = Join-Path $publishPath 'function'
    & dotnet publish (Join-Path $repoRoot 'src\API\Auraly.Platform.Worker\Auraly.Platform.Worker.csproj') `
        -c Release --no-restore -o $functionPublish `
        -p:ContinuousIntegrationBuild=true `
        -p:Deterministic=true `
        "-p:PathMap=$repoRoot=/_/src"
    if ($LASTEXITCODE) { throw 'La publicacion de Function fallo.' }

    $apiPublish = Join-Path $publishPath 'api'
    & dotnet publish (Join-Path $repoRoot 'src\API\Auraly.Api\Auraly.Api.csproj') `
        -c Release --no-restore -o $apiPublish `
        -p:ContinuousIntegrationBuild=true `
        -p:Deterministic=true `
        "-p:PathMap=$repoRoot=/_/src"
    if ($LASTEXITCODE) { throw 'La publicacion de Auraly API fallo.' }

    & dotnet build (Join-Path $repoRoot 'database\Auraly.Database\Auraly.Database.sqlproj') `
        -c Release --no-restore
    if ($LASTEXITCODE) { throw 'La compilacion de base de datos fallo.' }

    if (-not $SkipAdmin) {
        Push-Location (Join-Path $repoRoot 'admin')
        try {
            & npm ci
            if ($LASTEXITCODE) { throw 'npm ci fallo.' }
            & npm run build
            if ($LASTEXITCODE) { throw 'La compilacion del frontend fallo.' }
        }
        finally {
            Pop-Location
        }
    }

    if ($PosApiUrl) {
        $posArtifactPath = Join-Path $temporaryPath 'pos-installer'
        & (Join-Path $repoRoot 'scripts\Build-AuralyPosInstaller.ps1') `
            -ApiUrl $PosApiUrl `
            -Configuration Release `
            -ArtifactPath $posArtifactPath
        if ($LASTEXITCODE) { throw 'La construcción del instalador POS falló.' }
        $posSetup = Join-Path $posArtifactPath 'Auraly POS Setup.exe'
        if (-not (Test-Path -LiteralPath $posSetup)) {
            throw 'La construcción no produjo Auraly POS Setup.exe.'
        }
        Copy-Item -LiteralPath $posSetup `
            -Destination (Join-Path $releasePath "auraly-pos-$Version.exe")
    }
    New-DeterministicZip `
        -SourceDirectory $functionPublish `
        -DestinationPath (Join-Path $releasePath "auraly-function-$Version.zip")
    New-DeterministicZip `
        -SourceDirectory $apiPublish `
        -DestinationPath (Join-Path $releasePath "auraly-api-$Version.zip")

    Copy-Item -LiteralPath `
        (Join-Path $repoRoot 'database\Auraly.Database\bin\Release\Auraly.Database.dacpac') `
        -Destination (Join-Path $releasePath "auraly-database-$Version.dacpac")

    $artifacts = Get-ChildItem -LiteralPath $releasePath -File |
        Sort-Object Name |
        ForEach-Object {
            [ordered]@{
                name = $_.Name
                bytes = $_.Length
                sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        }

    $manifest = [ordered]@{
        product = 'AURALY'
        version = $Version
        commit = $commit
        dirty = [bool]$gitStatus
        createdUtc = [DateTimeOffset]::UtcNow.ToString('O')
        dotnetSdk = (& dotnet --version).Trim()
        node = (& node --version).Trim()
        npm = (& npm --version).Trim()
        artifacts = @($artifacts)
    }
    $manifestJson = $manifest | ConvertTo-Json -Depth 5
    [IO.File]::WriteAllText(
        (Join-Path $releasePath 'manifest.json'),
        $manifestJson,
        [Text.UTF8Encoding]::new($false))

    Write-Host "Release AURALY $Version creado en $releasePath" -ForegroundColor Green
}
catch {
    if (Test-Path -LiteralPath $releasePath) {
        Remove-Item -LiteralPath $releasePath -Recurse -Force
    }
    throw
}
finally {
    if (Test-Path -LiteralPath $temporaryPath) {
        Remove-Item -LiteralPath $temporaryPath -Recurse -Force
    }
}

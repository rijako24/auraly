#Requires -Version 5.1

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [string]$OutputRoot,

    [switch]$AllowDirty,

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
    throw 'El ?rbol de Git tiene cambios. Confirme los cambios antes de crear un release reproducible.'
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

    & dotnet restore (Join-Path $repoRoot 'MimosBabySpa.sln') --locked-mode
    if ($LASTEXITCODE) { throw 'dotnet restore fall?.' }
    & dotnet restore `
        (Join-Path $repoRoot 'src\Tests\MimosBabySpa.Tests\MimosBabySpa.Tests.csproj') `
        --locked-mode
    if ($LASTEXITCODE) { throw 'dotnet restore de pruebas fall?.' }

    & dotnet build (Join-Path $repoRoot 'MimosBabySpa.sln') `
        -c Release --no-restore `
        -p:ContinuousIntegrationBuild=true `
        -p:Deterministic=true `
        "-p:PathMap=$repoRoot=/_/src"
    if ($LASTEXITCODE) { throw 'dotnet build fall?.' }

    & dotnet build `
        (Join-Path $repoRoot 'src\Tests\MimosBabySpa.Tests\MimosBabySpa.Tests.csproj') `
        -c Release --no-restore `
        -p:ContinuousIntegrationBuild=true `
        -p:Deterministic=true `
        "-p:PathMap=$repoRoot=/_/src"
    if ($LASTEXITCODE) { throw 'dotnet build de pruebas fall?.' }

    & dotnet test (Join-Path $repoRoot 'src\Tests\MimosBabySpa.Tests\MimosBabySpa.Tests.csproj') `
        -c Release --no-build --logger 'console;verbosity=minimal'
    if ($LASTEXITCODE) { throw 'dotnet test fall?.' }

    $functionPublish = Join-Path $publishPath 'function'
    & dotnet publish (Join-Path $repoRoot 'src\API\MimosBabySpa.API\MimosBabySpa.API.csproj') `
        -c Release --no-build -o $functionPublish `
        -p:ContinuousIntegrationBuild=true `
        -p:Deterministic=true `
        "-p:PathMap=$repoRoot=/_/src"
    if ($LASTEXITCODE) { throw 'La publicaci?n de Function fall?.' }

    $apiPublish = Join-Path $publishPath 'api'
    & dotnet publish (Join-Path $repoRoot 'src\API\MimosBabySpa.WebAPI\MimosBabySpa.WebAPI.csproj') `
        -c Release --no-build -o $apiPublish `
        -p:ContinuousIntegrationBuild=true `
        -p:Deterministic=true `
        "-p:PathMap=$repoRoot=/_/src"
    if ($LASTEXITCODE) { throw 'La publicaci?n de Web API fall?.' }

    & dotnet build (Join-Path $repoRoot 'database\MimosBabySpa.Database\MimosBabySpa.Database.sqlproj') `
        -c Release --no-restore
    if ($LASTEXITCODE) { throw 'La compilaci?n de base de datos fall?.' }

    if (-not $SkipAdmin) {
        Push-Location (Join-Path $repoRoot 'admin')
        try {
            & npm ci
            if ($LASTEXITCODE) { throw 'npm ci fall?.' }
            & npm run build
            if ($LASTEXITCODE) { throw 'La compilaci?n de Admin fall?.' }
        }
        finally {
            Pop-Location
        }
    }

    New-DeterministicZip `
        -SourceDirectory $functionPublish `
        -DestinationPath (Join-Path $releasePath "auraly-function-$Version.zip")
    New-DeterministicZip `
        -SourceDirectory $apiPublish `
        -DestinationPath (Join-Path $releasePath "auraly-api-$Version.zip")

    Copy-Item -LiteralPath `
        (Join-Path $repoRoot 'database\MimosBabySpa.Database\bin\Release\MimosBabySpa.Database.dacpac') `
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

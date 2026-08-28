[CmdletBinding()]
param(
    [string]$ApiUrl = "http://127.0.0.1:5097",
    [string]$Version = "0.0.0-dev",
    [string]$Configuration = "Release",
    [string]$ArtifactPath = "",
    [ValidatePattern('^[0-9A-Fa-f]{40}$')]
    [string]$SigningCertificateThumbprint,
    [ValidatePattern('^https://')]
    [string]$SigningTimestampUrl,
    [ValidateSet('CurrentUser', 'LocalMachine')]
    [string]$SigningCertificateStoreLocation = 'CurrentUser',
    [string]$SignToolPath,
    [switch]$RequireSignature
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$artifacts = if ([string]::IsNullOrWhiteSpace($ArtifactPath)) {
    Join-Path $root "artifacts\auraly-pos"
}
elseif ([IO.Path]::IsPathRooted($ArtifactPath)) {
    $ArtifactPath
}
else {
    Join-Path $root $ArtifactPath
}
$payload = Join-Path $artifacts "payload"
$edge = Join-Path $payload "edge"
$web = Join-Path $payload "web"
$runtime = Join-Path $payload "runtime"
$desktopPublish = Join-Path $artifacts "desktop"
$msiBuild = Join-Path $artifacts "msi-build"
$bundleBuild = Join-Path $artifacts "bundle-build"
$msiIntermediate = Join-Path $artifacts "msi-obj"
$bundleIntermediate = Join-Path $artifacts "bundle-obj"
$bundleSigning = Join-Path $artifacts "bundle-signing"
$msi = Join-Path $artifacts "Auraly.Pos.Setup.msi"
$setup = Join-Path $artifacts "Auraly Setup.exe"
$utf8 = [Text.UTF8Encoding]::new($false)
$normalizedThumbprint = if ([string]::IsNullOrWhiteSpace($SigningCertificateThumbprint)) {
    ""
}
else {
    $SigningCertificateThumbprint.ToUpperInvariant()
}

function ConvertTo-MsiProductVersion([string]$value) {
    $match = [regex]::Match(
        $value,
        '^(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)(?:-(?<pre>[0-9A-Za-z.-]+))?$')
    if (-not $match.Success) {
        throw "La versión '$value' no es SemVer válida."
    }

    $major = [int]$match.Groups['major'].Value
    $minor = [int]$match.Groups['minor'].Value
    $patch = [int]$match.Groups['patch'].Value
    if ($major -gt 255 -or $minor -gt 255 -or $patch -gt 65) {
        throw "La versión '$value' excede los límites de Windows Installer."
    }

    $rank = 999
    if ($match.Groups['pre'].Success) {
        $pre = $match.Groups['pre'].Value
        $preMatch = [regex]::Match(
            $pre,
            '^(?<name>alpha|beta|rc)[.-]?(?<number>\d+)(?:[.-].*)?$',
            'IgnoreCase')
        if (-not $preMatch.Success) {
            throw "La versión preliminar '$pre' debe usar alphaN, betaN o rcN para conservar el orden de actualización MSI."
        }
        $number = [int]$preMatch.Groups['number'].Value
        if ($number -gt 299) {
            throw "La secuencia preliminar '$pre' supera el máximo 299."
        }
        $rank = switch ($preMatch.Groups['name'].Value.ToLowerInvariant()) {
            'alpha' { $number }
            'beta' { 300 + $number }
            'rc' { 600 + $number }
        }
    }

    $build = ($patch * 1000) + $rank
    return "$major.$minor.$build"
}

function Invoke-AuralySigning([string[]]$path) {
    if ([string]::IsNullOrWhiteSpace($normalizedThumbprint)) {
        if ($RequireSignature) {
            throw 'SigningCertificateThumbprint es obligatorio para un instalador firmado.'
        }
        return
    }
    if ([string]::IsNullOrWhiteSpace($SigningTimestampUrl)) {
        throw 'SigningTimestampUrl es obligatorio al firmar.'
    }

    $arguments = @{
        Path = $path
        CertificateThumbprint = $normalizedThumbprint
        TimestampUrl = $SigningTimestampUrl
        CertificateStoreLocation = $SigningCertificateStoreLocation
    }
    if (-not [string]::IsNullOrWhiteSpace($SignToolPath)) {
        $arguments.SignToolPath = $SignToolPath
    }
    & (Join-Path $PSScriptRoot 'Sign-AuralyWindowsArtifact.ps1') @arguments
}

function Invoke-WixProjectBuild(
    [string]$project,
    [string[]]$arguments,
    [string]$failureMessage) {
    $maximumAttempts = 2
    for ($attempt = 1; $attempt -le $maximumAttempts; $attempt++) {
        $output = @(& dotnet build $project @arguments 2>&1)
        $exitCode = $LASTEXITCODE
        $output | ForEach-Object { Write-Host $_ }

        if ($exitCode -eq 0) {
            return
        }

        $hasTransientPipeFailure = ($output -join [Environment]::NewLine) -match
            'WIX0001:\s+System\.IO\.IOException:\s+The pipe is being closed\.'
        if ($attempt -lt $maximumAttempts -and $hasTransientPipeFailure) {
            Write-Warning 'WiX cerró su canal nativo durante la primera ejecución; se reintentará una sola vez.'
            Start-Sleep -Seconds 3
            continue
        }

        throw $failureMessage
    }
}

if ($RequireSignature -and [string]::IsNullOrWhiteSpace($normalizedThumbprint)) {
    throw 'SigningCertificateThumbprint es obligatorio para un instalador de release.'
}
if (-not [string]::IsNullOrWhiteSpace($normalizedThumbprint) -and
    [string]::IsNullOrWhiteSpace($SigningTimestampUrl)) {
    throw 'SigningTimestampUrl es obligatorio al firmar.'
}

$msiProductVersion = ConvertTo-MsiProductVersion $Version

if (Test-Path -LiteralPath $artifacts) {
    Remove-Item -LiteralPath $artifacts -Recurse -Force
}
New-Item -ItemType Directory -Force -Path `
    $edge,$web,$runtime,$desktopPublish,$msiBuild,$bundleBuild,$bundleSigning,`
    $msiIntermediate,$bundleIntermediate | Out-Null

Push-Location (Join-Path $root "admin")
try {
    $env:NEXT_PUBLIC_AURALY_POS_EDGE_URL = "http://127.0.0.1:47831"
    $env:AURALY_DESKTOP_BUILD = "1"
    npm run build
    if ($LASTEXITCODE -ne 0) {
        throw "The POS web application could not be built."
    }
}
finally {
    Remove-Item Env:AURALY_DESKTOP_BUILD -ErrorAction SilentlyContinue
    Pop-Location
}

dotnet publish `
    (Join-Path $root "src\Pos\Auraly.Pos.Edge.Host\Auraly.Pos.Edge.Host.csproj") `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    --output $edge `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true
if ($LASTEXITCODE -ne 0) { throw 'La publicación de POS Edge falló.' }

dotnet publish `
    (Join-Path $root "src\Desktop\Auraly.Desktop\Auraly.Desktop.csproj") `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    --output $desktopPublish `
    -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) { throw 'La publicación de Auraly Desktop falló.' }

Copy-Item -Path (Join-Path $desktopPublish "*") -Destination $payload -Recurse -Force
$node = (Get-Command node.exe -ErrorAction Stop).Source
Copy-Item -LiteralPath $node -Destination $runtime
Copy-Item -Path (Join-Path $root "admin\.next\standalone\*") `
    -Destination $web -Recurse -Force
New-Item -ItemType Directory -Force -Path (Join-Path $web ".next") | Out-Null
Copy-Item -LiteralPath (Join-Path $root "admin\.next\static") `
    -Destination (Join-Path $web ".next\static") -Recurse -Force
Copy-Item -LiteralPath (Join-Path $root "admin\public") `
    -Destination (Join-Path $web "public") -Recurse -Force

$desktopSettings = @{
    apiUrl = $ApiUrl
    version = $Version
    webPort = 47830
    edgePort = 47831
    publisherCertificateThumbprint = $normalizedThumbprint
} | ConvertTo-Json
[IO.File]::WriteAllText(
    (Join-Path $payload "desktopsettings.json"),
    $desktopSettings,
    $utf8)

$auralyBinaries = @(
    Get-ChildItem -LiteralPath $payload -Recurse -File |
        Where-Object {
            $_.Name -like 'Auraly*.exe' -or $_.Name -like 'Auraly*.dll'
        } |
        Select-Object -ExpandProperty FullName
)
if ($auralyBinaries.Count -eq 0) {
    throw 'La publicación del POS no produjo binarios Auraly para firmar.'
}
Invoke-AuralySigning $auralyBinaries

dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw 'No fue posible restaurar WiX Toolset.' }

$msiProject = Join-Path $root 'src\Installer\Auraly.Pos.Setup\Auraly.Pos.Setup.wixproj'
Invoke-WixProjectBuild `
    $msiProject `
    @(
        '--configuration', $Configuration,
        "-p:PayloadDir=$payload",
        "-p:ProductVersion=$msiProductVersion",
        "-p:IntermediateOutputPath=$msiIntermediate\",
        "-p:OutputPath=$msiBuild\") `
    'La construcción del MSI de Auraly falló.'
$builtMsi = Join-Path $msiBuild 'Auraly.Pos.Setup.msi'
if (-not (Test-Path -LiteralPath $builtMsi -PathType Leaf)) {
    throw 'WiX no produjo el MSI final de Auraly en la salida esperada.'
}
Copy-Item -LiteralPath $builtMsi -Destination $msi
Invoke-AuralySigning @($msi)

$bundleProject = Join-Path $root 'src\Installer\Auraly.Pos.Bundle\Auraly.Pos.Bundle.wixproj'
Invoke-WixProjectBuild `
    $bundleProject `
    @(
        '--configuration', $Configuration,
        "-p:MsiPath=$msi",
        "-p:BundleVersion=$msiProductVersion",
        "-p:IntermediateOutputPath=$bundleIntermediate\",
        "-p:OutputPath=$bundleBuild\") `
    'La construcción del bundle de Auraly falló.'
$unsignedBundle = Join-Path $bundleBuild 'Auraly.Pos.Bundle.exe'
if (-not (Test-Path -LiteralPath $unsignedBundle -PathType Leaf)) {
    throw 'WiX no produjo el bundle final de Auraly en la salida esperada.'
}

if ([string]::IsNullOrWhiteSpace($normalizedThumbprint)) {
    Copy-Item -LiteralPath $unsignedBundle -Destination $setup
}
else {
    $engine = Join-Path $bundleSigning 'Auraly.Pos.Bundle.Engine.exe'
    dotnet tool run wix -- burn detach $unsignedBundle -engine $engine
    if ($LASTEXITCODE -ne 0) { throw 'WiX no pudo separar el motor del bundle para firmarlo.' }
    Invoke-AuralySigning @($engine)
    dotnet tool run wix -- burn reattach $unsignedBundle -engine $engine -o $setup
    if ($LASTEXITCODE -ne 0) { throw 'WiX no pudo reensamblar el bundle firmado.' }
    Invoke-AuralySigning @($setup)
}

$file = Get-Item -LiteralPath $setup
$hash = Get-FileHash -LiteralPath $setup -Algorithm SHA256
$signature = Get-AuthenticodeSignature -LiteralPath $setup
$hasExpectedSigner = $null -ne $signature.SignerCertificate -and
    $signature.SignerCertificate.Thumbprint -eq $normalizedThumbprint
$isSelfSignedSigner = $hasExpectedSigner -and
    $signature.SignerCertificate.Subject -eq $signature.SignerCertificate.Issuer
$hasAcceptedStatus = $signature.Status -eq 'Valid' -or
    ($isSelfSignedSigner -and
     $signature.Status -in @('NotTrusted', 'UnknownError'))
if ($RequireSignature -and (-not $hasExpectedSigner -or -not $hasAcceptedStatus)) {
    throw "El instalador final no tiene una firma Authenticode válida. Estado: $($signature.Status)."
}

[pscustomobject]@{
    Path = $file.FullName
    Bytes = $file.Length
    Sha256 = $hash.Hash
    Signature = $signature.Status
    SignerThumbprint = $signature.SignerCertificate.Thumbprint
    MsiPath = $msi
}

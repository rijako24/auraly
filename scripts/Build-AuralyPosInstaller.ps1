param(
    [string]$IdentityApiUrl = "http://127.0.0.1:5057/api",
    [string]$CommerceApiUrl = "http://127.0.0.1:5097",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root "artifacts\auraly-pos"
$payload = Join-Path $artifacts "payload"
$edge = Join-Path $payload "edge"
$web = Join-Path $payload "web"
$runtime = Join-Path $payload "runtime"
$desktopPublish = Join-Path $artifacts "desktop"
$zip = Join-Path $artifacts "payload.zip"
$installScript = Join-Path $artifacts "install.ps1"
$sed = Join-Path $artifacts "AuralyPosSetup.sed"
$setup = Join-Path $artifacts "Auraly POS Setup.exe"
$utf8 = [Text.UTF8Encoding]::new($false)

if (Test-Path -LiteralPath $artifacts) {
    Remove-Item -LiteralPath $artifacts -Recurse -Force
}
New-Item -ItemType Directory -Force -Path `
    $edge,$web,$runtime,$desktopPublish | Out-Null

Push-Location (Join-Path $root "admin")
try {
    $env:NEXT_PUBLIC_AURALY_POS_EDGE_URL = "http://127.0.0.1:47831"
    npm run build
}
finally {
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

dotnet publish `
    (Join-Path $root "src\Desktop\Auraly.Desktop\Auraly.Desktop.csproj") `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    --output $desktopPublish `
    -p:PublishSingleFile=false

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
    identityApiUrl = $IdentityApiUrl
    commerceApiUrl = $CommerceApiUrl
    webPort = 47830
    edgePort = 47831
} | ConvertTo-Json
[IO.File]::WriteAllText(
    (Join-Path $payload "desktopsettings.json"),
    $desktopSettings,
    $utf8)

& tar.exe -a -c -f $zip -C $payload .
if ($LASTEXITCODE -ne 0) {
    throw "The POS payload could not be compressed."
}

$install = @'
$ErrorActionPreference = "Stop"
$install = Join-Path $env:LOCALAPPDATA "Programs\Auraly POS"

Get-Process -Name "Auraly.Desktop" -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue

$browserProfile = Join-Path $env:LOCALAPPDATA "Auraly\Desktop\Browser"
for ($attempt = 0; $attempt -lt 20; $attempt++) {
    $children = @(
        Get-CimInstance Win32_Process |
            Where-Object {
                ($_.Name -eq "node.exe" -and $_.CommandLine -like "*$install*") -or
                ($_.Name -eq "Auraly.Pos.Edge.Host.exe" -and
                    $_.ExecutablePath -like "$install*") -or
                ($_.Name -eq "msedge.exe" -and
                    $_.CommandLine -like "*$browserProfile*")
            }
    )
    foreach ($process in $children) {
        Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
    }
    if (-not (Get-Process -Name "Auraly.Desktop" -ErrorAction SilentlyContinue) -and
        $children.Count -eq 0) {
        break
    }
    Start-Sleep -Milliseconds 250
}

if (Test-Path -LiteralPath $install) {
    Remove-Item -LiteralPath $install -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $install | Out-Null
Expand-Archive -LiteralPath (Join-Path $PSScriptRoot "payload.zip") `
    -DestinationPath $install -Force

$desktop = [Environment]::GetFolderPath("DesktopDirectory")
$shortcut = Join-Path $desktop "Auraly POS.lnk"
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($shortcut)
$link.TargetPath = Join-Path $install "Auraly.Desktop.exe"
$link.WorkingDirectory = $install
$link.Description = "Auraly POS"
$link.Save()

Start-Process -FilePath (Join-Path $install "Auraly.Desktop.exe") `
    -WorkingDirectory $install
'@
[IO.File]::WriteAllText($installScript, $install, $utf8)

$sedText = @"
[Version]
Class=IEXPRESS
SEDVersion=3
[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=0
HideExtractAnimation=1
UseLongFileName=1
InsideCompressed=0
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=
DisplayLicense=
FinishMessage=
TargetName=$setup
FriendlyName=Auraly POS
AppLaunched=powershell.exe -NoProfile -ExecutionPolicy Bypass -File install.ps1
PostInstallCmd=<None>
AdminQuietInstCmd=
UserQuietInstCmd=
SourceFiles=SourceFiles
[Strings]
FILE0="payload.zip"
FILE1="install.ps1"
[SourceFiles]
SourceFiles0=$artifacts\
[SourceFiles0]
%FILE0%=
%FILE1%=
"@
[IO.File]::WriteAllText($sed, $sedText, [Text.Encoding]::ASCII)

$iexpress = Join-Path $env:WINDIR "System32\iexpress.exe"
Start-Process -FilePath $iexpress -ArgumentList @("/N", $sed) -WindowStyle Hidden |
    Out-Null

$deadline = [DateTimeOffset]::Now.AddMinutes(15)
$stableChecks = 0
$lastLength = -1L
while ([DateTimeOffset]::Now -lt $deadline) {
    if (Test-Path -LiteralPath $setup) {
        $length = (Get-Item -LiteralPath $setup).Length
        if ($length -gt 0 -and $length -eq $lastLength) {
            $stableChecks++
        }
        else {
            $stableChecks = 0
            $lastLength = $length
        }
        if ($stableChecks -ge 5) {
            break
        }
    }
    Start-Sleep -Seconds 1
}

if (-not (Test-Path -LiteralPath $setup)) {
    throw "IExpress did not produce the installer."
}

$file = Get-Item -LiteralPath $setup
$hash = Get-FileHash -LiteralPath $setup -Algorithm SHA256
[pscustomobject]@{
    Path = $file.FullName
    Bytes = $file.Length
    Sha256 = $hash.Hash
}

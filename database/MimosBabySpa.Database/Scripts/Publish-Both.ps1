# Publica la base de datos en LOCAL y en AZURE
# Uso:
#   .\Publish-Both.ps1
#   .\Publish-Both.ps1 -AzureSqlUsername "sqladmin" -AzureSqlPassword "TuPassword"
#   .\Publish-Both.ps1 -SkipAzure  # solo local
#   .\Publish-Both.ps1 -SkipLocal  # solo nube

param(
    [Parameter(Mandatory=$false)]
    [switch]$SkipLocal = $false,

    [Parameter(Mandatory=$false)]
    [switch]$SkipAzure = $false,

    [Parameter(Mandatory=$false)]
    [string]$ResourceGroupName = "MimosBabySpa-RG",

    [Parameter(Mandatory=$false)]
    [string]$AzureSqlServer = "",

    [Parameter(Mandatory=$false)]
    [string]$AzureDatabase = "MimosBabySpa",

    [Parameter(Mandatory=$false)]
    [string]$AzureSqlUsername = "",

    [Parameter(Mandatory=$false)]
    [string]$AzureSqlPassword = "",

    [Parameter(Mandatory=$false)]
    [string]$LocalServerInstance = ".\LOCAL",

    [Parameter(Mandatory=$false)]
    [string]$LocalDatabase = "talkioai",

    [Parameter(Mandatory=$false)]
    [string]$LocalUsername = "admin",

    [Parameter(Mandatory=$false)]
    [string]$LocalPassword = "masterkey"
)

$ErrorActionPreference = "Stop"

$scriptDir = $PSScriptRoot
$projectDir = Split-Path -Parent $scriptDir
$projectPath = Join-Path $projectDir "MimosBabySpa.Database.sqlproj"
$dacpacPath = Join-Path $projectDir "bin\Debug\MimosBabySpa.Database.dacpac"

# Compilar una sola vez
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "  Compilando proyecto de base de datos" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
dotnet build $projectPath --configuration Debug
if ($LASTEXITCODE -ne 0) { exit 1 }
if (-not (Test-Path $dacpacPath)) {
    Write-Host "Error: No se generó el DACPAC." -ForegroundColor Red
    exit 1
}

# Buscar SqlPackage
$sqlPackagePaths = @(
    "C:\Program Files\Microsoft SQL Server\160\DAC\bin\SqlPackage.exe",
    "C:\Program Files\Microsoft SQL Server\150\DAC\bin\SqlPackage.exe",
    "C:\Program Files\Microsoft SQL Server\140\DAC\bin\SqlPackage.exe"
)
$sqlPackagePath = $null
foreach ($p in $sqlPackagePaths) {
    if (Test-Path $p) { $sqlPackagePath = $p; break }
}
if (-not $sqlPackagePath) {
    Write-Host "Error: SqlPackage.exe no encontrado." -ForegroundColor Red
    exit 1
}

function Publish-Database {
    param([string]$ConnStr, [string]$TargetName)
    Write-Host "`nPublicando en $TargetName..." -ForegroundColor Yellow
    $args = @(
        "/Action:Publish",
        "/SourceFile:$dacpacPath",
        "/TargetConnectionString:$ConnStr",
        "/p:BackupDatabaseBeforeChanges=False",
        "/p:DropObjectsNotInSource=True",
        "/p:BlockOnPossibleDataLoss=False",
        "/p:DoNotAlterChangeDataCaptureObjects=True",
        "/p:DoNotAlterReplicatedObjects=True",
        "/p:DoNotDropObjectTypes=Users;Logins;RoleMembership;Permissions"
    )
    & $sqlPackagePath $args
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  Publicado correctamente en $TargetName" -ForegroundColor Green
        return $true
    } else {
        Write-Host "  Error al publicar en $TargetName" -ForegroundColor Red
        return $false
    }
}

$localOk = $true
$azureOk = $true

# --- LOCAL ---
if (-not $SkipLocal) {
    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host "  1. PUBLICAR EN LOCAL" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    $localConn = "Server=$LocalServerInstance;Database=$LocalDatabase;User Id=$LocalUsername;Password=$LocalPassword;TrustServerCertificate=True;"
    $localOk = Publish-Database -ConnStr $localConn -TargetName "LOCAL ($LocalServerInstance\$LocalDatabase)"
}

# --- AZURE ---
if (-not $SkipAzure) {
    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host "  2. PUBLICAR EN AZURE" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan

    $server = $AzureSqlServer
    $user = if ($AzureSqlUsername) { $AzureSqlUsername } else { $env:AZURE_SQL_USERNAME }
    $pass = if ($AzureSqlPassword) { $AzureSqlPassword } else { $env:AZURE_SQL_PASSWORD }

    if ([string]::IsNullOrEmpty($server)) {
        # Intentar descubrir servidor desde Azure
        try {
            $azContext = Get-AzContext -ErrorAction SilentlyContinue
            if (-not $azContext) {
                Write-Host "  No hay sesión de Azure. Conectando..." -ForegroundColor Yellow
                Connect-AzAccount | Out-Null
            }
            $sqlServers = Get-AzSqlServer -ResourceGroupName $ResourceGroupName -ErrorAction Stop
            if ($sqlServers -and $sqlServers.Count -gt 0) {
                $first = $sqlServers[0]
                $server = "$($first.ServerName).database.windows.net"
                Write-Host "  Servidor encontrado: $server" -ForegroundColor Gray
            }
        } catch {
            Write-Host "  No se pudo obtener el servidor de Azure: $_" -ForegroundColor Yellow
        }
    }

    if ([string]::IsNullOrEmpty($server)) {
        Write-Host "  Omitiendo Azure: especifica -AzureSqlServer (o usa ResourceGroup para descubrirlo)" -ForegroundColor Yellow
        Write-Host "  Ejemplo: .\Publish-Both.ps1 -AzureSqlServer 'mimosbabyspa-sql-dev-xxx.database.windows.net' -AzureSqlUsername 'sqladmin' -AzureSqlPassword 'TuPassword'" -ForegroundColor Gray
        $azureOk = $false
    } elseif ([string]::IsNullOrEmpty($user) -or [string]::IsNullOrEmpty($pass)) {
        Write-Host "  Omitiendo Azure: faltan -AzureSqlUsername y -AzureSqlPassword" -ForegroundColor Yellow
        $azureOk = $false
    } else {
        $azureConn = "Server=tcp:$server,1433;Initial Catalog=$AzureDatabase;User ID=$user;Password=$pass;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
        $azureOk = Publish-Database -ConnStr $azureConn -TargetName "AZURE ($server/$AzureDatabase)"
    }
}

# Resumen
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "  RESUMEN" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Local: $(if ($SkipLocal) { 'Omitido' } elseif ($localOk) { 'OK' } else { 'Error' })" -ForegroundColor $(if ($localOk) { 'Green' } else { 'Red' })
Write-Host "  Azure: $(if ($SkipAzure) { 'Omitido' } elseif ($azureOk) { 'OK' } else { 'Error / Omitido' })" -ForegroundColor $(if ($azureOk) { 'Green' } else { 'Yellow' })
Write-Host ""

if (-not $localOk -or (-not $SkipAzure -and -not $azureOk)) {
    exit 1
}

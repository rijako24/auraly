# Ejecuta el script SQL para crear la tabla PaymentTransactions.
# Usa la cadena de conexión del appsettings.json de la Consola.

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$sqlFile = Join-Path $scriptDir "CreatePaymentTransactionsTable.sql"

# Leer connection string desde appsettings.json
$appSettingsPath = Join-Path $scriptDir "..\src\Console\MimosBabySpa.Console\appSettings.json"
if (-not (Test-Path $appSettingsPath)) {
    $appSettingsPath = Join-Path $scriptDir "..\src\Console\MimosBabySpa.Console\appsettings.json"
}
if (-not (Test-Path $appSettingsPath)) {
    Write-Host "No se encontró appsettings.json en la consola." -ForegroundColor Red
    exit 1
}

$config = Get-Content $appSettingsPath -Raw | ConvertFrom-Json
$connStr = $config.ConnectionStrings.DefaultConnection

# Parsear ConnectionString para sqlcmd (formato: Server=...;Database=...;User Id=...;Password=...)
$parts = @{}
$connStr -split ";" | ForEach-Object {
    if ($_ -match "(.+?)=(.+)") { $parts[$matches[1].Trim()] = $matches[2].Trim() }
}
$server = ($parts["Server"] -replace "\\\\", "\") -replace "\.\\", ".\"
$database = $parts["Database"]
$user = $parts["User Id"]
$password = $parts["Password"]
$trustCert = $parts["TrustServerCertificate"]

$sqlcmdArgs = @(
    "-S", $server,
    "-d", $database,
    "-U", $user,
    "-P", $password,
    "-i", $sqlFile
)
if ($trustCert -eq "True") { $sqlcmdArgs += "-C" }

Write-Host "Creando tabla PaymentTransactions..." -ForegroundColor Cyan
try {
    & sqlcmd @sqlcmdArgs
    Write-Host "Listo." -ForegroundColor Green
} catch {
    Write-Host "Error: $_" -ForegroundColor Red
    Write-Host "Asegurate de tener sqlcmd instalado (viene con SQL Server)." -ForegroundColor Yellow
    exit 1
}

# Script para crear la base de datos inicial
# Uso: .\CreateDatabase.ps1 -ServerInstance "localhost" -DatabaseName "MimosBabySpa"

param(
    [Parameter(Mandatory=$true)]
    [string]$ServerInstance,
    
    [Parameter(Mandatory=$true)]
    [string]$DatabaseName,
    
    [Parameter(Mandatory=$false)]
    [string]$Username,
    
    [Parameter(Mandatory=$false)]
    [string]$Password,
    
    [Parameter(Mandatory=$false)]
    [switch]$UseIntegratedSecurity = $true
)

$ErrorActionPreference = "Stop"

Write-Host "Creando base de datos: $DatabaseName" -ForegroundColor Green
Write-Host "Servidor: $ServerInstance" -ForegroundColor Cyan

# Construir la cadena de conexión al servidor (sin especificar base de datos)
$masterConnectionString = "Server=$ServerInstance;Database=master;"
if ($UseIntegratedSecurity) {
    $masterConnectionString += "Integrated Security=True;"
} else {
    if ([string]::IsNullOrEmpty($Username) -or [string]::IsNullOrEmpty($Password)) {
        Write-Host "Error: Se requiere Username y Password cuando UseIntegratedSecurity es False" -ForegroundColor Red
        exit 1
    }
    $masterConnectionString += "User Id=$Username;Password=$Password;"
}

try {
    # Crear la base de datos si no existe
    $createDbQuery = @"
IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = '$DatabaseName')
BEGIN
    CREATE DATABASE [$DatabaseName]
    PRINT 'Base de datos $DatabaseName creada correctamente.'
END
ELSE
BEGIN
    PRINT 'La base de datos $DatabaseName ya existe.'
END
"@

    Write-Host "`nEjecutando script de creación..." -ForegroundColor Yellow
    Invoke-Sqlcmd -ServerInstance $ServerInstance -Database "master" -Query $createDbQuery -TrustServerCertificate
    
    Write-Host "`nBase de datos lista para ser publicada." -ForegroundColor Green
    Write-Host "Ejecuta: .\Publish.ps1 -ServerInstance `"$ServerInstance`" -DatabaseName `"$DatabaseName`"" -ForegroundColor Cyan
    
} catch {
    Write-Host "`nError al crear la base de datos: $_" -ForegroundColor Red
    exit 1
}

# Script para ejecutar InsertContextFieldsMapping.sql
# Uso: .\ExecuteContextFieldsMapping.ps1 [-BusinessId "GUID"] [-ConnectionString "..."]

param(
    [Parameter(Mandatory=$false)]
    [string]$BusinessId,
    
    [Parameter(Mandatory=$false)]
    [string]$ConnectionString
)

$ErrorActionPreference = "Stop"

Write-Host "Ejecutando script InsertContextFieldsMapping.sql..." -ForegroundColor Cyan

# Obtener connection string desde appSettings.json si no se proporciona
if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    $appSettingsPath = Join-Path (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))) "src\Console\MimosBabySpa.Console\appSettings.json"
    
    if (Test-Path $appSettingsPath) {
        Write-Host "Leyendo connection string desde appSettings.json..." -ForegroundColor Yellow
        $appSettings = Get-Content $appSettingsPath | ConvertFrom-Json
        $ConnectionString = $appSettings.ConnectionStrings.DefaultConnection
    } else {
        Write-Host "Error: No se encontró appSettings.json y no se proporcionó ConnectionString" -ForegroundColor Red
        exit 1
    }
}

# Obtener BusinessId si no se proporciona
if ([string]::IsNullOrWhiteSpace($BusinessId)) {
    Write-Host "BusinessId no proporcionado. Buscando en la base de datos..." -ForegroundColor Yellow
    
    try {
        # Cargar System.Data.SqlClient desde .NET
        Add-Type -AssemblyName System.Data
        
        $connection = New-Object System.Data.SqlClient.SqlConnection($ConnectionString)
        $connection.Open()
        
        $query = "SELECT TOP 1 BusinessId FROM Businesses WHERE IsActive = 1 ORDER BY CreatedAt"
        $command = New-Object System.Data.SqlClient.SqlCommand($query, $connection)
        $reader = $command.ExecuteReader()
        
        if ($reader.Read()) {
            $BusinessId = $reader["BusinessId"].ToString()
            Write-Host "BusinessId encontrado: $BusinessId" -ForegroundColor Green
        } else {
            Write-Host "Error: No se encontró ningún negocio activo en la base de datos" -ForegroundColor Red
            $connection.Close()
            exit 1
        }
        
        $reader.Close()
        $connection.Close()
    } catch {
        Write-Host "Error al obtener BusinessId: $_" -ForegroundColor Red
        Write-Host "Por favor, proporciona el BusinessId manualmente: .\ExecuteContextFieldsMapping.ps1 -BusinessId 'TU-GUID-AQUI'" -ForegroundColor Yellow
        exit 1
    }
}

# Leer el script SQL
$scriptPath = Join-Path $PSScriptRoot "..\..\..\database\MimosBabySpa.Database\Scripts\InsertContextFieldsMapping.sql"
if (-not (Test-Path $scriptPath)) {
    Write-Host "Error: No se encontró el script InsertContextFieldsMapping.sql en $scriptPath" -ForegroundColor Red
    exit 1
}

$sqlScript = Get-Content $scriptPath -Raw

# Reemplazar @BusinessId con el valor real
$sqlScript = $sqlScript -replace '@BusinessId', "'$BusinessId'"

# Dividir por comandos GO (si existen)
$commands = $sqlScript -split '\bGO\b', [System.StringSplitOptions]::RemoveEmptyEntries

try {
    # Cargar System.Data.SqlClient
    Add-Type -AssemblyName System.Data
    
    $connection = New-Object System.Data.SqlClient.SqlConnection($ConnectionString)
    $connection.Open()
    Write-Host "Conexión establecida correctamente" -ForegroundColor Green
    
    $commandCount = 0
    foreach ($cmd in $commands) {
        $cmd = $cmd.Trim()
        if ([string]::IsNullOrWhiteSpace($cmd)) {
            continue
        }
        
        $commandCount++
        Write-Host "Ejecutando comando $commandCount de $($commands.Count)..." -ForegroundColor Yellow
        
        $sqlCommand = New-Object System.Data.SqlClient.SqlCommand($cmd, $connection)
        $sqlCommand.CommandTimeout = 30
        
        try {
            $sqlCommand.ExecuteNonQuery() | Out-Null
            Write-Host "Comando $commandCount ejecutado correctamente" -ForegroundColor Green
        } catch {
            Write-Host "Error en comando $commandCount : $_" -ForegroundColor Red
            $connection.Close()
            exit 1
        }
    }
    
    $connection.Close()
    Write-Host "`nScript ejecutado correctamente. ContextFieldsMapping configurado para BusinessId: $BusinessId" -ForegroundColor Green
    
} catch {
    Write-Host "Error al ejecutar script: $_" -ForegroundColor Red
    if ($connection.State -eq 'Open') {
        $connection.Close()
    }
    exit 1
}

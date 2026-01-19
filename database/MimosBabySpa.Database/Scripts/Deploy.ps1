# Script completo de despliegue
# Uso: .\Deploy.ps1 -ServerInstance "localhost" -DatabaseName "MimosBabySpa"

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
    [switch]$UseIntegratedSecurity = $true,
    
    [Parameter(Mandatory=$false)]
    [switch]$CreateDatabase = $true
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Despliegue de Base de Datos" -ForegroundColor Cyan
Write-Host "  Mimos Baby Spa" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path (Split-Path -Parent $scriptPath) "MimosBabySpa.Database.sqlproj"

# Paso 1: Crear base de datos si es necesario
if ($CreateDatabase) {
    Write-Host "[1/3] Creando base de datos..." -ForegroundColor Yellow
    & (Join-Path $scriptPath "CreateDatabase.ps1") `
        -ServerInstance $ServerInstance `
        -DatabaseName $DatabaseName `
        -Username $Username `
        -Password $Password `
        -UseIntegratedSecurity:$UseIntegratedSecurity
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Error al crear la base de datos" -ForegroundColor Red
        exit 1
    }
    Write-Host "✓ Base de datos lista" -ForegroundColor Green
    Write-Host ""
}

# Paso 2: Compilar proyecto
Write-Host "[2/3] Compilando proyecto..." -ForegroundColor Yellow
Push-Location (Split-Path -Parent $scriptPath)
try {
    # Intentar compilar con MSBuild si está disponible
    $msbuildPath = "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
    if (-not (Test-Path $msbuildPath)) {
        $msbuildPath = "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe"
    }
    if (-not (Test-Path $msbuildPath)) {
        $msbuildPath = "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
    }
    
    if (Test-Path $msbuildPath) {
        & $msbuildPath $projectPath /t:Build /p:Configuration=Debug /p:TargetDatabase=$DatabaseName
    } else {
        Write-Host "MSBuild no encontrado. Usando dotnet build..." -ForegroundColor Yellow
        dotnet build $projectPath --configuration Debug
    }
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Error al compilar el proyecto" -ForegroundColor Red
        exit 1
    }
} finally {
    Pop-Location
}
Write-Host "✓ Proyecto compilado" -ForegroundColor Green
Write-Host ""

# Paso 3: Publicar
Write-Host "[3/3] Publicando esquema..." -ForegroundColor Yellow
& (Join-Path $scriptPath "Publish.ps1") `
    -ServerInstance $ServerInstance `
    -DatabaseName $DatabaseName `
    -Username $Username `
    -Password $Password `
    -UseIntegratedSecurity:$UseIntegratedSecurity

if ($LASTEXITCODE -ne 0) {
    Write-Host "Error al publicar el esquema" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Despliegue completado exitosamente!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green

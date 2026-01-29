# Script para ejecutar los 20 escenarios de prueba de Function Calling
# Asegúrate de tener configurado appsettings.json con tus credenciales de OpenAI

Write-Host "================================================" -ForegroundColor Cyan
Write-Host "  EJECUTANDO 20 ESCENARIOS DE PRUEBA" -ForegroundColor Cyan
Write-Host "  Function Calling - Extracción de Información" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host ""

# Cambiar al directorio del proyecto
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptPath

# Verificar que existe appsettings.json
if (-not (Test-Path "appsettings.json")) {
    Write-Host "ERROR: appsettings.json no encontrado" -ForegroundColor Red
    Write-Host "Copia appsettings.json desde src/Console/MimosBabySpa.Console/" -ForegroundColor Yellow
    exit 1
}

# Ejecutar el programa
Write-Host "Ejecutando escenarios..." -ForegroundColor Green
Write-Host ""

dotnet run

Write-Host ""
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "  PRUEBAS COMPLETADAS" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan

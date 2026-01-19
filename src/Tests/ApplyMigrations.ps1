# Script para aplicar migraciones a la base de datos

Write-Host "=== Aplicando migraciones ===" -ForegroundColor Green

# Navegar al proyecto de Infrastructure
$infrastructurePath = Join-Path $PSScriptRoot "..\Infrastructure\MimosBabySpa.Infrastructure"
Set-Location $infrastructurePath

# Aplicar migraciones
Write-Host "Aplicando migraciones a la base de datos..." -ForegroundColor Yellow
dotnet ef database update --startup-project ..\..\API\MimosBabySpa.API\MimosBabySpa.API.csproj --context ApplicationDbContext

if ($LASTEXITCODE -eq 0) {
    Write-Host "Migraciones aplicadas exitosamente" -ForegroundColor Green
} else {
    Write-Host "Error al aplicar migraciones" -ForegroundColor Red
    exit 1
}

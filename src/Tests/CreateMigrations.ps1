# Script para crear y aplicar migraciones de EF Core

Write-Host "=== Creando migración inicial ===" -ForegroundColor Green

# Navegar al proyecto de Infrastructure
$infrastructurePath = Join-Path $PSScriptRoot "..\Infrastructure\MimosBabySpa.Infrastructure"
Set-Location $infrastructurePath

# Crear migración
Write-Host "Creando migración 'InitialCreate'..." -ForegroundColor Yellow
dotnet ef migrations add InitialCreate --startup-project ..\..\API\MimosBabySpa.API\MimosBabySpa.API.csproj --context ApplicationDbContext

if ($LASTEXITCODE -eq 0) {
    Write-Host "Migración creada exitosamente" -ForegroundColor Green
    
    Write-Host "`nPara aplicar la migración a la base de datos, ejecuta:" -ForegroundColor Cyan
    Write-Host "dotnet ef database update --startup-project ..\..\API\MimosBabySpa.API\MimosBabySpa.API.csproj --context ApplicationDbContext" -ForegroundColor White
} else {
    Write-Host "Error al crear la migración" -ForegroundColor Red
    exit 1
}

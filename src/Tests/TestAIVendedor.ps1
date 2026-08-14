# Script de validación básica del sistema IA Vendedor
# Ejecuta pruebas básicas para validar que el sistema funciona correctamente

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  VALIDACIÓN IA VENDEDOR" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 1. Compilar solución
Write-Host "[1/4] Compilando solución..." -ForegroundColor Yellow
$buildResult = dotnet build --no-restore 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Error en compilación" -ForegroundColor Red
    $buildResult | Select-Object -Last 10
    exit 1
}
Write-Host "✅ Compilación exitosa" -ForegroundColor Green
Write-Host ""

# 2. Ejecutar pruebas unitarias
Write-Host "[2/4] Ejecutando pruebas unitarias..." -ForegroundColor Yellow
$testResult = dotnet test src/Tests/Auraly.Platform.Tests/Auraly.Platform.Tests.csproj --verbosity minimal 2>&1
$testOutput = $testResult | Select-Object -Last 5
Write-Host $testOutput
if ($testOutput -match "Correctas!") {
    Write-Host "✅ Todas las pruebas pasan" -ForegroundColor Green
} else {
    Write-Host "⚠️  Revisar resultados de pruebas" -ForegroundColor Yellow
}
Write-Host ""

# 3. Verificar migraciones aplicadas
Write-Host "[3/4] Verificando migraciones..." -ForegroundColor Yellow
$migrations = dotnet ef migrations list --project src/Infrastructure/Auraly.Platform.Infrastructure/Auraly.Platform.Infrastructure.csproj --startup-project src/API/Auraly.Platform.Worker/Auraly.Platform.Worker.csproj 2>&1
if ($migrations -match "AddAIVendedorEntities" -and $migrations -match "RemoveBabySpecificFieldsFromCustomerProfile") {
    Write-Host "✅ Migraciones encontradas" -ForegroundColor Green
} else {
    Write-Host "⚠️  Verificar migraciones" -ForegroundColor Yellow
}
Write-Host ""

# 4. Verificar configuración
Write-Host "[4/4] Verificando configuración..." -ForegroundColor Yellow
if (Test-Path "src/API/Auraly.Platform.Worker/local.settings.json") {
    $settings = Get-Content "src/API/Auraly.Platform.Worker/local.settings.json" | ConvertFrom-Json
    if ($settings.Values.'Features:UseAIVendedor' -eq "true") {
        Write-Host "✅ Feature flag activado" -ForegroundColor Green
    } else {
        Write-Host "⚠️  Feature flag no configurado (usará default: true)" -ForegroundColor Yellow
    }
} else {
    Write-Host "⚠️  local.settings.json no encontrado" -ForegroundColor Yellow
}
Write-Host ""

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  VALIDACIÓN COMPLETA" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Próximos pasos:" -ForegroundColor Green
Write-Host "1. Ejecutar función localmente: cd src/API/Auraly.Platform.Worker && func start" -ForegroundColor White
Write-Host "2. Enviar mensaje de prueba por WhatsApp" -ForegroundColor White
Write-Host "3. Verificar logs del orquestador" -ForegroundColor White
Write-Host "4. Revisar tablas ConversationSessions y CustomerProfiles en BD" -ForegroundColor White

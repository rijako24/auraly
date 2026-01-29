#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Test básico de validación de la refactorización sin requerir infraestructura completa.
#>

Write-Host @"
╔════════════════════════════════════════════════════════════╗
║                                                            ║
║   ✅ TEST BÁSICO DE REFACTORIZACIÓN                       ║
║      Validación sin infraestructura completa              ║
║                                                            ║
╚════════════════════════════════════════════════════════════╝
"@ -ForegroundColor Cyan

$projectRoot = Split-Path -Parent $PSScriptRoot | Split-Path -Parent
$results = @{
    Passed = 0
    Failed = 0
    Tests = @()
}

function Test-Item {
    param(
        [string]$Name,
        [scriptblock]$Test
    )
    
    Write-Host "`n🔍 Test: " -NoNewline
    Write-Host $Name -ForegroundColor White
    
    try {
        $result = & $Test
        if ($result) {
            Write-Host "   ✅ PASSED" -ForegroundColor Green
            $results.Passed++
            $results.Tests += @{ Name = $Name; Status = "PASSED" }
            return $true
        } else {
            Write-Host "   ❌ FAILED" -ForegroundColor Red
            $results.Failed++
            $results.Tests += @{ Name = $Name; Status = "FAILED" }
            return $false
        }
    } catch {
        Write-Host "   ❌ ERROR: $_" -ForegroundColor Red
        $results.Failed++
        $results.Tests += @{ Name = $Name; Status = "ERROR"; Error = $_.Exception.Message }
        return $false
    }
}

Write-Host "`n" + ("─" * 60)
Write-Host "📋 VALIDANDO ESTRUCTURA DE LA REFACTORIZACIÓN"
Write-Host ("─" * 60)

# Test 1: Compilación exitosa
Test-Item "El proyecto compila sin errores" {
    Push-Location $projectRoot
    try {
        $output = dotnet build --verbosity quiet 2>&1
        $compiled = $LASTEXITCODE -eq 0
        if (-not $compiled) {
            Write-Host "   Salida: $output" -ForegroundColor Gray
        }
        return $compiled
    } finally {
        Pop-Location
    }
}

# Test 2: LoadedBusinessContext existe
Test-Item "LoadedBusinessContext existe" {
    $path = Join-Path $projectRoot "src\Application\MimosBabySpa.Application\Configuration\LoadedBusinessContext.cs"
    $exists = Test-Path $path
    if ($exists) {
        $content = Get-Content $path -Raw
        # Verificar que tiene el método LoadAsync
        return $content -match "public static async Task<LoadedBusinessContext> LoadAsync"
    }
    return $false
}

# Test 3: CachedBusinessContextProvider existe
Test-Item "CachedBusinessContextProvider existe" {
    $path = Join-Path $projectRoot "src\Application\MimosBabySpa.Application\Configuration\CachedBusinessContextProvider.cs"
    $exists = Test-Path $path
    if ($exists) {
        $content = Get-Content $path -Raw
        # Verificar que usa IMemoryCache
        return $content -match "IMemoryCache" -and $content -match "GetOrLoadAsync"
    }
    return $false
}

# Test 4: SystemPrompts existe
Test-Item "SystemPrompts (constantes estáticas) existen" {
    $path = Join-Path $projectRoot "src\Application\MimosBabySpa.Application\Prompts\SystemPrompts.cs"
    $exists = Test-Path $path
    if ($exists) {
        $content = Get-Content $path -Raw
        # Verificar que tiene las secciones principales
        return $content -match "public static class SystemPrompts" -and 
               $content -match "public static class Roles" -and
               $content -match "public static class ConversationRules"
    }
    return $false
}

# Test 5: SystemPromptProvider existe
Test-Item "SystemPromptProvider existe" {
    $path = Join-Path $projectRoot "src\Application\MimosBabySpa.Application\Prompts\SystemPromptProvider.cs"
    $exists = Test-Path $path
    if ($exists) {
        $content = Get-Content $path -Raw
        # Verificar que implementa IPromptProvider
        return $content -match "IPromptProvider" -and $content -match "BuildAsync"
    }
    return $false
}

# Test 6: HybridTransactionalOrchestrator refactorizado
Test-Item "HybridTransactionalOrchestrator usa CachedBusinessContextProvider" {
    $path = Join-Path $projectRoot "src\Application\MimosBabySpa.Application\Orchestration\HybridTransactionalOrchestrator.cs"
    $exists = Test-Path $path
    if ($exists) {
        $content = Get-Content $path -Raw
        # Verificar que usa el nuevo provider
        return $content -match "CachedBusinessContextProvider" -and 
               $content -match "GetOrLoadAsync"
    }
    return $false
}

# Test 7: JsonSchemaPromptBuilder refactorizado
Test-Item "JsonSchemaPromptBuilder usa LoadedBusinessContext" {
    $path = Join-Path $projectRoot "src\Application\MimosBabySpa.Application\LLM\Extraction\JsonSchemaPromptBuilder.cs"
    $exists = Test-Path $path
    if ($exists) {
        $content = Get-Content $path -Raw
        # Verificar que recibe LoadedBusinessContext
        return $content -match "LoadedBusinessContext"
    }
    return $false
}

# Test 8: SmartExtractionService refactorizado
Test-Item "SmartExtractionService usa LoadedBusinessContext" {
    $path = Join-Path $projectRoot "src\Application\MimosBabySpa.Application\LLM\Extraction\SmartExtractionService.cs"
    $exists = Test-Path $path
    if ($exists) {
        $content = Get-Content $path -Raw
        # Verificar que recibe LoadedBusinessContext
        return $content -match "LoadedBusinessContext"
    }
    return $false
}

# Test 9: ProcessingContext actualizado
Test-Item "ProcessingContext incluye BusinessContext" {
    $path = Join-Path $projectRoot "src\Application\MimosBabySpa.Application\Orchestration\ProcessingContext.cs"
    $exists = Test-Path $path
    if ($exists) {
        $content = Get-Content $path -Raw
        # Verificar que tiene propiedad BusinessContext
        return $content -match "public LoadedBusinessContext BusinessContext"
    }
    return $false
}

# Test 10: Program.cs tiene registro de servicios
Test-Item "Program.cs registra CachedBusinessContextProvider" {
    $path = Join-Path $projectRoot "src\API\MimosBabySpa.API\Program.cs"
    $exists = Test-Path $path
    if ($exists) {
        $content = Get-Content $path -Raw
        # Verificar que registra los nuevos servicios
        return $content -match "CachedBusinessContextProvider" -and 
               $content -match "IPromptProvider" -and
               $content -match "AddMemoryCache"
    }
    return $false
}

# Test 11: Microsoft.Extensions.Caching.Memory agregado
Test-Item "Paquete Microsoft.Extensions.Caching.Memory agregado" {
    $path = Join-Path $projectRoot "src\Application\MimosBabySpa.Application\MimosBabySpa.Application.csproj"
    $exists = Test-Path $path
    if ($exists) {
        $content = Get-Content $path -Raw
        return $content -match "Microsoft.Extensions.Caching.Memory"
    }
    return $false
}

# Test 12: Sin repositorio duplicado
Test-Item "No hay IBusinessConfigurationRepository duplicado" {
    $path1 = Join-Path $projectRoot "src\Application\MimosBabySpa.Application\Configuration\IBusinessConfigurationRepository.cs"
    $path2 = Join-Path $projectRoot "src\Application\MimosBabySpa.Application\Configuration\BusinessConfigurationRepository.cs"
    
    # Estos archivos NO deberían existir (fueron eliminados)
    $notExists1 = -not (Test-Path $path1)
    $notExists2 = -not (Test-Path $path2)
    
    return $notExists1 -and $notExists2
}

# Mostrar resumen
Write-Host "`n" + ("─" * 60)
Write-Host "📊 RESUMEN DE RESULTADOS"
Write-Host ("─" * 60)

$total = $results.Passed + $results.Failed
$percentage = if ($total -gt 0) { [math]::Round(($results.Passed / $total) * 100, 2) } else { 0 }

Write-Host "`nTests ejecutados:   $total"
Write-Host "✅ Pasados:         " -NoNewline
Write-Host $results.Passed -ForegroundColor Green
Write-Host "❌ Fallados:        " -NoNewline
if ($results.Failed -gt 0) {
    Write-Host $results.Failed -ForegroundColor Red
} else {
    Write-Host $results.Failed -ForegroundColor Green
}
Write-Host "📈 Porcentaje:      " -NoNewline
if ($percentage -ge 90) {
    Write-Host "$percentage%" -ForegroundColor Green
} elseif ($percentage -ge 70) {
    Write-Host "$percentage%" -ForegroundColor Yellow
} else {
    Write-Host "$percentage%" -ForegroundColor Red
}

Write-Host "`n💡 INTERPRETACIÓN:"
if ($percentage -eq 100) {
    Write-Host "   ✅ ¡PERFECTO! Refactorización completada exitosamente." -ForegroundColor Green
    Write-Host "   ✅ Todos los componentes están implementados correctamente." -ForegroundColor Green
    Write-Host "   ✅ El código compila sin errores." -ForegroundColor Green
    Write-Host "`n   🚀 Siguiente paso: Ejecutar tests de integración con infraestructura completa"
    Write-Host "      .\IniciarTesting.ps1 -RunTests -AnalyzeLogs"
} elseif ($percentage -ge 90) {
    Write-Host "   ✅ Muy bien! La refactorización está casi completa." -ForegroundColor Green
    Write-Host "   ⚠️ Revisar tests fallidos arriba." -ForegroundColor Yellow
} elseif ($percentage -ge 70) {
    Write-Host "   ⚠️ Progreso aceptable, pero hay problemas." -ForegroundColor Yellow
    Write-Host "   ❌ Revisar tests fallidos y corregir." -ForegroundColor Yellow
} else {
    Write-Host "   ❌ Refactorización incompleta o con errores." -ForegroundColor Red
    Write-Host "   ❌ Revisar implementación y corregir errores." -ForegroundColor Red
}

Write-Host "`n" + ("─" * 60) + "`n"

# Exit code
if ($results.Failed -eq 0) {
    exit 0
} else {
    exit 1
}

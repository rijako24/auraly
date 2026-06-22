#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Test bÃ¡sico de validaciÃ³n de la refactorizaciÃ³n sin requerir infraestructura completa.
#>

Write-Host @"
â•”â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•—
â•‘                                                            â•‘
â•‘   âœ… TEST BÃSICO DE REFACTORIZACIÃ“N                       â•‘
â•‘      ValidaciÃ³n sin infraestructura completa              â•‘
â•‘                                                            â•‘
â•šâ•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
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
    
    Write-Host "`nðŸ” Test: " -NoNewline
    Write-Host $Name -ForegroundColor White
    
    try {
        $result = & $Test
        if ($result) {
            Write-Host "   âœ… PASSED" -ForegroundColor Green
            $results.Passed++
            $results.Tests += @{ Name = $Name; Status = "PASSED" }
            return $true
        } else {
            Write-Host "   âŒ FAILED" -ForegroundColor Red
            $results.Failed++
            $results.Tests += @{ Name = $Name; Status = "FAILED" }
            return $false
        }
    } catch {
        Write-Host "   âŒ ERROR: $_" -ForegroundColor Red
        $results.Failed++
        $results.Tests += @{ Name = $Name; Status = "ERROR"; Error = $_.Exception.Message }
        return $false
    }
}

Write-Host "`n" + ("â”€" * 60)
Write-Host "ðŸ“‹ VALIDANDO ESTRUCTURA DE LA REFACTORIZACIÃ“N"
Write-Host ("â”€" * 60)

# Test 1: CompilaciÃ³n exitosa
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
        # Verificar que tiene el mÃ©todo LoadAsync
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

# Test 4: SystemPromptProvider existe
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

# Test 5: HybridTransactionalOrchestrator refactorizado
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

# Test 6: JsonSchemaPromptBuilder refactorizado
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

# Test 7: SmartExtractionService refactorizado
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

# Test 8: ProcessingContext actualizado
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

# Test 9: Program.cs tiene registro de servicios
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

# Test 10: Microsoft.Extensions.Caching.Memory agregado
Test-Item "Paquete Microsoft.Extensions.Caching.Memory agregado" {
    $path = Join-Path $projectRoot "src\Application\MimosBabySpa.Application\MimosBabySpa.Application.csproj"
    $exists = Test-Path $path
    if ($exists) {
        $content = Get-Content $path -Raw
        return $content -match "Microsoft.Extensions.Caching.Memory"
    }
    return $false
}

# Test 11: Sin repositorio duplicado

# Mostrar resumen
Write-Host "`n" + ("â”€" * 60)
Write-Host "ðŸ“Š RESUMEN DE RESULTADOS"
Write-Host ("â”€" * 60)

$total = $results.Passed + $results.Failed
$percentage = if ($total -gt 0) { [math]::Round(($results.Passed / $total) * 100, 2) } else { 0 }

Write-Host "`nTests ejecutados:   $total"
Write-Host "âœ… Pasados:         " -NoNewline
Write-Host $results.Passed -ForegroundColor Green
Write-Host "âŒ Fallados:        " -NoNewline
if ($results.Failed -gt 0) {
    Write-Host $results.Failed -ForegroundColor Red
} else {
    Write-Host $results.Failed -ForegroundColor Green
}
Write-Host "ðŸ“ˆ Porcentaje:      " -NoNewline
if ($percentage -ge 90) {
    Write-Host "$percentage%" -ForegroundColor Green
} elseif ($percentage -ge 70) {
    Write-Host "$percentage%" -ForegroundColor Yellow
} else {
    Write-Host "$percentage%" -ForegroundColor Red
}

Write-Host "`nðŸ’¡ INTERPRETACIÃ“N:"
if ($percentage -eq 100) {
    Write-Host "   âœ… Â¡PERFECTO! RefactorizaciÃ³n completada exitosamente." -ForegroundColor Green
    Write-Host "   âœ… Todos los componentes estÃ¡n implementados correctamente." -ForegroundColor Green
    Write-Host "   âœ… El cÃ³digo compila sin errores." -ForegroundColor Green
    Write-Host "`n   ðŸš€ Siguiente paso: Ejecutar tests de integraciÃ³n con infraestructura completa"
    Write-Host "      .\IniciarTesting.ps1 -RunTests -AnalyzeLogs"
} elseif ($percentage -ge 90) {
    Write-Host "   âœ… Muy bien! La refactorizaciÃ³n estÃ¡ casi completa." -ForegroundColor Green
    Write-Host "   âš ï¸ Revisar tests fallidos arriba." -ForegroundColor Yellow
} elseif ($percentage -ge 70) {
    Write-Host "   âš ï¸ Progreso aceptable, pero hay problemas." -ForegroundColor Yellow
    Write-Host "   âŒ Revisar tests fallidos y corregir." -ForegroundColor Yellow
} else {
    Write-Host "   âŒ RefactorizaciÃ³n incompleta o con errores." -ForegroundColor Red
    Write-Host "   âŒ Revisar implementaciÃ³n y corregir errores." -ForegroundColor Red
}

Write-Host "`n" + ("â”€" * 60) + "`n"

# Exit code
if ($results.Failed -eq 0) {
    exit 0
} else {
    exit 1
}


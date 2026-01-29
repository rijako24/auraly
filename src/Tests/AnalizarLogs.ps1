#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Analiza los logs de Azure Functions para validar la refactorización.

.DESCRIPTION
    Este script analiza los logs generados por 'func start' para:
    1. Contar cache hits y misses
    2. Medir tiempos de carga
    3. Verificar cargas únicas de configuración
    4. Generar reporte de performance

.PARAMETER LogFile
    Ruta al archivo de logs (default: busca en directorio actual)

.PARAMETER Watch
    Si se especifica, monitorea el archivo de logs en tiempo real

.EXAMPLE
    # Analizar archivo de logs existente
    .\AnalizarLogs.ps1 -LogFile "logs.txt"

.EXAMPLE
    # Monitorear logs en tiempo real
    Get-Content -Wait -Tail 50 logs.txt | .\AnalizarLogs.ps1 -Watch
#>

param(
    [Parameter()]
    [string]$LogFile = "logs.txt",
    
    [Parameter()]
    [switch]$Watch
)

function Show-Banner {
    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host "📊 ANÁLISIS DE LOGS - REFACTORIZACIÓN" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
}

function Analyze-Logs {
    param([string[]]$LogLines)
    
    if (-not $LogLines -or $LogLines.Count -eq 0) {
        Write-Warning "No se encontraron líneas en el log"
        return
    }
    
    # Estadísticas
    $stats = @{
        CacheHits = 0
        CacheMisses = 0
        ConfigLoads = 0
        ContextLoadTimes = @()
        TotalRequests = 0
    }
    
    # Analizar cada línea
    foreach ($line in $LogLines) {
        # Cache hits
        if ($line -match "✅ BusinessContext servido desde caché") {
            $stats.CacheHits++
        }
        
        # Cache misses
        if ($line -match "⚠️ BusinessContext no en caché") {
            $stats.CacheMisses++
        }
        
        # Cargas de configuración
        if ($line -match "Configuración cargada para BusinessId=.+ en (\d+)ms") {
            $stats.ConfigLoads++
            $loadTime = [int]$Matches[1]
            $stats.ContextLoadTimes += $loadTime
        }
        
        # Context loaded times
        if ($line -match "✅ Contexto cargado en (\d+)ms") {
            $stats.TotalRequests++
            $contextTime = [int]$Matches[1]
            $stats.ContextLoadTimes += $contextTime
        }
    }
    
    # Calcular métricas
    $totalCacheOps = $stats.CacheHits + $stats.CacheMisses
    $hitRate = if ($totalCacheOps -gt 0) { 
        [math]::Round(($stats.CacheHits / $totalCacheOps) * 100, 2)
    } else { 
        0 
    }
    
    $avgLoadTime = if ($stats.ContextLoadTimes.Count -gt 0) {
        [math]::Round(($stats.ContextLoadTimes | Measure-Object -Average).Average, 2)
    } else {
        0
    }
    
    $minLoadTime = if ($stats.ContextLoadTimes.Count -gt 0) {
        ($stats.ContextLoadTimes | Measure-Object -Minimum).Minimum
    } else {
        0
    }
    
    $maxLoadTime = if ($stats.ContextLoadTimes.Count -gt 0) {
        ($stats.ContextLoadTimes | Measure-Object -Maximum).Maximum
    } else {
        0
    }
    
    # Mostrar resultados
    Show-Banner
    
    Write-Host "`n📈 ESTADÍSTICAS DE CACHÉ"
    Write-Host "────────────────────────────────────────"
    
    if ($totalCacheOps -gt 0) {
        Write-Host "Cache Hits:       " -NoNewline
        Write-Host $stats.CacheHits -ForegroundColor Green
        
        Write-Host "Cache Misses:     " -NoNewline
        Write-Host $stats.CacheMisses -ForegroundColor $(if ($stats.CacheMisses -lt 5) { "Green" } else { "Yellow" })
        
        Write-Host "Total Operaciones: $totalCacheOps"
        
        Write-Host "`nCache Hit Rate:   " -NoNewline
        Write-Host "$hitRate%" -ForegroundColor $(
            if ($hitRate -ge 80) { "Green" }
            elseif ($hitRate -ge 50) { "Yellow" }
            else { "Red" }
        )
        
        # Interpretación
        Write-Host "`n💡 Interpretación:"
        if ($hitRate -ge 80) {
            Write-Host "   ✅ Excelente - El caché funciona perfectamente" -ForegroundColor Green
        }
        elseif ($hitRate -ge 50) {
            Write-Host "   ⚠️ Aceptable - Considerar aumentar tiempo de expiración" -ForegroundColor Yellow
        }
        else {
            Write-Host "   ❌ Bajo - Revisar configuración de caché" -ForegroundColor Red
        }
    }
    else {
        Write-Host "⚠️ No se encontraron operaciones de caché en los logs" -ForegroundColor Yellow
    }
    
    Write-Host "`n⏱️ TIEMPOS DE CARGA"
    Write-Host "────────────────────────────────────────"
    
    if ($stats.ContextLoadTimes.Count -gt 0) {
        Write-Host "Total de cargas:  $($stats.ContextLoadTimes.Count)"
        Write-Host "Tiempo promedio:  $($avgLoadTime)ms"
        Write-Host "Tiempo mínimo:    " -NoNewline
        Write-Host "$($minLoadTime)ms" -ForegroundColor Green
        Write-Host "Tiempo máximo:    " -NoNewline
        Write-Host "$($maxLoadTime)ms" -ForegroundColor $(if ($maxLoadTime -lt 100) { "Green" } else { "Yellow" })
        
        Write-Host "`n💡 Análisis:"
        if ($minLoadTime -le 5) {
            Write-Host "   ✅ Caché funcionando - tiempo mínimo óptimo (<5ms)" -ForegroundColor Green
        }
        if ($maxLoadTime -le 100) {
            Write-Host "   ✅ Carga inicial eficiente (<100ms)" -ForegroundColor Green
        }
        elseif ($maxLoadTime -le 200) {
            Write-Host "   ⚠️ Carga inicial aceptable (100-200ms)" -ForegroundColor Yellow
        }
        else {
            Write-Host "   ❌ Carga inicial lenta (>200ms) - revisar BD" -ForegroundColor Red
        }
    }
    else {
        Write-Host "⚠️ No se encontraron tiempos de carga en los logs" -ForegroundColor Yellow
    }
    
    Write-Host "`n🔄 CARGAS DE CONFIGURACIÓN"
    Write-Host "────────────────────────────────────────"
    Write-Host "Total de cargas:  $($stats.ConfigLoads)"
    
    if ($stats.ConfigLoads -eq 0) {
        Write-Host "⚠️ No se encontraron cargas de configuración" -ForegroundColor Yellow
    }
    elseif ($stats.ConfigLoads -eq $stats.CacheMisses) {
        Write-Host "✅ Correcto - 1 carga por cache miss" -ForegroundColor Green
    }
    else {
        Write-Host "⚠️ Revisar - cantidad de cargas no coincide con cache misses" -ForegroundColor Yellow
    }
    
    # Comparación ANTES vs DESPUÉS
    Write-Host "`n📊 COMPARACIÓN DE PERFORMANCE"
    Write-Host "────────────────────────────────────────"
    
    $beforeAvg = 150  # Tiempo promedio antes de la refactorización
    if ($avgLoadTime -gt 0) {
        $improvement = [math]::Round((($beforeAvg - $avgLoadTime) / $beforeAvg) * 100, 2)
        
        Write-Host "Tiempo ANTES:     ~$($beforeAvg)ms (promedio)"
        Write-Host "Tiempo DESPUÉS:   $($avgLoadTime)ms (promedio)"
        Write-Host "Mejora:           " -NoNewline
        
        if ($improvement -gt 0) {
            Write-Host "$improvement%" -ForegroundColor Green
            Write-Host "   ✅ Performance mejorada significativamente" -ForegroundColor Green
        }
        else {
            Write-Host "Sin mejora detectada" -ForegroundColor Red
            Write-Host "   ❌ Revisar implementación" -ForegroundColor Red
        }
    }
    
    Write-Host "`n📋 RECOMENDACIONES"
    Write-Host "────────────────────────────────────────"
    
    $recommendations = @()
    
    if ($hitRate -lt 50) {
        $recommendations += "⚠️ Aumentar tiempo de expiración del caché (actualmente 30 min)"
    }
    
    if ($maxLoadTime -gt 200) {
        $recommendations += "⚠️ Optimizar queries a base de datos"
    }
    
    if ($stats.ConfigLoads -gt ($stats.CacheMisses * 2)) {
        $recommendations += "❌ CRÍTICO: Configuración se está cargando múltiples veces - revisar implementación"
    }
    
    if ($recommendations.Count -eq 0) {
        Write-Host "✅ Todo funciona correctamente - sin recomendaciones" -ForegroundColor Green
    }
    else {
        foreach ($rec in $recommendations) {
            Write-Host $rec
        }
    }
    
    Write-Host "`n========================================`n" -ForegroundColor Cyan
}

# Main
if ($Watch) {
    Write-Host "👁️ Modo monitoreo activado - presiona Ctrl+C para salir`n" -ForegroundColor Yellow
    
    $buffer = @()
    while ($true) {
        $line = Read-Host
        if ($line) {
            $buffer += $line
            
            # Analizar cada 10 líneas
            if ($buffer.Count -ge 10) {
                Clear-Host
                Analyze-Logs -LogLines $buffer
                $buffer = @()
            }
        }
    }
}
else {
    if (-not (Test-Path $LogFile)) {
        Write-Error "Archivo de logs no encontrado: $LogFile"
        Write-Host "`nPara generar logs, ejecuta:`n"
        Write-Host "  cd src\API\MimosBabySpa.API"
        Write-Host "  func start --verbose > logs.txt`n"
        exit 1
    }
    
    $logs = Get-Content $LogFile
    Analyze-Logs -LogLines $logs
}

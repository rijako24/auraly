#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Script de testing para validar la refactorización de carga de configuración.

.DESCRIPTION
    Este script simula una conversación completa de WhatsApp para:
    1. Verificar que la carga de configuración funciona (1 sola vez)
    2. Validar que el caché funciona correctamente (cache hits)
    3. Medir tiempos de respuesta
    4. Verificar que el flujo completo funciona

.PARAMETER BaseUrl
    URL base del webhook de WhatsApp (default: http://localhost:7071/api/WhatsAppWebhook)

.PARAMETER Phone
    Número de teléfono simulado (default: 521234567890)

.PARAMETER DelaySeconds
    Segundos de espera entre mensajes (default: 2)

.EXAMPLE
    .\TestConversacionCompleta.ps1
    
.EXAMPLE
    .\TestConversacionCompleta.ps1 -BaseUrl "https://mi-function.azurewebsites.net/api/WhatsAppWebhook" -Phone "523331234567"
#>

param(
    [Parameter()]
    [string]$BaseUrl = "http://localhost:7071/api/WhatsAppWebhook",
    
    [Parameter()]
    [string]$Phone = "521234567890",
    
    [Parameter()]
    [int]$DelaySeconds = 2
)

# Variables globales para estadísticas
$script:TotalRequests = 0
$script:SuccessfulRequests = 0
$script:FailedRequests = 0
$script:TotalTime = 0

function Send-Message {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Message,
        
        [Parameter()]
        [string]$Step = ""
    )
    
    $script:TotalRequests++
    
    Write-Host "`n----------------------------------------" -ForegroundColor Gray
    Write-Host "📱 Paso $Step" -ForegroundColor Cyan
    Write-Host "📤 Enviando: " -NoNewline
    Write-Host "$Message" -ForegroundColor White
    
    $body = @{
        entry = @(
            @{
                changes = @(
                    @{
                        value = @{
                            messages = @(
                                @{
                                    from = $Phone
                                    text = @{ body = $Message }
                                    type = "text"
                                    timestamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
                                }
                            )
                        }
                    }
                )
            }
        )
    } | ConvertTo-Json -Depth 10
    
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    
    try {
        $response = Invoke-RestMethod -Uri $BaseUrl `
            -Method POST `
            -Body $body `
            -ContentType "application/json" `
            -TimeoutSec 30
        
        $stopwatch.Stop()
        $elapsed = $stopwatch.ElapsedMilliseconds
        $script:TotalTime += $elapsed
        $script:SuccessfulRequests++
        
        Write-Host "✅ Respuesta recibida en " -NoNewline -ForegroundColor Green
        Write-Host "$($elapsed)ms" -ForegroundColor Yellow
        
        # Mostrar respuesta si existe
        if ($response.response) {
            Write-Host "💬 Bot: " -NoNewline -ForegroundColor Magenta
            Write-Host "$($response.response)" -ForegroundColor White
        }
        
        Start-Sleep -Seconds $DelaySeconds
        
        return $true
    }
    catch {
        $stopwatch.Stop()
        $elapsed = $stopwatch.ElapsedMilliseconds
        $script:FailedRequests++
        
        Write-Host "❌ Error ($($elapsed)ms): " -NoNewline -ForegroundColor Red
        Write-Host "$($_.Exception.Message)" -ForegroundColor Red
        
        return $false
    }
}

function Show-Summary {
    Write-Host "`n========================================" -ForegroundColor Green
    Write-Host "📊 RESUMEN DE TESTING" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    
    Write-Host "`n🔢 Estadísticas de Requests:"
    Write-Host "   Total enviados:    $script:TotalRequests"
    Write-Host "   Exitosos:          " -NoNewline
    Write-Host "$script:SuccessfulRequests" -ForegroundColor Green
    Write-Host "   Fallidos:          " -NoNewline
    if ($script:FailedRequests -gt 0) {
        Write-Host "$script:FailedRequests" -ForegroundColor Red
    } else {
        Write-Host "$script:FailedRequests" -ForegroundColor Green
    }
    
    if ($script:SuccessfulRequests -gt 0) {
        $avgTime = [math]::Round($script:TotalTime / $script:SuccessfulRequests, 2)
        Write-Host "   Tiempo promedio:   $($avgTime)ms"
        Write-Host "   Tiempo total:      $($script:TotalTime)ms"
    }
    
    Write-Host "`n⚡ Performance Esperado:"
    Write-Host "   1er mensaje:       ~50ms (sin caché)"
    Write-Host "   Mensajes 2-9:      ~2-5ms (con caché)"
    Write-Host "   Cache hit rate:    >80% es excelente"
    
    Write-Host "`n🔍 Siguiente Paso:"
    Write-Host "   1. Revisa los logs de 'func start'"
    Write-Host "   2. Busca: '✅ BusinessContext servido desde caché'"
    Write-Host "   3. Verifica que solo hay 1 'Configuración cargada' para este BusinessId"
    Write-Host "   4. Compara tiempos: 1er request (~50ms) vs siguientes (~2ms)"
    
    if ($script:FailedRequests -eq 0) {
        Write-Host "`n✅ " -NoNewline -ForegroundColor Green
        Write-Host "Test completado exitosamente!" -ForegroundColor Green
    } else {
        Write-Host "`n⚠️ " -NoNewline -ForegroundColor Yellow
        Write-Host "Test completado con errores. Revisa los logs arriba." -ForegroundColor Yellow
    }
    
    Write-Host "`n========================================`n" -ForegroundColor Green
}

# Inicio del script
Clear-Host
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "🧪 TEST DE REFACTORIZACIÓN" -ForegroundColor Cyan
Write-Host "   Carga de Configuración Optimizada" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

Write-Host "`n🎯 Objetivo del Test:"
Write-Host "   1. Verificar carga única de configuración"
Write-Host "   2. Validar funcionamiento del caché"
Write-Host "   3. Medir mejora de performance"

Write-Host "`n🔧 Configuración:"
Write-Host "   URL:     $BaseUrl"
Write-Host "   Phone:   $Phone"
Write-Host "   Delay:   $DelaySeconds segundos entre mensajes"

Write-Host "`n⏳ Iniciando conversación simulada...`n"
Start-Sleep -Seconds 2

# Conversación simulada
$success = $true

$success = Send-Message -Message "Hola" -Step "1/9"
if (-not $success) { Write-Warning "Primer mensaje falló. Continuando..." }

$success = Send-Message -Message "¿Qué planes tienen disponibles?" -Step "2/9"
$success = Send-Message -Message "Mi bebé tiene 4 meses" -Step "3/9"
$success = Send-Message -Message "Se llama Mateo" -Step "4/9"
$success = Send-Message -Message "Me interesa el Plan Marineritos" -Step "5/9"
$success = Send-Message -Message "Para mañana" -Step "6/9"
$success = Send-Message -Message "A las 3pm" -Step "7/9"
$success = Send-Message -Message "Mi nombre es María González" -Step "8/9"
$success = Send-Message -Message "Sí, confirmo la reserva" -Step "9/9"

# Mostrar resumen
Show-Summary

# Exit code
if ($script:FailedRequests -eq 0) {
    exit 0
} else {
    exit 1
}

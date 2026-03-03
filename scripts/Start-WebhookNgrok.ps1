#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Inicia ngrok para exponer el webhook de WhatsApp y muestra la URL y token para Meta.

.DESCRIPTION
    1. Asegúrate de tener la API corriendo en otra terminal: cd src\API\MimosBabySpa.API; func start
    2. Este script inicia ngrok en el puerto 7071
    3. Muestra la URL del Callback y el Verify Token para configurar en Meta for Developers

.PARAMETER Force
    Omite la verificación de que la API esté corriendo (útil cuando se ejecuta antes de iniciar la API).

.EXAMPLE
    .\Start-WebhookNgrok.ps1

.EXAMPLE
    .\Start-WebhookNgrok.ps1 -Force
#>

param(
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$ApiPort = 7071
$VerifyToken = "mimos-meta-verify-2024"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Webhook Meta + ngrok - Mimos Baby Spa" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Verificar que la API esté corriendo
try {
    $null = Invoke-WebRequest -Uri "http://localhost:$ApiPort" -UseBasicParsing -TimeoutSec 2
} catch {
    if (-not $Force) {
        Write-Host "⚠  La API no responde en localhost:$ApiPort" -ForegroundColor Yellow
        Write-Host "   Ejecuta en otra terminal:" -ForegroundColor Gray
        Write-Host "   cd src\API\MimosBabySpa.API" -ForegroundColor White
        Write-Host "   func start" -ForegroundColor White
        Write-Host ""
        $continue = Read-Host "¿Continuar con ngrok de todos modos? (s/n)"
        if ($continue -ne "s" -and $continue -ne "S") { exit 1 }
    } else {
        Write-Host "⚠  API no detectada (modo -Force). Asegúrate de iniciarla antes de verificar en Meta." -ForegroundColor Yellow
    }
}

# Refrescar PATH por si ngrok se instaló recientemente
$env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")

# Verificar ngrok
if (-not (Get-Command ngrok -ErrorAction SilentlyContinue)) {
    Write-Host "✗ ngrok no encontrado. Instálalo con:" -ForegroundColor Red
    Write-Host "  winget install ngrok.ngrok" -ForegroundColor White
    exit 1
}

# Iniciar ngrok en nueva ventana
Write-Host "Iniciando ngrok en el puerto $ApiPort..." -ForegroundColor Gray
Start-Process ngrok -ArgumentList "http", $ApiPort -WindowStyle Normal

Start-Sleep -Seconds 5

# Obtener la URL pública de ngrok
try {
    $tunnels = Invoke-RestMethod -Uri "http://127.0.0.1:4040/api/tunnels" -TimeoutSec 5
    $publicUrl = $tunnels.tunnels | Where-Object { $_.proto -eq "https" } | Select-Object -First 1 -ExpandProperty public_url
} catch {
    Write-Host "✗ No se pudo obtener la URL de ngrok. ¿Está ngrok corriendo?" -ForegroundColor Red
    exit 1
}

if (-not $publicUrl) {
    Write-Host "✗ No se encontró túnel HTTPS de ngrok. Espera unos segundos y ejecuta el script de nuevo." -ForegroundColor Red
    exit 1
}

$callbackUrl = "$publicUrl/api/WhatsAppWebhook"

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Copia estos valores en Meta for Developers" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Callback URL:" -ForegroundColor Cyan
Write-Host "  $callbackUrl" -ForegroundColor White
Write-Host ""
Write-Host "  Verify Token:" -ForegroundColor Cyan
Write-Host "  $VerifyToken" -ForegroundColor White
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Pasos en Meta:" -ForegroundColor Gray
Write-Host "  1. Ve a developers.facebook.com -> Tu app -> WhatsApp -> Configuración" -ForegroundColor Gray
Write-Host "  2. Configurar webhooks -> Callback URL: (pega la URL de arriba)" -ForegroundColor Gray
Write-Host "  3. Verify Token: (pega el token de arriba)" -ForegroundColor Gray
Write-Host "  4. Verificar y guardar" -ForegroundColor Gray
Write-Host "  5. Suscríbete a 'messages'" -ForegroundColor Gray
Write-Host ""
Write-Host "ngrok está corriendo en una ventana separada. Ciérrala para detenerlo." -ForegroundColor Yellow
Write-Host ""

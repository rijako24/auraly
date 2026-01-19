#Requires -Version 7.0

<#
.SYNOPSIS
    Verifica los requisitos previos antes de ejecutar el despliegue

.DESCRIPTION
    Este script verifica que todos los requisitos estén instalados y configurados
    antes de ejecutar el despliegue de infraestructura.

.EXAMPLE
    .\Test-Prerequisites.ps1
#>

$ErrorActionPreference = "Continue"
$allChecksPassed = $true

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Verificación de Requisitos Previos" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Verificar PowerShell version
Write-Host "[1/6] Verificando versión de PowerShell..." -ForegroundColor Yellow
$psVersion = $PSVersionTable.PSVersion
if ($psVersion.Major -ge 7) {
    Write-Host "  ✓ PowerShell $($psVersion.Major).$($psVersion.Minor) instalado" -ForegroundColor Green
} else {
    Write-Host "  ✗ Se requiere PowerShell 7.0 o superior. Versión actual: $($psVersion.Major).$($psVersion.Minor)" -ForegroundColor Red
    $allChecksPassed = $false
}
Write-Host ""

# Verificar módulos de Azure PowerShell
Write-Host "[2/6] Verificando módulos de Azure PowerShell..." -ForegroundColor Yellow
$requiredModules = @(
    "Az.Accounts",
    "Az.Resources",
    "Az.Storage",
    "Az.Sql",
    "Az.Functions",
    "Az.CognitiveServices"
)

$missingModules = @()
foreach ($module in $requiredModules) {
    $installed = Get-Module -ListAvailable -Name $module
    if ($null -eq $installed) {
        $missingModules += $module
        Write-Host "  ✗ Módulo faltante: $module" -ForegroundColor Red
    } else {
        Write-Host "  ✓ Módulo instalado: $module (v$($installed.Version))" -ForegroundColor Green
    }
}

if ($missingModules.Count -gt 0) {
    Write-Host ""
    Write-Host "  Para instalar los módulos faltantes, ejecuta:" -ForegroundColor Yellow
    Write-Host "  Install-Module -Name Az -AllowClobber -Scope CurrentUser" -ForegroundColor Gray
    $allChecksPassed = $false
}
Write-Host ""

# Verificar conexión a Azure
Write-Host "[3/6] Verificando conexión a Azure..." -ForegroundColor Yellow
try {
    $context = Get-AzContext -ErrorAction Stop
    if ($null -ne $context) {
        Write-Host "  ✓ Conectado a Azure como: $($context.Account.Id)" -ForegroundColor Green
        Write-Host "  ✓ Suscripción: $($context.Subscription.Name) ($($context.Subscription.Id))" -ForegroundColor Green
    } else {
        Write-Host "  ✗ No hay sesión activa de Azure" -ForegroundColor Red
        Write-Host "  Ejecuta: Connect-AzAccount" -ForegroundColor Yellow
        $allChecksPassed = $false
    }
} catch {
    Write-Host "  ✗ Error al verificar conexión: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "  Ejecuta: Connect-AzAccount" -ForegroundColor Yellow
    $allChecksPassed = $false
}
Write-Host ""

# Verificar permisos
Write-Host "[4/6] Verificando permisos en Azure..." -ForegroundColor Yellow
try {
    $subscription = Get-AzSubscription -ErrorAction Stop
    if ($null -ne $subscription) {
        Write-Host "  ✓ Tienes acceso a la suscripción" -ForegroundColor Green
        
        # Intentar verificar permisos básicos
        try {
            $testRG = Get-AzResourceGroup -ErrorAction Stop | Select-Object -First 1
            Write-Host "  ✓ Permisos de lectura verificados" -ForegroundColor Green
        } catch {
            Write-Host "  ⚠ No se pudieron verificar permisos completos" -ForegroundColor Yellow
        }
    }
} catch {
    Write-Host "  ✗ Error al verificar permisos: $($_.Exception.Message)" -ForegroundColor Red
    $allChecksPassed = $false
}
Write-Host ""

# Verificar Azure CLI (opcional pero recomendado)
Write-Host "[5/6] Verificando Azure CLI..." -ForegroundColor Yellow
try {
    $azVersion = az version --output json 2>$null | ConvertFrom-Json
    if ($null -ne $azVersion) {
        Write-Host "  ✓ Azure CLI instalado: v$($azVersion.'azure-cli')" -ForegroundColor Green
    } else {
        Write-Host "  ⚠ Azure CLI no encontrado (opcional pero recomendado)" -ForegroundColor Yellow
    }
} catch {
    Write-Host "  ⚠ Azure CLI no encontrado (opcional pero recomendado)" -ForegroundColor Yellow
}
Write-Host ""

# Verificar .NET SDK (para publicar Function App después)
Write-Host "[6/6] Verificando .NET SDK..." -ForegroundColor Yellow
try {
    $dotnetVersion = dotnet --version 2>$null
    if ($null -ne $dotnetVersion) {
        Write-Host "  ✓ .NET SDK instalado: v$dotnetVersion" -ForegroundColor Green
    } else {
        Write-Host "  ⚠ .NET SDK no encontrado (necesario para publicar Function App)" -ForegroundColor Yellow
    }
} catch {
    Write-Host "  ⚠ .NET SDK no encontrado (necesario para publicar Function App)" -ForegroundColor Yellow
}
Write-Host ""

# Resumen final
Write-Host "========================================" -ForegroundColor Cyan
if ($allChecksPassed) {
    Write-Host "  ✓ Todos los requisitos están cumplidos" -ForegroundColor Green
    Write-Host "  Puedes proceder con el despliegue" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Cyan
    exit 0
} else {
    Write-Host "  ✗ Algunos requisitos no están cumplidos" -ForegroundColor Red
    Write-Host "  Por favor, corrige los problemas antes de continuar" -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Cyan
    exit 1
}

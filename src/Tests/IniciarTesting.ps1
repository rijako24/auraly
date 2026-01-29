#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Script maestro para iniciar el entorno de testing de la refactorización.

.DESCRIPTION
    Este script automatiza el inicio del entorno de testing:
    1. Compila el proyecto
    2. Inicia Azure Functions con logging detallado
    3. Opcionalmente ejecuta tests automáticos
    4. Analiza resultados

.PARAMETER RunTests
    Si se especifica, ejecuta automáticamente los tests después de iniciar

.PARAMETER AnalyzeLogs
    Si se especifica, analiza los logs al finalizar

.EXAMPLE
    .\IniciarTesting.ps1

.EXAMPLE
    .\IniciarTesting.ps1 -RunTests -AnalyzeLogs
#>

param(
    [Parameter()]
    [switch]$RunTests,
    
    [Parameter()]
    [switch]$AnalyzeLogs
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message, [string]$Color = "Cyan")
    Write-Host "`n$Message" -ForegroundColor $Color
    Write-Host ("─" * 60) -ForegroundColor Gray
}

function Test-Prerequisites {
    Write-Step "🔍 Verificando pre-requisitos" "Yellow"
    
    # Verificar .NET SDK
    try {
        $dotnetVersion = dotnet --version
        Write-Host "✅ .NET SDK: $dotnetVersion" -ForegroundColor Green
    }
    catch {
        Write-Error ".NET SDK no encontrado. Instalar desde: https://dotnet.microsoft.com/download"
        return $false
    }
    
    # Verificar Azure Functions Core Tools
    try {
        $funcVersion = func --version
        Write-Host "✅ Azure Functions Core Tools: $funcVersion" -ForegroundColor Green
    }
    catch {
        Write-Error "Azure Functions Core Tools no encontrado. Instalar con: npm install -g azure-functions-core-tools@4"
        return $false
    }
    
    # Verificar estructura del proyecto
    $projectRoot = Split-Path -Parent $PSScriptRoot | Split-Path -Parent
    $apiPath = Join-Path $projectRoot "src\API\MimosBabySpa.API"
    
    if (-not (Test-Path $apiPath)) {
        Write-Error "Directorio de API no encontrado: $apiPath"
        return $false
    }
    
    Write-Host "✅ Estructura del proyecto correcta" -ForegroundColor Green
    
    return $true
}

function Build-Project {
    Write-Step "🔨 Compilando proyecto"
    
    $projectRoot = Split-Path -Parent $PSScriptRoot | Split-Path -Parent
    Push-Location $projectRoot
    
    try {
        Write-Host "Limpiando..." -ForegroundColor Gray
        dotnet clean --verbosity quiet
        
        Write-Host "Compilando..." -ForegroundColor Gray
        $buildOutput = dotnet build --verbosity quiet 2>&1
        
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Error en compilación:`n$buildOutput"
            return $false
        }
        
        Write-Host "✅ Compilación exitosa" -ForegroundColor Green
        return $true
    }
    finally {
        Pop-Location
    }
}

function Start-AzureFunctions {
    Write-Step "🚀 Iniciando Azure Functions"
    
    $projectRoot = Split-Path -Parent $PSScriptRoot | Split-Path -Parent
    $apiPath = Join-Path $projectRoot "src\API\MimosBabySpa.API"
    $logsPath = Join-Path $PSScriptRoot "logs.txt"
    
    # Limpiar logs anteriores
    if (Test-Path $logsPath) {
        Remove-Item $logsPath -Force
    }
    
    Write-Host "📝 Logs se guardarán en: $logsPath" -ForegroundColor Gray
    Write-Host "🌐 URL del webhook: http://localhost:7071/api/WhatsAppWebhook" -ForegroundColor Cyan
    
    # Iniciar en segundo plano
    Push-Location $apiPath
    try {
        $job = Start-Job -ScriptBlock {
            param($Path)
            Set-Location $Path
            func start --verbose 2>&1 | Tee-Object -FilePath (Join-Path $using:PSScriptRoot "logs.txt")
        } -ArgumentList $apiPath
        
        Write-Host "✅ Azure Functions iniciándose..." -ForegroundColor Green
        Write-Host "   Job ID: $($job.Id)" -ForegroundColor Gray
        
        # Esperar a que inicie (máximo 30 segundos)
        Write-Host "`n⏳ Esperando que Functions inicie (máximo 30s)..." -ForegroundColor Yellow
        
        $timeout = 30
        $elapsed = 0
        $started = $false
        
        while ($elapsed -lt $timeout -and -not $started) {
            Start-Sleep -Seconds 2
            $elapsed += 2
            
            if (Test-Path $logsPath) {
                $logs = Get-Content $logsPath -Tail 10
                if ($logs -match "Functions:") {
                    $started = $true
                    break
                }
            }
            
            Write-Host "." -NoNewline
        }
        
        Write-Host ""
        
        if ($started) {
            Write-Host "✅ Functions iniciado correctamente!" -ForegroundColor Green
            
            # Mostrar funciones disponibles
            Start-Sleep -Seconds 2
            $logs = Get-Content $logsPath
            $functionsSection = $logs | Select-String -Pattern "Functions:" -Context 0,10
            if ($functionsSection) {
                Write-Host "`n📋 Funciones disponibles:"
                $functionsSection.Context.PostContext | ForEach-Object {
                    if ($_ -match "^\s+\w+:") {
                        Write-Host "   $_" -ForegroundColor Cyan
                    }
                }
            }
            
            return $job
        }
        else {
            Write-Warning "Timeout esperando inicio de Functions"
            Write-Host "Revisa el archivo de logs para más detalles: $logsPath"
            return $null
        }
    }
    finally {
        Pop-Location
    }
}

# Main
Clear-Host

Write-Host @"
╔════════════════════════════════════════════════════════════╗
║                                                            ║
║   🧪 ENTORNO DE TESTING - REFACTORIZACIÓN                 ║
║      Carga de Configuración Optimizada                    ║
║                                                            ║
╚════════════════════════════════════════════════════════════╝
"@ -ForegroundColor Cyan

# 1. Verificar pre-requisitos
if (-not (Test-Prerequisites)) {
    exit 1
}

# 2. Compilar proyecto
if (-not (Build-Project)) {
    exit 1
}

# 3. Iniciar Azure Functions
$functionsJob = Start-AzureFunctions

if (-not $functionsJob) {
    Write-Error "No se pudo iniciar Azure Functions"
    exit 1
}

# 4. Ejecutar tests si se solicitó
if ($RunTests) {
    Write-Step "🧪 Ejecutando tests automáticos"
    
    # Esperar un poco más para asegurar que Functions está listo
    Start-Sleep -Seconds 5
    
    $testScript = Join-Path $PSScriptRoot "TestConversacionCompleta.ps1"
    
    if (Test-Path $testScript) {
        & $testScript
        $testExitCode = $LASTEXITCODE
        
        if ($testExitCode -eq 0) {
            Write-Host "`n✅ Tests completados exitosamente!" -ForegroundColor Green
        }
        else {
            Write-Warning "Tests completados con errores (código: $testExitCode)"
        }
    }
    else {
        Write-Warning "Script de tests no encontrado: $testScript"
    }
}
else {
    Write-Step "ℹ️ Modo interactivo" "Cyan"
    Write-Host @"

Azure Functions está corriendo en segundo plano.

Opciones:
  1. Ejecutar tests manualmente:
     .\TestConversacionCompleta.ps1

  2. Enviar requests manualmente:
     Usa Postman o curl a http://localhost:7071/api/WhatsAppWebhook

  3. Ver logs en tiempo real:
     Get-Content -Wait -Tail 50 .\logs.txt

  4. Analizar logs:
     .\AnalizarLogs.ps1

Presiona Ctrl+C para detener Functions cuando termines.

"@
    
    # Mantener el script corriendo
    try {
        Write-Host "Presiona " -NoNewline
        Write-Host "Ctrl+C" -ForegroundColor Yellow -NoNewline
        Write-Host " para detener...`n"
        
        while ($true) {
            Start-Sleep -Seconds 5
            
            # Verificar que el job sigue corriendo
            $job = Get-Job -Id $functionsJob.Id
            if ($job.State -ne "Running") {
                Write-Warning "Azure Functions se detuvo inesperadamente"
                Write-Host "Revisa los logs para más detalles: .\logs.txt"
                break
            }
        }
    }
    finally {
        Write-Host "`n🛑 Deteniendo Azure Functions..." -ForegroundColor Yellow
        Stop-Job -Id $functionsJob.Id
        Remove-Job -Id $functionsJob.Id -Force
        Write-Host "✅ Functions detenido" -ForegroundColor Green
    }
}

# 5. Analizar logs si se solicitó
if ($AnalyzeLogs) {
    Write-Step "📊 Analizando logs"
    
    $analyzeScript = Join-Path $PSScriptRoot "AnalizarLogs.ps1"
    $logsPath = Join-Path $PSScriptRoot "logs.txt"
    
    if (Test-Path $analyzeScript -and Test-Path $logsPath) {
        & $analyzeScript -LogFile $logsPath
    }
    else {
        Write-Warning "No se pueden analizar logs: archivo de script o logs no encontrado"
    }
}

Write-Host "`n✅ Testing completado" -ForegroundColor Green
Write-Host "📄 Revisa la guía completa en: GUIA_TESTING_REFACTORIZACION.md`n"

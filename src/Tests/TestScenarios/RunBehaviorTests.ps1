# Script para ejecutar pruebas de comportamiento conversacional individualmente
# Uso: .\RunBehaviorTests.ps1 [número_de_prueba]
# Ejemplos:
#   .\RunBehaviorTests.ps1          -> Menú interactivo
#   .\RunBehaviorTests.ps1 1         -> Ejecuta solo la prueba 1
#   .\RunBehaviorTests.ps1 all       -> Ejecuta todas las pruebas

param(
    [Parameter(Mandatory=$false)]
    [string]$TestNumber = ""
)

# Cambiar al directorio del proyecto
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptPath

# Verificar que existe appsettings.json
if (-not (Test-Path "appsettings.json")) {
    Write-Host "ERROR: appsettings.json no encontrado" -ForegroundColor Red
    Write-Host "Copia appsettings.json desde src/Console/MimosBabySpa.Console/" -ForegroundColor Yellow
    exit 1
}

# Función para mostrar el menú
function Show-Menu {
    Write-Host ""
    Write-Host "================================================" -ForegroundColor Cyan
    Write-Host "  PRUEBAS DE COMPORTAMIENTO CONVERSACIONAL" -ForegroundColor Cyan
    Write-Host "================================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Selecciona una prueba:" -ForegroundColor Yellow
    Write-Host "  1. Test 1: Comportamiento de saludo contextual" -ForegroundColor White
    Write-Host "  2. Test 2: Verificación automática de disponibilidad" -ForegroundColor White
    Write-Host "  3. Test 3: No promesas falsas" -ForegroundColor White
    Write-Host "  4. Test 4: Horarios del backend (no inventados)" -ForegroundColor White
    Write-Host "  5. Test 5: Inferencia de referencias implícitas" -ForegroundColor White
    Write-Host "  6. Ejecutar todas las pruebas" -ForegroundColor White
    Write-Host "  0. Salir" -ForegroundColor White
    Write-Host ""
}

# Función para ejecutar una prueba específica
function Run-Test {
    param([int]$Number)
    
    Write-Host ""
    Write-Host "Ejecutando prueba $Number..." -ForegroundColor Green
    Write-Host ""
    
    dotnet run -- behavior:$Number
    
    Write-Host ""
    Write-Host "================================================" -ForegroundColor Cyan
    Write-Host "  PRUEBA COMPLETADA" -ForegroundColor Cyan
    Write-Host "================================================" -ForegroundColor Cyan
}

# Función para ejecutar todas las pruebas
function Run-AllTests {
    Write-Host ""
    Write-Host "Ejecutando todas las pruebas..." -ForegroundColor Green
    Write-Host ""
    
    dotnet run -- behavior
    
    Write-Host ""
    Write-Host "================================================" -ForegroundColor Cyan
    Write-Host "  TODAS LAS PRUEBAS COMPLETADAS" -ForegroundColor Cyan
    Write-Host "================================================" -ForegroundColor Cyan
}

# Si se proporcionó un argumento, ejecutar directamente
if ($TestNumber -ne "") {
    $TestNumber = $TestNumber.Trim().ToLower()
    
    if ($TestNumber -eq "all" -or $TestNumber -eq "todas") {
        Run-AllTests
        exit 0
    }
    
    if ([int]::TryParse($TestNumber, [ref]$null)) {
        $num = [int]$TestNumber
        if ($num -ge 1 -and $num -le 5) {
            Run-Test -Number $num
            exit 0
        } else {
            Write-Host "ERROR: El número de prueba debe estar entre 1 y 5" -ForegroundColor Red
            exit 1
        }
    } else {
        Write-Host "ERROR: Argumento inválido. Usa un número del 1 al 5, 'all', o deja vacío para el menú." -ForegroundColor Red
        exit 1
    }
}

# Modo interactivo
while ($true) {
    Show-Menu
    $choice = Read-Host "Ingresa tu opción"
    
    switch ($choice) {
        "1" { 
            Run-Test -Number 1
            Write-Host ""
            $continue = Read-Host "Presiona Enter para continuar..."
        }
        "2" { 
            Run-Test -Number 2
            Write-Host ""
            $continue = Read-Host "Presiona Enter para continuar..."
        }
        "3" { 
            Run-Test -Number 3
            Write-Host ""
            $continue = Read-Host "Presiona Enter para continuar..."
        }
        "4" { 
            Run-Test -Number 4
            Write-Host ""
            $continue = Read-Host "Presiona Enter para continuar..."
        }
        "5" { 
            Run-Test -Number 5
            Write-Host ""
            $continue = Read-Host "Presiona Enter para continuar..."
        }
        "6" { 
            Run-AllTests
            Write-Host ""
            $continue = Read-Host "Presiona Enter para continuar..."
        }
        "0" { 
            Write-Host "Saliendo..." -ForegroundColor Yellow
            exit 0
        }
        default { 
            Write-Host "Opción inválida. Por favor selecciona 0-6." -ForegroundColor Red
            Start-Sleep -Seconds 1
        }
    }
}

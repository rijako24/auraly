param(
  [switch]$KeepServerProfile,
  [switch]$KeepCatalogCache
)

$ErrorActionPreference = "Stop"

function Stop-AuralyProcesses {
  param([string]$Name)
  Get-Process -Name $Name -ErrorAction SilentlyContinue |
    ForEach-Object {
      try {
        Stop-Process -Id $_.Id -Force -ErrorAction Stop
      } catch {
        Write-Warning "No se pudo cerrar $Name ($($_.Id)): $($_.Exception.Message)"
      }
    }
}

$localDataRoot = Join-Path $env:LOCALAPPDATA "Auraly\PosEdge"
$programData = Join-Path $env:PROGRAMDATA "Auraly"
$databasePath = Join-Path $localDataRoot "auraly-pos.db"
$startupModePath = Join-Path $localDataRoot "startup-mode"
$enrollmentPath = Join-Path $localDataRoot "enrollment.protected"
$keysDirectory = Join-Path $localDataRoot "keys"
$installFolder = Join-Path $env:LOCALAPPDATA "Programs\Auraly POS"

Write-Host "Deteniendo servicios de Auraly..."
Stop-AuralyProcesses -Name "Auraly.Desktop"
Stop-AuralyProcesses -Name "Auraly.Pos.Edge.Host"

Get-Process -Name "Auraly.Pos.Edge.Host","node","Auraly.Desktop" -ErrorAction SilentlyContinue |
  Where-Object {
    $_.Name -eq "node" -and $_.CommandLine -like "*Auraly POS*"
  } |
  ForEach-Object {
    try {
      Stop-Process -Id $_.Id -Force -ErrorAction Stop
    } catch {
      Write-Warning "No se pudo cerrar proceso node ($($_.Id)): $($_.Exception.Message)"
    }
  }

Write-Host "Limpiando estado local del POS..."
if (Test-Path -LiteralPath $startupModePath) {
  Remove-Item -LiteralPath $startupModePath -Force
}
if (Test-Path -LiteralPath $enrollmentPath) {
  Remove-Item -LiteralPath $enrollmentPath -Force
}

if (-not $KeepServerProfile) {
  if (Test-Path -LiteralPath $databasePath) {
    Remove-Item -LiteralPath $databasePath -Force
  }
  if (Test-Path -LiteralPath "$databasePath-wal") {
    Remove-Item -LiteralPath "$databasePath-wal" -Force
  }
  if (Test-Path -LiteralPath "$databasePath-shm") {
    Remove-Item -LiteralPath "$databasePath-shm" -Force
  }
}

if (-not $KeepCatalogCache -and (Test-Path -LiteralPath $keysDirectory)) {
  Get-ChildItem -Path $keysDirectory -File | ForEach-Object {
    Remove-Item -LiteralPath $_.FullName -Force
  }
}

if (Test-Path -LiteralPath $programData) {
  Get-ChildItem -Path $programData -File -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -like "*\\PosEdge*" -or $_.FullName -like "*\\Edge*" } |
    ForEach-Object {
      try {
        Remove-Item -LiteralPath $_.FullName -Force
      } catch {
        Write-Warning "No se pudo limpiar $($_.FullName): $($_.Exception.Message)"
      }
    }
}

Write-Host "La app web guarda sesión por navegador. Ejecuta esto en la consola de la pestaña /pos para limpiar auth local:"
Write-Host "localStorage.removeItem('auth-state');"
Write-Host "localStorage.removeItem('selected_business_id');"
Write-Host "sessionStorage.clear();"

if (Test-Path -LiteralPath $installFolder) {
  Write-Host "Si quieres reinstalar desde cero, elimina esta carpeta de instalación:"
  Write-Host $installFolder
}

Write-Host "Listo. Reinicia Auraly POS para abrir setup desde cero."

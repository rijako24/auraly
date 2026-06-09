# Script de publicación para el proyecto de base de datos
# Uso: .\Publish.ps1 -ServerInstance "localhost" -DatabaseName "MimosBabySpa" -PublishProfile "Local"

param(
    [Parameter(Mandatory=$true)]
    [string]$ServerInstance,
    
    [Parameter(Mandatory=$true)]
    [string]$DatabaseName,
    
    [Parameter(Mandatory=$false)]
    [string]$PublishProfile = "Default",
    
    [Parameter(Mandatory=$false)]
    [string]$Username,
    
    [Parameter(Mandatory=$false)]
    [string]$Password,
    
    [Parameter(Mandatory=$false)]
    [switch]$UseIntegratedSecurity = $true
)

$ErrorActionPreference = "Stop"

Write-Host "Publicando base de datos..." -ForegroundColor Green
Write-Host "Servidor: $ServerInstance" -ForegroundColor Cyan
Write-Host "Base de datos: $DatabaseName" -ForegroundColor Cyan

$projectDir = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $projectDir "MimosBabySpa.Database.sqlproj"
$dacpacPath = Join-Path $projectDir "bin\Debug\MimosBabySpa.Database.dacpac"

# Construir el proyecto
Write-Host "`nCompilando proyecto..." -ForegroundColor Yellow
dotnet build $projectPath --configuration Debug

if ($LASTEXITCODE -ne 0) {
    Write-Host "Error al compilar el proyecto" -ForegroundColor Red
    exit 1
}

# Verificar que el DACPAC existe
if (-not (Test-Path $dacpacPath)) {
    Write-Host "Error: No se encontró el archivo DACPAC en $dacpacPath" -ForegroundColor Red
    Write-Host "Asegúrate de que el proyecto se haya compilado correctamente." -ForegroundColor Yellow
    exit 1
}

# Construir la cadena de conexión
$connectionString = "Server=$ServerInstance;Database=$DatabaseName;TrustServerCertificate=True;"
if ($UseIntegratedSecurity) {
    $connectionString += "Integrated Security=True;"
} else {
    if ([string]::IsNullOrEmpty($Username) -or [string]::IsNullOrEmpty($Password)) {
        Write-Host "Error: Se requiere Username y Password cuando UseIntegratedSecurity es False" -ForegroundColor Red
        exit 1
    }
    $connectionString += "User Id=$Username;Password=$Password;"
}

# Buscar SqlPackage.exe
$sqlPackagePaths = @(
    "C:\Program Files\Microsoft SQL Server\160\DAC\bin\SqlPackage.exe",
    "C:\Program Files (x86)\Microsoft SQL Server\160\DAC\bin\SqlPackage.exe",
    "C:\Program Files\Microsoft SQL Server\150\DAC\bin\SqlPackage.exe",
    "C:\Program Files (x86)\Microsoft SQL Server\150\DAC\bin\SqlPackage.exe",
    "C:\Program Files\Microsoft SQL Server\140\DAC\bin\SqlPackage.exe",
    "C:\Program Files (x86)\Microsoft SQL Server\140\DAC\bin\SqlPackage.exe"
)

$sqlPackagePath = $null
foreach ($path in $sqlPackagePaths) {
    if (Test-Path $path) {
        $sqlPackagePath = $path
        break
    }
}

if ($null -eq $sqlPackagePath) {
    Write-Host "Error: No se encontró SqlPackage.exe" -ForegroundColor Red
    Write-Host "Por favor, instala SQL Server Data Tools (SSDT) o especifica la ruta manualmente." -ForegroundColor Yellow
    exit 1
}

# Publicar usando SqlPackage
Write-Host "`nPublicando base de datos usando: $sqlPackagePath" -ForegroundColor Yellow

$sqlPackageArgs = @(
    "/Action:Publish",
    "/SourceFile:$dacpacPath",
    "/TargetConnectionString:$connectionString",
    "/p:BackupDatabaseBeforeChanges=False",
    "/p:DoNotAlterChangeDataCaptureObjects=True",
    "/p:DoNotAlterReplicatedObjects=True",
    "/p:DropObjectsNotInSource=True",
    "/p:BlockOnPossibleDataLoss=False",
    "/p:DoNotDropObjectTypes=Users;Logins;RoleMembership;Permissions"
)

& $sqlPackagePath $sqlPackageArgs

if ($LASTEXITCODE -eq 0) {
    Write-Host "`nBase de datos publicada correctamente!" -ForegroundColor Green
} else {
    Write-Host "`nError al publicar la base de datos" -ForegroundColor Red
    exit 1
}

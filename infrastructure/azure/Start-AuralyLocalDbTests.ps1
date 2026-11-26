[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Las pruebas LocalDB requieren el runner Windows.' }
$tool = (Get-Command SqlLocalDB.exe -ErrorAction Stop).Source
& $tool info MSSQLLocalDB
& $tool start MSSQLLocalDB
$startResult = $LASTEXITCODE
if ($startResult -ne 0) {
    $log = Join-Path $env:LOCALAPPDATA 'Microsoft/Microsoft SQL Server Local DB/Instances/MSSQLLocalDB/error.log'
    if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log -Tail 100 | Write-Output }
    throw "No arrancó SQL LocalDB del runner (código $startResult)."
}
& $tool info MSSQLLocalDB
try {
    $sqlcmd = (Get-Command sqlcmd.exe -ErrorAction Stop).Source
    & $sqlcmd -S '(localdb)\MSSQLLocalDB' -E -C -d master -l 30 -b -Q "SET NOCOUNT ON; IF CONVERT(int,SERVERPROPERTY('ProductMajorVersion'))<15 THROW 51000,N'Las pruebas requieren SQL Server 2019 o posterior para UTF-8.',1; SELECT SERVERPROPERTY('ProductVersion') AS SqlTestVersion;"
    if ($LASTEXITCODE -ne 0) { throw 'La conexión SQL de prueba no superó la verificación de versión.' }
} catch {
    $log = Join-Path $env:LOCALAPPDATA 'Microsoft/Microsoft SQL Server Local DB/Instances/MSSQLLocalDB/error.log'
    if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log -Tail 100 | Write-Output }
    throw
}

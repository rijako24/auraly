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
$connection = [Data.SqlClient.SqlConnection]::new('Server=(localdb)\MSSQLLocalDB;Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=30;Application Name=AuralyCiLocalDbPreflight;')
try {
    $connection.Open()
    $command = $connection.CreateCommand()
    try {
        $command.CommandText = "SELECT CONVERT(int,SERVERPROPERTY('ProductMajorVersion'));"
        $major = [int]$command.ExecuteScalar()
        if ($major -lt 15) { throw 'Las pruebas requieren SQL Server 2019 o posterior para las intercalaciones UTF-8.' }
        Write-Output "SQL LocalDB disponible para pruebas aisladas; versión principal $major."
    } finally { $command.Dispose() }
} finally { $connection.Dispose() }

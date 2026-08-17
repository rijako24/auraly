#Requires -Version 5.1
#Requires -Modules Az.Sql

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ResourceGroupName,
    [Parameter(Mandatory)][string]$ServerName,
    [Parameter(Mandatory)][string]$DatabaseName,
    [Parameter(Mandatory)][string]$SqlAdministratorLogin,
    [Parameter(Mandatory)][SecureString]$SqlAdministratorPassword,
    [Parameter(Mandatory)][string]$ManagedIdentityName,
    [Parameter(Mandatory)][guid]$ManagedIdentityClientId,
    [Parameter(Mandatory)][string]$DacpacPath,
    [string]$BootstrapAdminPasswordHash,
    [string]$SqlPackagePath = "$env:USERPROFILE\.dotnet\tools\sqlpackage.exe"
)

$ErrorActionPreference = 'Stop'
$resolvedDacpac = (Resolve-Path -LiteralPath $DacpacPath).Path
if (-not (Test-Path -LiteralPath $SqlPackagePath)) {
    throw "No se encontr? SqlPackage en $SqlPackagePath."
}

$passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR(
    $SqlAdministratorPassword)
$password = $null
$firewallRuleName = "AuralyRelease-$([guid]::NewGuid().ToString('N').Substring(0, 10))"

try {
    $password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer)
    $publicIp = (Invoke-RestMethod -Uri 'https://api.ipify.org').Trim()
    $parsedIp = $null
    if (-not [Net.IPAddress]::TryParse($publicIp, [ref]$parsedIp)) {
        throw 'No se pudo determinar una IP p?blica v?lida para la regla temporal.'
    }

    New-AzSqlServerFirewallRule `
        -ResourceGroupName $ResourceGroupName `
        -ServerName $ServerName `
        -FirewallRuleName $firewallRuleName `
        -StartIpAddress $publicIp `
        -EndIpAddress $publicIp | Out-Null

    $connectionStringBuilder = [Data.SqlClient.SqlConnectionStringBuilder]::new()
    $connectionStringBuilder['Data Source'] = "tcp:$ServerName.database.windows.net,1433"
    $connectionStringBuilder['Initial Catalog'] = $DatabaseName
    $connectionStringBuilder['User ID'] = $SqlAdministratorLogin
    $connectionStringBuilder['Password'] = $password
    $connectionStringBuilder['Encrypt'] = $true
    $connectionStringBuilder['TrustServerCertificate'] = $false
    $connectionStringBuilder['Connect Timeout'] = 30
    $connectionString = $connectionStringBuilder.ConnectionString

    $publishArguments = @(
        '/Action:Publish'
        "/SourceFile:$resolvedDacpac"
        "/TargetConnectionString:$connectionString"
        '/p:BlockOnPossibleDataLoss=True'
        '/p:DropObjectsNotInSource=False'
        '/p:ScriptDatabaseOptions=True'
        "/v:BootstrapAdminPasswordHash=$BootstrapAdminPasswordHash"
    )
    & $SqlPackagePath $publishArguments
    if ($LASTEXITCODE) {
        throw "SqlPackage fall? con c?digo $LASTEXITCODE."
    }

    $sidBytes = $ManagedIdentityClientId.ToByteArray()
    $sidHex = '0x' + (($sidBytes | ForEach-Object { $_.ToString('X2') }) -join '')
    $escapedIdentityName = $ManagedIdentityName.Replace(']', ']]')
    $permissionsSql = @"
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$escapedIdentityName')
    CREATE USER [$escapedIdentityName] WITH SID = $sidHex, TYPE = E;
ALTER ROLE db_datareader ADD MEMBER [$escapedIdentityName];
ALTER ROLE db_datawriter ADD MEMBER [$escapedIdentityName];
GRANT EXECUTE TO [$escapedIdentityName];
"@

    $sqlcmd = Get-Command sqlcmd -ErrorAction Stop
    $env:SQLCMDPASSWORD = $password
    & $sqlcmd.Source `
        -S "$ServerName.database.windows.net" `
        -d $DatabaseName `
        -U $SqlAdministratorLogin `
        -N `
        -b `
        -Q $permissionsSql
    if ($LASTEXITCODE) {
        throw "La creaci?n del usuario Managed Identity fall? con c?digo $LASTEXITCODE."
    }

    Write-Host "Base $DatabaseName publicada y Managed Identity autorizada." -ForegroundColor Green
}
finally {
    $env:SQLCMDPASSWORD = $null
    $password = $null
    if ($passwordPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer)
    }
    Remove-AzSqlServerFirewallRule `
        -ResourceGroupName $ResourceGroupName `
        -ServerName $ServerName `
        -FirewallRuleName $firewallRuleName `
        -ErrorAction SilentlyContinue | Out-Null
}

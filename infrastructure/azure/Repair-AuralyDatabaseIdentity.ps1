#Requires -Version 5.1
#Requires -Modules Az.Sql

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ResourceGroupName,
    [Parameter(Mandatory)][string]$ServerName,
    [Parameter(Mandatory)][string]$DatabaseName,
    [Parameter(Mandatory)][string]$ManagedIdentityName,
    [Parameter(Mandatory)][guid]$ManagedIdentityClientId,
    [string]$SqlAdministratorLogin = 'auralyadmin'
)

$ErrorActionPreference = 'Stop'
$alphabet = 'abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789!@#$%*-_+'
$random = [Security.Cryptography.RandomNumberGenerator]::Create()
$bytes = [byte[]]::new(28)
$plainPassword = $null
$securePassword = $null
$passwordPointer = [IntPtr]::Zero
$firewallRuleName = "AuralyIdentity-$([guid]::NewGuid().ToString('N').Substring(0, 10))"

try {
    $random.GetBytes($bytes)
    $plainPassword = 'Aa1!' + (($bytes | ForEach-Object {
                $alphabet[$_ % $alphabet.Length]
            }) -join '')
    $securePassword = ConvertTo-SecureString $plainPassword -AsPlainText -Force

    Set-AzSqlServer `
        -ResourceGroupName $ResourceGroupName `
        -ServerName $ServerName `
        -SqlAdministratorPassword $securePassword `
        -Force | Out-Null

    $publicIp = (Invoke-RestMethod -Uri 'https://api.ipify.org').Trim()
    $parsedIp = $null
    if (-not [Net.IPAddress]::TryParse($publicIp, [ref]$parsedIp)) {
        throw 'Could not determine a valid public IP for the temporary rule.'
    }

    New-AzSqlServerFirewallRule `
        -ResourceGroupName $ResourceGroupName `
        -ServerName $ServerName `
        -FirewallRuleName $firewallRuleName `
        -StartIpAddress $publicIp `
        -EndIpAddress $publicIp | Out-Null

    $passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
    $plainPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer)
    $sidBytes = $ManagedIdentityClientId.ToByteArray()
    $sidHex = '0x' + (($sidBytes | ForEach-Object { $_.ToString('X2') }) -join '')
    $escapedIdentityName = $ManagedIdentityName.Replace(']', ']]').Replace("'", "''")

    $permissionsSql = @"
SET NOCOUNT ON;
DECLARE @ExpectedSid varbinary(85) = $sidHex;

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$escapedIdentityName')
BEGIN
    CREATE USER [$escapedIdentityName] WITH SID = $sidHex, TYPE = E;
END
ELSE IF EXISTS (
    SELECT 1
    FROM sys.database_principals
    WHERE name = N'$escapedIdentityName'
      AND sid <> @ExpectedSid)
BEGIN
    DROP USER [$escapedIdentityName];
    CREATE USER [$escapedIdentityName] WITH SID = $sidHex, TYPE = E;
END;

IF IS_ROLEMEMBER(N'db_datareader', N'$escapedIdentityName') <> 1
    ALTER ROLE db_datareader ADD MEMBER [$escapedIdentityName];
IF IS_ROLEMEMBER(N'db_datawriter', N'$escapedIdentityName') <> 1
    ALTER ROLE db_datawriter ADD MEMBER [$escapedIdentityName];
GRANT EXECUTE TO [$escapedIdentityName];

IF NOT EXISTS (
    SELECT 1
    FROM sys.database_principals
    WHERE name = N'$escapedIdentityName'
      AND sid = @ExpectedSid
      AND type = 'E')
    THROW 51000, 'Managed Identity database principal verification failed.', 1;
"@

    $sqlcmd = Get-Command sqlcmd -ErrorAction Stop
    $env:SQLCMDPASSWORD = $plainPassword
    & $sqlcmd.Source `
        -S "$ServerName.database.windows.net" `
        -d $DatabaseName `
        -U $SqlAdministratorLogin `
        -N `
        -r 1 `
        -V 11 `
        -b `
        -Q $permissionsSql
    if ($LASTEXITCODE) {
        throw "Managed Identity repair failed with sqlcmd exit code $LASTEXITCODE."
    }

    [pscustomobject]@{
        database = $DatabaseName
        identity = $ManagedIdentityName
        clientId = $ManagedIdentityClientId
        verified = $true
    }
}
finally {
    $env:SQLCMDPASSWORD = $null
    $plainPassword = $null
    $securePassword = $null
    $bytes = $null
    if ($passwordPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer)
    }
    $random.Dispose()
    Remove-AzSqlServerFirewallRule `
        -ResourceGroupName $ResourceGroupName `
        -ServerName $ServerName `
        -FirewallRuleName $firewallRuleName `
        -ErrorAction SilentlyContinue | Out-Null
}

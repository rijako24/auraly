#Requires -Version 5.1
#Requires -Modules Az.Sql

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ResourceGroupName,
    [Parameter(Mandatory)][string]$ServerName,
    [Parameter(Mandatory)][string]$DatabaseName,
    [Parameter(Mandatory)][string]$ManagedIdentityName,
    [Parameter(Mandatory)][guid]$ManagedIdentityClientId,
    [Parameter(Mandatory)][string]$DacpacPath,
    [string]$SqlAdministratorLogin = 'auralyadmin'
)

$ErrorActionPreference = 'Stop'
$alphabet = 'abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789!@#$%*-_+'
$random = [Security.Cryptography.RandomNumberGenerator]::Create()
$bytes = [byte[]]::new(28)
$plainPassword = $null
$securePassword = $null

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

    & (Join-Path $PSScriptRoot 'Publish-AuralyDatabase.ps1') `
        -ResourceGroupName $ResourceGroupName `
        -ServerName $ServerName `
        -DatabaseName $DatabaseName `
        -SqlAdministratorLogin $SqlAdministratorLogin `
        -SqlAdministratorPassword $securePassword `
        -ManagedIdentityName $ManagedIdentityName `
        -ManagedIdentityClientId $ManagedIdentityClientId `
        -DacpacPath $DacpacPath
}
finally {
    $plainPassword = $null
    $securePassword = $null
    $bytes = $null
    $random.Dispose()
}

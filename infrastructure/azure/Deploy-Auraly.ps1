#Requires -Version 5.1
#Requires -Modules Az.Accounts, Az.Resources

[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Validate', 'WhatIf', 'Apply')]
    [string]$Mode,

    [Parameter(Mandatory)]
    [ValidateSet('Shared', 'Dev', 'Prod', 'All')]
    [string]$Scope,

    [Parameter(Mandatory)]
    [string]$ReleaseVersion,

    [string]$SubscriptionId = '5ea009ce-23c5-4bbd-b1c8-62116d58f596',

    [string]$Location = 'eastus2',

    [string]$SqlAdministratorLogin = 'auralyadmin',

    [SecureString]$SqlAdministratorPassword,

    [string]$SqlEntraAdministratorLogin,

    [string]$SqlEntraAdministratorObjectId,

    [SecureString]$JwtSecret,

    [SecureString]$OfflineLeaseSigningPrivateKeyPem,

    [string]$WebPushPublicKey,

    [SecureString]$WebPushPrivateKey,

    [SecureString]$FiscalSecretProtectionKey,

    [SecureString]$WhatsAppVerifyToken,

    [ValidatePattern('^[0-9A-Fa-f]{64}$')]
    [string]$PosInstallerSha256,

    [switch]$IncludeSharedAudio,

    [switch]$SeedAppConfiguration
)

$ErrorActionPreference = 'Stop'
$templateRoot = $PSScriptRoot
$temporaryPath = Join-Path ([IO.Path]::GetTempPath()) "auraly-bicep-$([guid]::NewGuid().ToString('N'))"

function New-RandomSqlPassword {
    $lower = 'abcdefghijkmnopqrstuvwxyz'
    $upper = 'ABCDEFGHJKLMNPQRSTUVWXYZ'
    $digits = '23456789'
    $symbols = '!@#$%*-_+='
    $all = $lower + $upper + $digits + $symbols
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $characters = @(
            $lower[(Get-Random -Maximum $lower.Length)]
            $upper[(Get-Random -Maximum $upper.Length)]
            $digits[(Get-Random -Maximum $digits.Length)]
            $symbols[(Get-Random -Maximum $symbols.Length)]
        )
        while ($characters.Count -lt 32) {
            $byte = [byte[]]::new(1)
            $rng.GetBytes($byte)
            $characters += $all[$byte[0] % $all.Length]
        }
        return ConvertTo-SecureString (($characters | Sort-Object { Get-Random }) -join '') `
            -AsPlainText -Force
    }
    finally {
        $rng.Dispose()
    }
}

function Ensure-ResourceGroup {
    param([string]$Name)

    $group = Get-AzResourceGroup -Name $Name -ErrorAction SilentlyContinue
    if (-not $group) {
        if ($Mode -ne 'Apply') {
            throw "El grupo $Name no existe. Ejecute Apply para crearlo antes de $Mode."
        }
        $group = New-AzResourceGroup -Name $Name -Location $Location -Tag @{
            application = 'AURALY'
            environment = $Name.Replace('RG-AURALY-', '').ToLowerInvariant()
            managedBy = 'Bicep'
        }
    }
    return $group
}

function Invoke-Template {
    param(
        [string]$ResourceGroupName,
        [string]$DeploymentName,
        [string]$TemplateFile,
        [hashtable]$Parameters
    )

    switch ($Mode) {
        'Validate' {
            Test-AzResourceGroupDeployment `
                -ResourceGroupName $ResourceGroupName `
                -TemplateFile $TemplateFile `
                -TemplateParameterObject $Parameters
            return $null
        }
        'WhatIf' {
            return Get-AzResourceGroupDeploymentWhatIfResult `
                -ResourceGroupName $ResourceGroupName `
                -Name $DeploymentName `
                -TemplateFile $TemplateFile `
                -TemplateParameterObject $Parameters `
                -ResultFormat FullResourcePayloads
        }
        'Apply' {
            if (-not $PSCmdlet.ShouldProcess(
                $ResourceGroupName,
                "Desplegar $DeploymentName")) {
                return $null
            }
            return New-AzResourceGroupDeployment `
                -ResourceGroupName $ResourceGroupName `
                -Name $DeploymentName `
                -TemplateFile $TemplateFile `
                -TemplateParameterObject $Parameters `
                -Mode Incremental `
                -Force
        }
    }
}

try {
    Set-AzContext -SubscriptionId $SubscriptionId | Out-Null
    New-Item -ItemType Directory -Path $temporaryPath -Force | Out-Null

    $sharedTemplate = Join-Path $temporaryPath 'shared-ai.json'
    $environmentTemplate = Join-Path $temporaryPath 'main.json'
    & az bicep build --file (Join-Path $templateRoot 'shared-ai.bicep') --outfile $sharedTemplate
    if ($LASTEXITCODE) { throw 'No se pudo compilar shared-ai.bicep.' }
    & az bicep build --file (Join-Path $templateRoot 'main.bicep') --outfile $environmentTemplate
    if ($LASTEXITCODE) { throw 'No se pudo compilar main.bicep.' }

    if (-not $SqlEntraAdministratorLogin -or -not $SqlEntraAdministratorObjectId) {
        $accountId = (Get-AzContext).Account.Id
        $user = Get-AzADUser -UserPrincipalName $accountId
        if (-not $user) {
            throw 'No se pudo resolver el administrador Entra. Especifique login y object ID.'
        }
        $SqlEntraAdministratorLogin = $accountId
        $SqlEntraAdministratorObjectId = $user.Id
    }
    if (-not $SqlAdministratorPassword) {
        $SqlAdministratorPassword = New-RandomSqlPassword
    }

    $sharedGroupName = 'RG-AURALY-SHARED'
    $sharedGroup = Ensure-ResourceGroup -Name $sharedGroupName
    $sharedDeployment = $null

    if ($Scope -in @('Shared', 'All')) {
        $sharedDeployment = Invoke-Template `
            -ResourceGroupName $sharedGroup.ResourceGroupName `
            -DeploymentName "auraly-shared-$ReleaseVersion" `
            -TemplateFile $sharedTemplate `
            -Parameters @{
                location = $Location
                releaseVersion = $ReleaseVersion
                deployAudio = [bool]$IncludeSharedAudio
            }
        if ($Mode -ne 'Apply' -or $Scope -eq 'Shared') {
            $sharedDeployment
            return
        }
    }

    if ($Mode -eq 'Apply' -and $sharedDeployment) {
        $sharedAccountName = $sharedDeployment.Outputs.accountName.Value
        $sharedEndpoint = $sharedDeployment.Outputs.endpoint.Value
        $textDeploymentName = $sharedDeployment.Outputs.textDeploymentName.Value
        $audioDeploymentName = $sharedDeployment.Outputs.audioDeploymentName.Value
    }
    else {
        $sharedResource = Get-AzResource `
            -ResourceGroupName $sharedGroupName `
            -ResourceType 'Microsoft.CognitiveServices/accounts' |
            Where-Object Name -Like 'ai-auraly-shared-*' |
            Select-Object -First 1
        if (-not $sharedResource) {
            throw 'El recurso AI compartido no existe. Despliegue Scope=Shared primero.'
        }
        $sharedAccountName = $sharedResource.Name
        $sharedEndpoint = "https://$sharedAccountName.cognitiveservices.azure.com/"
        $textDeploymentName = 'gpt-4.1-mini'
        $audioDeploymentName = 'whisper'
    }

    $environments = switch ($Scope) {
        'Dev' { @('dev') }
        'Prod' { @('prod') }
        'All' { @('dev', 'prod') }
        default { @() }
    }

    if ($environments.Count -gt 0 -and (-not $JwtSecret -or -not $OfflineLeaseSigningPrivateKeyPem -or -not $WebPushPublicKey -or -not $WebPushPrivateKey -or -not $FiscalSecretProtectionKey -or -not $WhatsAppVerifyToken)) {
        throw 'JwtSecret, OfflineLeaseSigningPrivateKeyPem, WebPushPublicKey, WebPushPrivateKey, FiscalSecretProtectionKey y WhatsAppVerifyToken son obligatorios para desplegar DEV o PROD.'
    }
    if ($environments.Count -gt 0 -and -not $PosInstallerSha256) {
        $repositoryRoot = (Resolve-Path (Join-Path $templateRoot '..\..')).Path
        $manifestPath = Join-Path $repositoryRoot "artifacts\releases\$ReleaseVersion\manifest.json"
        if (-not (Test-Path -LiteralPath $manifestPath)) {
            throw "No existe el manifiesto del release $ReleaseVersion para resolver el instalador POS."
        }
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        $installerName = "auraly-pos-$ReleaseVersion.exe"
        $installer = $manifest.artifacts | Where-Object name -EQ $installerName | Select-Object -First 1
        if (-not $installer -or "$($installer.sha256)" -notmatch '^[0-9A-Fa-f]{64}$') {
            throw "El release no contiene $installerName con SHA-256 válido."
        }
        $PosInstallerSha256 = "$($installer.sha256)"
    }

    foreach ($environment in $environments) {
        $groupName = "RG-AURALY-$($environment.ToUpperInvariant())"
        $group = Ensure-ResourceGroup -Name $groupName
        $result = Invoke-Template `
            -ResourceGroupName $group.ResourceGroupName `
            -DeploymentName "auraly-$environment-$ReleaseVersion" `
            -TemplateFile $environmentTemplate `
            -Parameters @{
                environment = $environment
                location = $Location
                releaseVersion = $ReleaseVersion
                posInstallerSha256 = $PosInstallerSha256
                deployStaticAdminSettings = $Mode -ne 'WhatIf'
                sqlAdministratorLogin = $SqlAdministratorLogin
                sqlAdministratorPassword = $SqlAdministratorPassword
                sqlEntraAdministratorLogin = $SqlEntraAdministratorLogin
                sqlEntraAdministratorObjectId = $SqlEntraAdministratorObjectId
                sharedOpenAiEndpoint = $sharedEndpoint
                sharedOpenAiResourceGroupName = $sharedGroupName
                sharedOpenAiAccountName = $sharedAccountName
                textModelDeploymentName = $textDeploymentName
                audioModelDeploymentName = $audioDeploymentName
                jwtSecret = $JwtSecret
                offlineLeaseSigningPrivateKeyPem = $OfflineLeaseSigningPrivateKeyPem
                webPushPublicKey = $WebPushPublicKey
                webPushPrivateKey = $WebPushPrivateKey
                fiscalSecretProtectionKey = $FiscalSecretProtectionKey
                whatsAppVerifyToken = $WhatsAppVerifyToken
                seedAppConfiguration = [bool]$SeedAppConfiguration
            }
        $result
    }
}
finally {
    $SqlAdministratorPassword = $null
    $JwtSecret = $null
    $OfflineLeaseSigningPrivateKeyPem = $null
    $WebPushPrivateKey = $null
    $FiscalSecretProtectionKey = $null
    if (Test-Path -LiteralPath $temporaryPath) {
        Remove-Item -LiteralPath $temporaryPath -Recurse -Force
    }
}

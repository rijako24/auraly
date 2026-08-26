#Requires -Version 7.2

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('dev', 'prod')]
    [string]$Environment,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$')]
    [string]$ReleaseVersion,

    [string]$ReleaseRoot,

    [string]$SqlPackagePath = 'sqlpackage'
)

$ErrorActionPreference = 'Stop'
$resolvedSqlPackagePath = $SqlPackagePath
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($ReleaseRoot)) {
    $ReleaseRoot = Join-Path $repoRoot "artifacts/releases/$ReleaseVersion"
}
$releasePath = (Resolve-Path -LiteralPath $ReleaseRoot).Path
$manifestPath = Join-Path $releasePath 'manifest.json'

$configuration = if ($Environment -eq 'dev') {
    @{
        ResourceGroup = 'RG-AURALY-DEV'
        Storage = 'stauralydevw5usmo6w'
        SqlServer = 'sql-auraly-dev-w5usmo6w'
        Database = 'auraly-dev'
        Function = 'func-auraly-dev-w5usmo6w'
        Api = 'api-auraly-dev-w5usmo6w'
        Admin = 'admin-auraly-dev-w5usmo6w'
    }
}
else {
    @{
        ResourceGroup = 'RG-AURALY-PROD'
        Storage = 'stauralyprod7sov4nxc'
        SqlServer = 'sql-auraly-prod-7sov4nxc'
        Database = 'auraly-prod'
        Function = 'func-auraly-prod-7sov4nxc'
        Api = 'api-auraly-prod-7sov4nxc'
        Admin = 'admin-auraly-prod-7sov4nxc'
    }
}

function Assert-LastExitCode {
    param([Parameter(Mandatory)][string]$Message)
    if ($LASTEXITCODE -ne 0) { throw "$Message (codigo $LASTEXITCODE)." }
}

function Invoke-AzJson {
    param([Parameter(Mandatory)][string[]]$Arguments)
    $output = & az @Arguments
    Assert-LastExitCode "Azure CLI fallo: az $($Arguments[0..1] -join ' ')"
    if ([string]::IsNullOrWhiteSpace("$output")) { return $null }
    return ($output | ConvertFrom-Json)
}

function Wait-HttpHealthy {
    param(
        [Parameter(Mandatory)][string]$Uri,
        [int]$Attempts = 18,
        [int]$DelaySeconds = 10
    )
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        try {
            $response = Invoke-WebRequest -Uri $Uri -TimeoutSec 30
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 400) {
                return $response.StatusCode
            }
        }
        catch {
            if ($attempt -eq $Attempts) {
                throw "Health check agotado para ${Uri}: $($_.Exception.Message)"
            }
        }
        Start-Sleep -Seconds $DelaySeconds
    }
}

function Invoke-ReviewedPreDacpacMigration {
    param(
        [Parameter(Mandatory)][string]$MigrationPath,
        [Parameter(Mandatory)][string]$AccessToken
    )
    if (-not (Test-Path -LiteralPath $MigrationPath)) {
        throw "No existe la migracion previa al DACPAC: $MigrationPath"
    }
    $connection = [System.Data.SqlClient.SqlConnection]::new(
        "Server=tcp:$($configuration.SqlServer).database.windows.net,1433;" +
        "Initial Catalog=$($configuration.Database);Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;")
    $connection.AccessToken = $AccessToken
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandTimeout = 120
        $command.CommandText = Get-Content -LiteralPath $MigrationPath -Raw
        [void]$command.ExecuteNonQuery()
        Write-Information "Migracion previa al DACPAC aplicada: $(Split-Path $MigrationPath -Leaf)." -InformationAction Continue
    }
    finally {
        $connection.Dispose()
    }
}

function Test-Release {
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        throw "No existe el manifiesto $manifestPath."
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.product -ne 'AURALY' -or $manifest.version -ne $ReleaseVersion -or $manifest.dirty) {
        throw 'El manifiesto no es un release Auraly limpio de la version solicitada.'
    }
    foreach ($artifact in $manifest.artifacts) {
        $artifactPath = Join-Path $releasePath $artifact.name
        if (-not (Test-Path -LiteralPath $artifactPath)) {
            throw "Falta el artefacto $($artifact.name)."
        }
        $hash = (Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($hash -ne $artifact.sha256 -or (Get-Item -LiteralPath $artifactPath).Length -ne $artifact.bytes) {
            throw "El artefacto $($artifact.name) no coincide con el manifiesto."
        }
    }
    foreach ($requiredName in @(
        "auraly-database-$ReleaseVersion.dacpac",
        "auraly-function-$ReleaseVersion.zip",
        "auraly-api-$ReleaseVersion.zip",
        "auraly-pos-$ReleaseVersion.exe")) {
        if (-not ($manifest.artifacts.name -contains $requiredName)) {
            throw "El release no contiene $requiredName."
        }
    }
    return $manifest
}

function Publish-Database {
    $dacpac = Join-Path $releasePath "auraly-database-$ReleaseVersion.dacpac"
    $firewallRule = "github-$Environment-$([guid]::NewGuid().ToString('N').Substring(0, 10))"
    $reportPath = Join-Path ([IO.Path]::GetTempPath()) "auraly-$Environment-$ReleaseVersion-deploy-report.xml"
    $accessToken = $null
    try {
        $publicIp = (Invoke-RestMethod -Uri 'https://api.ipify.org').Trim()
        $parsedIp = $null
        if (-not [Net.IPAddress]::TryParse($publicIp, [ref]$parsedIp)) {
            throw 'No se pudo determinar una IP publica valida para Azure SQL.'
        }
        & az sql server firewall-rule create `
            --resource-group $configuration.ResourceGroup `
            --server $configuration.SqlServer `
            --name $firewallRule `
            --start-ip-address $publicIp `
            --end-ip-address $publicIp `
            --output none
        Assert-LastExitCode 'No se pudo crear la regla SQL temporal'

        $tokenResponse = Invoke-AzJson @(
            'account', 'get-access-token',
            '--resource', 'https://database.windows.net/',
            '--output', 'json')
        $accessToken = if ($tokenResponse.accessToken) { $tokenResponse.accessToken } else { $tokenResponse.token }
        if ([string]::IsNullOrWhiteSpace($accessToken)) { throw 'Azure no devolvio token para SQL.' }

        # Esta migracion retira una columna solo despues de preservar su valor.
        # Se ejecuta antes del DeployReport para que BlockOnPossibleDataLoss siga
        # protegiendo cualquier otra eliminacion no revisada del DACPAC.
        Invoke-ReviewedPreDacpacMigration `
            -MigrationPath (Join-Path $repoRoot 'database/Auraly.Database/Scripts/Migrations/20260823_MoveBusinessLogoToTenant.sql') `
            -AccessToken $accessToken
        Invoke-ReviewedPreDacpacMigration `
            -MigrationPath (Join-Path $repoRoot 'database/Auraly.Database/Scripts/Migrations/20260824_MoveFiscalCredentialsToTenant.sql') `
            -AccessToken $accessToken
        Invoke-ReviewedPreDacpacMigration `
            -MigrationPath (Join-Path $repoRoot 'database/Auraly.Database/Scripts/Migrations/20260825_AddPurchaseEvidence.sql') `
            -AccessToken $accessToken

        $commonArguments = @(
            "/SourceFile:$dacpac",
            "/TargetServerName:tcp:$($configuration.SqlServer).database.windows.net,1433",
            "/TargetDatabaseName:$($configuration.Database)",
            "/AccessToken:$accessToken",
            "/v:CJWhatsAppAccessToken=$($env:CJ_WHATSAPP_ACCESS_TOKEN)",
            '/p:BlockOnPossibleDataLoss=True',
            '/p:DropObjectsNotInSource=False',
            '/p:CommandTimeout=120',
            '/TargetTimeout:60')

        & $resolvedSqlPackagePath '/Action:DeployReport' @commonArguments "/OutputPath:$reportPath" '/Quiet:True'
        Assert-LastExitCode 'No se pudo generar el plan del DACPAC'
        [xml]$report = Get-Content -LiteralPath $reportPath -Raw
        $namespace = [Xml.XmlNamespaceManager]::new($report.NameTable)
        $namespace.AddNamespace('d', 'http://schemas.microsoft.com/sqlserver/dac/DeployReport/2012/02')
        $destructiveItems = @($report.SelectNodes(
            "//d:Operation[@Name='Drop']/d:Item[@Type='SqlTable' or @Type='SqlColumn']",
            $namespace))
        if ($destructiveItems.Count -gt 0) {
            $objects = ($destructiveItems | ForEach-Object { $_.GetAttribute('Value') }) -join ', '
            throw "El plan intenta borrar tablas o columnas: $objects"
        }
        $rebuilds = @($report.SelectNodes("//d:Operation[@Name='TableRebuild']/d:Item", $namespace))
        if ($rebuilds.Count -gt 0) {
            Write-Information "Reconstrucciones de tabla revisadas por BlockOnPossibleDataLoss: $($rebuilds.Count)." -InformationAction Continue
        }

        & $resolvedSqlPackagePath '/Action:Publish' @commonArguments
        Assert-LastExitCode 'La publicacion del DACPAC fallo'
    }
    finally {
        $accessToken = $null
        if (Test-Path -LiteralPath $reportPath) {
            Remove-Item -LiteralPath $reportPath -Force
        }
        & az sql server firewall-rule delete `
            --resource-group $configuration.ResourceGroup `
            --server $configuration.SqlServer `
            --name $firewallRule `
            --output none 2>$null
    }
}

function Publish-Function {
    $container = 'auraly-deploy'
    $blobName = "auraly-function-$ReleaseVersion-$([guid]::NewGuid().ToString('N')).zip"
    $zip = Join-Path $releasePath "auraly-function-$ReleaseVersion.zip"
    $uploaded = $false
    $packageUri = $null
    try {
        for ($attempt = 1; $attempt -le 12; $attempt++) {
            & az storage blob upload `
                --account-name $configuration.Storage `
                --container-name $container `
                --name $blobName `
                --file $zip `
                --auth-mode login `
                --overwrite true `
                --output none 2>$null
            if ($LASTEXITCODE -eq 0) { $uploaded = $true; break }
            if ($attempt -eq 12) {
                throw 'No se pudo cargar Function. Valide Storage Blob Data Contributor para la identidad OIDC.'
            }
            Start-Sleep -Seconds 10
        }
        $expiry = [DateTime]::UtcNow.AddHours(2).ToString('yyyy-MM-ddTHH:mmZ')
        $packageUri = (& az storage blob generate-sas `
            --account-name $configuration.Storage `
            --container-name $container `
            --name $blobName `
            --permissions r `
            --expiry $expiry `
            --auth-mode login `
            --as-user `
            --full-uri `
            --output tsv).Trim()
        Assert-LastExitCode 'No se pudo generar el SAS de delegacion para Function'
        if (-not $packageUri) { throw 'Azure devolvio un SAS vacio para Function.' }

        & az deployment group create `
            --name "auraly-$Environment-function-$ReleaseVersion" `
            --resource-group $configuration.ResourceGroup `
            --template-file (Join-Path $PSScriptRoot 'function-onedeploy-v2.bicep') `
            --parameters "functionAppName=$($configuration.Function)" 'location=eastus2' "packageUri=$packageUri" `
            --output none
        Assert-LastExitCode 'OneDeploy de Function fallo'
        & az functionapp config appsettings set `
            --resource-group $configuration.ResourceGroup `
            --name $configuration.Function `
            --settings "Release__Version=$ReleaseVersion" `
            --output none
        Assert-LastExitCode 'No se pudo registrar Release__Version en Function'

        if ($Environment -eq 'dev' -and
            -not [string]::IsNullOrWhiteSpace($env:CJ_WHATSAPP_FUNCTION_KEY) -and
            -not [string]::IsNullOrWhiteSpace($env:CJ_WHATSAPP_VERIFY_TOKEN)) {
            & az functionapp function keys set `
                --resource-group $configuration.ResourceGroup `
                --name $configuration.Function `
                --function-name 'WhatsAppWebhook' `
                --key-name 'meta-cj' `
                --key-value $env:CJ_WHATSAPP_FUNCTION_KEY `
                --output none
            Assert-LastExitCode 'No se pudo configurar la Function key dedicada de Meta para CJ'
            & az functionapp config appsettings set `
                --resource-group $configuration.ResourceGroup `
                --name $configuration.Function `
                --settings "WhatsApp__Webhook__VerifyToken=$($env:CJ_WHATSAPP_VERIFY_TOKEN)" `
                --output none
            Assert-LastExitCode 'No se pudo configurar el verify token de Meta para CJ'
        }
    }
    finally {
        $packageUri = $null
        if ($uploaded) {
            & az storage blob delete `
                --account-name $configuration.Storage `
                --container-name $container `
                --name $blobName `
                --auth-mode login `
                --output none 2>$null
        }
    }
}

function Publish-Api {
    $zip = Join-Path $releasePath "auraly-api-$ReleaseVersion.zip"
    & az webapp deploy `
        --resource-group $configuration.ResourceGroup `
        --name $configuration.Api `
        --src-path $zip `
        --type zip `
        --restart true `
        --clean true `
        --output none
    $deployExitCode = $LASTEXITCODE
    if ($deployExitCode -ne 0) {
        $deploymentLog = & az webapp log deployment show `
            --resource-group $configuration.ResourceGroup `
            --name $configuration.Api `
            --query "[-1].message" `
            --output tsv 2>$null
        if ($LASTEXITCODE -ne 0 -or "$deploymentLog" -notmatch 'Deployment successful') {
            throw "El despliegue de API fallo y Kudu no confirmo exito (codigo $deployExitCode)."
        }
        Write-Warning 'Azure CLI perdio el seguimiento, pero Kudu confirmo Deployment successful.'
    }
    & az webapp config appsettings set `
        --resource-group $configuration.ResourceGroup `
        --name $configuration.Api `
        --settings "Release__Version=$ReleaseVersion" `
        --output none
    Assert-LastExitCode 'No se pudo registrar Release__Version en API'
}

function Publish-PosInstallerIfPresent {
    $installerName = "auraly-pos-$ReleaseVersion.exe"
    $installerPath = Join-Path $releasePath $installerName
    if (-not (Test-Path -LiteralPath $installerPath)) {
        throw "El release no contiene el instalador POS $installerName."
    }
    $installerArtifact = $manifest.artifacts |
        Where-Object name -EQ $installerName |
        Select-Object -First 1
    if (-not $installerArtifact -or "$($installerArtifact.sha256)" -notmatch '^[0-9A-Fa-f]{64}$') {
        throw "El manifiesto no contiene un SHA-256 valido para $installerName."
    }
    & az storage blob upload `
        --account-name $configuration.Storage `
        --container-name downloads `
        --name 'Auraly-POS-Setup.exe' `
        --file $installerPath `
        --auth-mode login `
        --overwrite true `
        --output none
    Assert-LastExitCode 'No se pudo publicar el instalador POS'
    & az webapp config appsettings set `
        --resource-group $configuration.ResourceGroup `
        --name $configuration.Api `
        --settings `
            "PosInstaller__Version=$ReleaseVersion" `
            "PosInstaller__Sha256=$($installerArtifact.sha256)" `
        --output none
    Assert-LastExitCode 'No se pudo registrar la version e integridad del instalador POS en API'
    return $installerArtifact
}

$manifest = Test-Release
Write-Information "Release $ReleaseVersion verificado; commit $($manifest.commit)." -InformationAction Continue
Publish-Database
Publish-Function
Publish-Api
$installerMetadata = Publish-PosInstallerIfPresent

$apiStatus = Wait-HttpHealthy "https://$($configuration.Api).azurewebsites.net/health"
$functionStatus = Wait-HttpHealthy "https://$($configuration.Function).azurewebsites.net/api/health"
$apiVersion = (& az webapp config appsettings list `
    --resource-group $configuration.ResourceGroup `
    --name $configuration.Api `
    --query "[?name=='Release__Version'].value | [0]" `
    --output tsv).Trim()
Assert-LastExitCode 'No se pudo validar la version de API'
$functionVersion = (& az functionapp config appsettings list `
    --resource-group $configuration.ResourceGroup `
    --name $configuration.Function `
    --query "[?name=='Release__Version'].value | [0]" `
    --output tsv).Trim()
Assert-LastExitCode 'No se pudo validar la version de Function'
$installerVersion = (& az webapp config appsettings list `
    --resource-group $configuration.ResourceGroup `
    --name $configuration.Api `
    --query "[?name=='PosInstaller__Version'].value | [0]" `
    --output tsv).Trim()
Assert-LastExitCode 'No se pudo validar la version remota del instalador POS'
$installerSha256 = (& az webapp config appsettings list `
    --resource-group $configuration.ResourceGroup `
    --name $configuration.Api `
    --query "[?name=='PosInstaller__Sha256'].value | [0]" `
    --output tsv).Trim()
Assert-LastExitCode 'No se pudo validar la integridad remota del instalador POS'
if ($apiVersion -ne $ReleaseVersion -or $functionVersion -ne $ReleaseVersion -or
    $installerVersion -ne $ReleaseVersion -or
    $installerSha256 -ne "$($installerMetadata.sha256)") {
    throw "Version remota inconsistente. API=$apiVersion Function=$functionVersion."
}

$result = [pscustomobject]@{
    Environment = $Environment
    Release = $ReleaseVersion
    Commit = $manifest.commit
    Database = $configuration.Database
    ApiStatus = $apiStatus
    FunctionStatus = $functionStatus
}
$result | Format-List
if ($env:GITHUB_STEP_SUMMARY) {
    @"
## Auraly $ReleaseVersion desplegado en $Environment

- Commit: ``$($manifest.commit)``
- Database: ``$($configuration.Database)``
- API health: ``$apiStatus``
- Function health: ``$functionStatus``
"@ | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY
}

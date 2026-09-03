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

    [Parameter(Mandatory)]
    [ValidateSet('database', 'function', 'api', 'pos-installer')]
    [string[]]$Components,

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
        AppConfiguration = 'cfg-auraly-dev-w5usmo6w'
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
        AppConfiguration = 'cfg-auraly-prod-7sov4nxc'
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

function Set-FunctionKeyWithRetry {
    param(
        [Parameter(Mandatory)][string]$FunctionName,
        [Parameter(Mandatory)][string]$KeyName,
        [Parameter(Mandatory)][string]$KeyValue,
        [int]$Attempts = 18,
        [int]$DelaySeconds = 10
    )
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        $null = & az functionapp function keys set `
            --resource-group $configuration.ResourceGroup `
            --name $configuration.Function `
            --function-name $FunctionName `
            --key-name $KeyName `
            --key-value $KeyValue `
            --only-show-errors `
            --output none 2>&1
        if ($LASTEXITCODE -eq 0) { return }
        if ($attempt -eq $Attempts) {
            throw "No se pudo configurar la Function key '$KeyName' de '$FunctionName' después de $Attempts intentos."
        }
        Write-Warning "La Function todavía no expone '$FunctionName'; reintento $attempt de $Attempts."
        Start-Sleep -Seconds $DelaySeconds
    }
}

function Set-AppConfigurationValueWithRetry {
    param(
        [Parameter(Mandatory)][string]$Key,
        [Parameter(Mandatory)][string]$Value,
        [int]$Attempts = 8,
        [int]$DelaySeconds = 10
    )
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        $null = & az appconfig kv set `
            --name $configuration.AppConfiguration `
            --key $Key `
            --value $Value `
            --content-type 'text/plain' `
            --auth-mode login `
            --yes `
            --only-show-errors `
            --output none 2>&1
        if ($LASTEXITCODE -eq 0) { return }

        # A data-plane timeout can happen after the value was committed. Read
        # it back before retrying so the operation stays idempotent and the
        # secret is never written to the log.
        $currentValue = "$(& az appconfig kv show `
            --name $configuration.AppConfiguration `
            --key $Key `
            --auth-mode login `
            --query value `
            --output tsv 2>$null)".Trim()
        if ($LASTEXITCODE -eq 0 -and
            [string]::Equals($currentValue, $Value, [StringComparison]::Ordinal)) {
            return
        }
        if ($attempt -eq $Attempts) {
            throw "No se pudo confirmar '$Key' en Azure App Configuration después de $Attempts intentos."
        }
        Write-Warning "App Configuration no confirmó '$Key'; reintento $attempt de $Attempts."
        Start-Sleep -Seconds $DelaySeconds
    }
}

function Sync-AndAssertFunctionTriggers {
    param(
        [Parameter(Mandatory)][string]$PackagePath,
        [int]$Attempts = 18,
        [int]$DelaySeconds = 10
    )
    $subscriptionId = "$(& az account show --query id --output tsv)".Trim()
    Assert-LastExitCode 'No se pudo resolver la suscripción para sincronizar los triggers'
    if ([string]::IsNullOrWhiteSpace($subscriptionId)) {
        throw 'Azure no devolvió la suscripción para sincronizar los triggers.'
    }

    $syncUri = 'https://management.azure.com/subscriptions/{0}/resourceGroups/{1}/providers/Microsoft.Web/sites/{2}/syncfunctiontriggers?api-version=2024-04-01' -f `
        [Uri]::EscapeDataString($subscriptionId),
        [Uri]::EscapeDataString($configuration.ResourceGroup),
        [Uri]::EscapeDataString($configuration.Function)
    $archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $metadataEntry = $archive.GetEntry('functions.metadata')
        if ($null -eq $metadataEntry) {
            throw 'El paquete Function no contiene functions.metadata.'
        }
        $reader = [IO.StreamReader]::new($metadataEntry.Open())
        try {
            $requiredTriggers = @(
                ($reader.ReadToEnd() | ConvertFrom-Json) |
                    ForEach-Object { "$($_.name)" } |
                    Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }
    if ($requiredTriggers.Count -eq 0) {
        throw 'El paquete Function no declaró triggers para verificar.'
    }

    $missing = @($requiredTriggers)
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        $null = & az rest --method post --url $syncUri --only-show-errors --output none 2>&1
        if ($LASTEXITCODE -eq 0) {
            $registered = @(& az functionapp function list `
                --resource-group $configuration.ResourceGroup `
                --name $configuration.Function `
                --query '[].name' `
                --output tsv 2>$null | ForEach-Object { ($_ -split '/')[-1] })
            if ($LASTEXITCODE -eq 0) {
                $missing = @($requiredTriggers | Where-Object { $_ -notin $registered })
                if ($missing.Count -eq 0) { return }
            }
        }
        if ($attempt -eq $Attempts) {
            throw "Function no registró todos los triggers requeridos: $($missing -join ', ')."
        }
        Write-Warning "Function todavía no registra todos sus triggers; reintento $attempt de $Attempts."
        Start-Sleep -Seconds $DelaySeconds
    }
}

function Sync-FunctionRuntimeSettingsFromApi {
    $requiredNames = @(
        'Auraly__Accounting__ServiceBus__QueueName',
        'Auraly__Fiscal__ServiceBus__QueueName',
        'Auraly__SalesReporting__ServiceBus__QueueName'
    )
    $apiSettings = Invoke-AzJson -Arguments @(
        'webapp', 'config', 'appsettings', 'list',
        '--resource-group', $configuration.ResourceGroup,
        '--name', $configuration.Api,
        '--output', 'json'
    )
    $settings = @("Release__Version=$ReleaseVersion")
    foreach ($name in $requiredNames) {
        $value = @($apiSettings | Where-Object name -EQ $name)[0].value
        if ([string]::IsNullOrWhiteSpace("$value")) {
            throw "La API no contiene la configuración canónica '$name' requerida por Function."
        }
        $settings += "$name=$value"
    }
    $arguments = @(
        'functionapp', 'config', 'appsettings', 'set',
        '--resource-group', $configuration.ResourceGroup,
        '--name', $configuration.Function,
        '--settings'
    ) + $settings + @('--output', 'none')
    $null = & az @arguments
    Assert-LastExitCode 'No se pudo sincronizar la configuración de procesamiento en Function'
}

function Assert-OfflineLeaseSigningConfiguration {
    $settings = & az webapp config appsettings list `
        --resource-group $configuration.ResourceGroup `
        --name $configuration.Api `
        --query "[?name=='Authentication__OfflineLeaseSigning__KeyId' || name=='Authentication__OfflineLeaseSigning__PrivateKeyPem'].{Name:name,Value:value}" `
        --output json | ConvertFrom-Json
    Assert-LastExitCode 'No se pudo inspeccionar la firma del acceso sin conexión'
    $keyId = @($settings | Where-Object Name -EQ 'Authentication__OfflineLeaseSigning__KeyId')[0].Value
    $privateKeyPem = @($settings | Where-Object Name -EQ 'Authentication__OfflineLeaseSigning__PrivateKeyPem')[0].Value
    if ([string]::IsNullOrWhiteSpace($keyId) -or [string]::IsNullOrWhiteSpace($privateKeyPem)) {
        throw 'La firma del acceso sin conexión no está configurada en la API.'
    }
    $rsa = [Security.Cryptography.RSA]::Create()
    try {
        $rsa.ImportFromPem($privateKeyPem)
        if ($rsa.KeySize -lt 2048) {
            throw 'La clave de firma del acceso sin conexión debe ser RSA de al menos 2048 bits.'
        }
    }
    catch {
        throw "La firma del acceso sin conexión no contiene una clave PEM privada completa: $($_.Exception.Message)"
    }
    finally {
        $rsa.Dispose()
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
        "auraly-pos-$ReleaseVersion.exe",
        "auraly-pos-prod-$ReleaseVersion.exe")) {
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
            -MigrationPath (Join-Path $repoRoot 'database/Auraly.Database/Scripts/Migrations/20260817_NormalizeAuralyPlatformTenantKey.sql') `
            -AccessToken $accessToken
        Invoke-ReviewedPreDacpacMigration `
            -MigrationPath (Join-Path $repoRoot 'database/Auraly.Database/Scripts/Migrations/20260823_MoveBusinessLogoToTenant.sql') `
            -AccessToken $accessToken
        Invoke-ReviewedPreDacpacMigration `
            -MigrationPath (Join-Path $repoRoot 'database/Auraly.Database/Scripts/Migrations/20260824_MoveFiscalCredentialsToTenant.sql') `
            -AccessToken $accessToken
        Invoke-ReviewedPreDacpacMigration `
            -MigrationPath (Join-Path $repoRoot 'database/Auraly.Database/Scripts/Migrations/20260825_AddPurchaseEvidence.sql') `
            -AccessToken $accessToken
        Invoke-ReviewedPreDacpacMigration `
            -MigrationPath (Join-Path $repoRoot 'database/Auraly.Database/Scripts/Migrations/20260828_RemoveProductsUnitPrice.sql') `
            -AccessToken $accessToken
        Invoke-ReviewedPreDacpacMigration `
            -MigrationPath (Join-Path $repoRoot 'database/Auraly.Database/Scripts/Migrations/20260829_BackfillProductTenant.sql') `
            -AccessToken $accessToken
        Invoke-ReviewedPreDacpacMigration `
            -MigrationPath (Join-Path $repoRoot 'database/Auraly.Database/Scripts/Migrations/20260829_RemoveFiscalSeriesAllocationState.sql') `
            -AccessToken $accessToken
        Invoke-ReviewedPreDacpacMigration `
            -MigrationPath (Join-Path $repoRoot 'database/Auraly.Database/Scripts/Migrations/20260902_ScopeWorkSessionsByTenant.sql') `
            -AccessToken $accessToken

        $whatsAppAccessTokenArgument = if ([string]::IsNullOrWhiteSpace($env:CJ_WHATSAPP_ACCESS_TOKEN)) {
            '/v:CJWhatsAppAccessToken=""'
        }
        else {
            "/v:CJWhatsAppAccessToken=$($env:CJ_WHATSAPP_ACCESS_TOKEN)"
        }
        $bootstrapAdminPasswordHashArgument = if ([string]::IsNullOrWhiteSpace($env:AURALY_BOOTSTRAP_ADMIN_PASSWORD_HASH)) {
            '/v:BootstrapAdminPasswordHash=""'
        }
        else {
            "/v:BootstrapAdminPasswordHash=$($env:AURALY_BOOTSTRAP_ADMIN_PASSWORD_HASH)"
        }

        $commonArguments = @(
            "/SourceFile:$dacpac",
            "/TargetServerName:tcp:$($configuration.SqlServer).database.windows.net,1433",
            "/TargetDatabaseName:$($configuration.Database)",
            "/AccessToken:$accessToken",
            "/v:DeploymentEnvironment=$Environment",
            $bootstrapAdminPasswordHashArgument,
            $whatsAppAccessTokenArgument,
            '/p:BlockOnPossibleDataLoss=True',
            '/p:DropObjectsNotInSource=False',
            '/p:IgnoreWithNocheckOnForeignKeys=True',
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

        $verificationConnection = [System.Data.SqlClient.SqlConnection]::new(
            "Server=tcp:$($configuration.SqlServer).database.windows.net,1433;" +
            "Initial Catalog=$($configuration.Database);Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;")
        $verificationConnection.AccessToken = $accessToken
        try {
            $verificationConnection.Open()
            $verificationCommand = $verificationConnection.CreateCommand()
            $verificationCommand.CommandTimeout = 60
            $verificationCommand.CommandText = @'
SELECT
  CASE WHEN tenantValue.IsActive=1 THEN 1 ELSE 0 END,
  CASE WHEN roleValue.IsActive=1 AND roleValue.IsSystemRole=1 THEN 1 ELSE 0 END,
  (SELECT COUNT(*) FROM dbo.Permissions),
  (SELECT COUNT(*) FROM dbo.RolePermissions assignment WHERE assignment.RoleId=roleValue.RoleId),
  (SELECT COUNT(*) FROM dbo.AppUsers obsoleteUser
   WHERE obsoleteUser.IsActive=1
     AND (obsoleteUser.NormalizedUsername=N'ADMIN2222'
       OR (obsoleteUser.TenantId=tenantValue.TenantId
         AND obsoleteUser.NormalizedUsername=N'ADMIN'
         AND obsoleteUser.NormalizedEmail=N'ADMIN@AURALY.AI'))),
  (SELECT COUNT(*) FROM dbo.UserRoles obsoleteAssignment
   JOIN dbo.AppUsers obsoleteUser ON obsoleteUser.UserId=obsoleteAssignment.UserId
   WHERE obsoleteUser.NormalizedUsername=N'ADMIN2222'
      OR (obsoleteUser.TenantId=tenantValue.TenantId
        AND obsoleteUser.NormalizedUsername=N'ADMIN'
        AND obsoleteUser.NormalizedEmail=N'ADMIN@AURALY.AI'))
FROM dbo.Tenants tenantValue
JOIN dbo.AppRoles roleValue ON roleValue.TenantId=tenantValue.TenantId AND roleValue.NormalizedName=N'ADMINISTRATOR'
WHERE tenantValue.TenantKey=N'@auraly';
'@
            $reader = $verificationCommand.ExecuteReader()
            try {
                if (-not $reader.Read()) { throw 'No existe el tenant canonico @auraly con su rol administrador de plataforma.' }
                $tenantActive = $reader.GetInt32(0) -eq 1
                $roleActive = $reader.GetInt32(1) -eq 1
                $permissionCount = $reader.GetInt32(2)
                $assignedPermissionCount = $reader.GetInt32(3)
                $activeObsoleteUsers = $reader.GetInt32(4)
                $obsoleteAssignments = $reader.GetInt32(5)
                if (-not $tenantActive -or -not $roleActive -or
                    $permissionCount -ne $assignedPermissionCount -or
                    $activeObsoleteUsers -ne 0 -or $obsoleteAssignments -ne 0) {
                    throw "El rol administrador @auraly no quedo aprovisionado correctamente. Tenant=$tenantActive Role=$roleActive Permissions=$assignedPermissionCount/$permissionCount ActiveObsoleteUsers=$activeObsoleteUsers ObsoleteAssignments=$obsoleteAssignments."
                }
                Write-Information "Rol administrador @auraly verificado con $assignedPermissionCount permisos y sin identidades tecnicas activas." -InformationAction Continue
            }
            finally { $reader.Dispose() }
        }
        finally { $verificationConnection.Dispose() }
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
    $parameterFile = $null
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

        $parameterFile = [IO.Path]::GetTempFileName()
        @{
            '$schema' = 'https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#'
            contentVersion = '1.0.0.0'
            parameters = @{
                functionAppName = @{ value = $configuration.Function }
                location = @{ value = 'eastus2' }
                packageUri = @{ value = $packageUri }
            }
        } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $parameterFile -Encoding utf8NoBOM

        & az deployment group create `
            --name "auraly-$Environment-function-$ReleaseVersion" `
            --resource-group $configuration.ResourceGroup `
            --template-file (Join-Path $PSScriptRoot 'function-onedeploy-v2.bicep') `
            --parameters "@$parameterFile" `
            --output none
        Assert-LastExitCode 'OneDeploy de Function fallo'
        Sync-FunctionRuntimeSettingsFromApi
        Sync-AndAssertFunctionTriggers -PackagePath $zip

        if ($Environment -eq 'dev' -and
            -not [string]::IsNullOrWhiteSpace($env:CJ_WHATSAPP_FUNCTION_KEY) -and
            -not [string]::IsNullOrWhiteSpace($env:CJ_WHATSAPP_VERIFY_TOKEN)) {
            Set-FunctionKeyWithRetry `
                -FunctionName 'WhatsAppWebhook' `
                -KeyName 'meta-cj' `
                -KeyValue $env:CJ_WHATSAPP_FUNCTION_KEY
            & az functionapp config appsettings set `
                --resource-group $configuration.ResourceGroup `
                --name $configuration.Function `
                --settings "WhatsApp__Webhook__VerifyToken=$($env:CJ_WHATSAPP_VERIFY_TOKEN)" `
                --output none
            Assert-LastExitCode 'No se pudo configurar el verify token de Meta para CJ'
            Set-AppConfigurationValueWithRetry `
                -Key 'WhatsApp:Webhook:ApiBaseUrl' `
                -Value 'https://graph.facebook.com/v25.0/'
            Set-AppConfigurationValueWithRetry `
                -Key 'WhatsApp:Webhook:VerifyToken' `
                -Value $env:CJ_WHATSAPP_VERIFY_TOKEN
            # El cambio de app settings reinicia el host; se vuelve a registrar
            # el mismo paquete canónico y se verifica antes de continuar.
            Sync-AndAssertFunctionTriggers -PackagePath $zip
        }
    }
    finally {
        $packageUri = $null
        if ($parameterFile -and (Test-Path -LiteralPath $parameterFile)) {
            Remove-Item -LiteralPath $parameterFile -Force
        }
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
    $installerName = if ($Environment -eq 'prod') {
        "auraly-pos-prod-$ReleaseVersion.exe"
    } else {
        "auraly-pos-$ReleaseVersion.exe"
    }
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
$selectedComponents = @($Components | ForEach-Object { $_.Trim().ToLowerInvariant() } | Sort-Object -Unique)
$recordedComponents = @(
    $manifest.deploymentComponents |
        ForEach-Object { "$_".Trim().ToLowerInvariant() } |
        Where-Object { $_ -in @('database', 'function', 'api', 'pos-installer') } |
        Sort-Object -Unique)
if ($recordedComponents.Count -gt 0 -and
    (Compare-Object -ReferenceObject $recordedComponents -DifferenceObject $selectedComponents)) {
    throw 'El alcance solicitado no coincide con los componentes archivados en el release.'
}

if ($selectedComponents -contains 'database') {
    Publish-Database
    & (Join-Path $PSScriptRoot 'Sync-AuralySqlFirewall.ps1') -Environment $Environment
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo habilitar el acceso administrado del runtime a Azure SQL.' }
}
if ($selectedComponents -contains 'function') {
    Assert-OfflineLeaseSigningConfiguration
    Publish-Function
}
if ($selectedComponents -contains 'api') { Publish-Api }
$installerMetadata = if ($selectedComponents -contains 'pos-installer') { Publish-PosInstallerIfPresent } else { $null }

$apiStatus = if ($selectedComponents -contains 'api' -or $selectedComponents -contains 'pos-installer') {
    Wait-HttpHealthy "https://$($configuration.Api).azurewebsites.net/health"
}
else { $null }
$functionStatus = if ($selectedComponents -contains 'function') {
    Wait-HttpHealthy "https://$($configuration.Function).azurewebsites.net/api/health"
}
else { $null }

if ($selectedComponents -contains 'api') {
    $apiVersion = (& az webapp config appsettings list `
        --resource-group $configuration.ResourceGroup `
        --name $configuration.Api `
        --query "[?name=='Release__Version'].value | [0]" `
        --output tsv).Trim()
    Assert-LastExitCode 'No se pudo validar la version de API'
    if ($apiVersion -ne $ReleaseVersion) { throw "Version remota de API inconsistente: $apiVersion." }
}
if ($selectedComponents -contains 'function') {
    $functionVersion = (& az functionapp config appsettings list `
        --resource-group $configuration.ResourceGroup `
        --name $configuration.Function `
        --query "[?name=='Release__Version'].value | [0]" `
        --output tsv).Trim()
    Assert-LastExitCode 'No se pudo validar la version de Function'
    if ($functionVersion -ne $ReleaseVersion) { throw "Version remota de Function inconsistente: $functionVersion." }
}
if ($selectedComponents -contains 'pos-installer') {
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
    if ($installerVersion -ne $ReleaseVersion -or $installerSha256 -ne "$($installerMetadata.sha256)") {
        throw "Version remota del instalador inconsistente: $installerVersion."
    }
}

$result = [pscustomobject]@{
    Environment = $Environment
    Release = $ReleaseVersion
    Commit = $manifest.commit
    Components = $selectedComponents -join ','
    Database = if ($selectedComponents -contains 'database') { $configuration.Database } else { $null }
    ApiStatus = $apiStatus
    FunctionStatus = $functionStatus
}
$result | Format-List
if ($env:GITHUB_STEP_SUMMARY) {
    @"
## Auraly $ReleaseVersion desplegado en $Environment

- Commit: ``$($manifest.commit)``
- Components: ``$($selectedComponents -join ',')``
- Database: ``$(if ($selectedComponents -contains 'database') { $configuration.Database } else { 'sin cambios' })``
- API health: ``$apiStatus``
- Function health: ``$functionStatus``
"@ | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY
}

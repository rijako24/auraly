#Requires -Version 7.2
#Requires -Modules SqlServer

[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidateSet('dev', 'prod')]
    [string]$Environment,

    [string]$Repository = 'rijako24/auraly',

    [string]$SubscriptionId = '5ea009ce-23c5-4bbd-b1c8-62116d58f596'
)

$ErrorActionPreference = 'Stop'
$suffix = if ($Environment -eq 'dev') { 'w5usmo6w' } else { '7sov4nxc' }
$resourceGroup = "RG-AURALY-$($Environment.ToUpperInvariant())"
$storage = "stauraly$Environment$suffix"
$releaseStorage = 'stauralydevw5usmo6w'
$sqlServer = "sql-auraly-$Environment-$suffix"
$database = "auraly-$Environment"
$admin = "admin-auraly-$Environment-$suffix"
$identityName = "id-auraly-github-$Environment"
$federatedCredentialName = "github-$Environment"
$subject = "repo:${Repository}:environment:$Environment"
$firewallRule = "github-bootstrap-$([guid]::NewGuid().ToString('N').Substring(0, 10))"

function Assert-LastExitCode {
    param([Parameter(Mandatory)][string]$Message)
    if ($LASTEXITCODE -ne 0) { throw "$Message (codigo $LASTEXITCODE)." }
}

& az account set --subscription $SubscriptionId
Assert-LastExitCode 'No se pudo seleccionar la suscripcion Azure'

$identityJson = & az identity show `
    --resource-group $resourceGroup `
    --name $identityName `
    --output json 2>$null
if ($LASTEXITCODE -ne 0) {
    if (-not $PSCmdlet.ShouldProcess($resourceGroup, "Crear $identityName")) { return }
    $identityJson = & az identity create `
        --resource-group $resourceGroup `
        --name $identityName `
        --location eastus2 `
        --tags application=auraly environment=$Environment managedBy=github-oidc `
        --output json
    Assert-LastExitCode 'No se pudo crear la identidad OIDC'
}
$identity = $identityJson | ConvertFrom-Json

& az identity federated-credential show `
    --resource-group $resourceGroup `
    --identity-name $identityName `
    --name $federatedCredentialName `
    --output none 2>$null
if ($LASTEXITCODE -ne 0) {
    & az identity federated-credential create `
        --resource-group $resourceGroup `
        --identity-name $identityName `
        --name $federatedCredentialName `
        --issuer 'https://token.actions.githubusercontent.com' `
        --subject $subject `
        --audiences 'api://AzureADTokenExchange' `
        --output none
    Assert-LastExitCode 'No se pudo crear la credencial federada de GitHub'
}

$resourceGroupScope = (& az group show --name $resourceGroup --query id --output tsv).Trim()
Assert-LastExitCode 'No se pudo resolver el resource group'
$storageScope = (& az storage account show `
    --resource-group $resourceGroup `
    --name $storage `
    --query id `
    --output tsv).Trim()
Assert-LastExitCode 'No se pudo resolver el storage'
$releaseStorageScope = (& az storage account show `
    --resource-group 'RG-AURALY-DEV' `
    --name $releaseStorage `
    --query id `
    --output tsv).Trim()
Assert-LastExitCode 'No se pudo resolver el storage privado de releases'

$assignments = @(
    @{ Role = 'Contributor'; Scope = $resourceGroupScope },
    @{ Role = 'Storage Blob Data Contributor'; Scope = $storageScope })
if ($Environment -eq 'prod') {
    $assignments += @{ Role = 'Storage Blob Data Reader'; Scope = $releaseStorageScope }
}
foreach ($assignment in $assignments) {
    $existingOutput = & az role assignment list `
        --assignee $identity.principalId `
        --role $assignment.Role `
        --scope $assignment.Scope `
        --query '[0].id' `
        --output tsv
    Assert-LastExitCode "No se pudo consultar el rol $($assignment.Role)"
    $existing = "$existingOutput".Trim()
    if (-not $existing) {
        & az role assignment create `
            --assignee-object-id $identity.principalId `
            --assignee-principal-type ServicePrincipal `
            --role $assignment.Role `
            --scope $assignment.Scope `
            --output none
        Assert-LastExitCode "No se pudo asignar el rol $($assignment.Role)"
    }
}

$publicIp = (Invoke-RestMethod -Uri 'https://api.ipify.org').Trim()
$parsedIp = $null
if (-not [Net.IPAddress]::TryParse($publicIp, [ref]$parsedIp)) {
    throw 'No se pudo determinar una IP publica valida para preparar Azure SQL.'
}
$sqlAccessToken = $null
try {
    & az sql server firewall-rule create `
        --resource-group $resourceGroup `
        --server $sqlServer `
        --name $firewallRule `
        --start-ip-address $publicIp `
        --end-ip-address $publicIp `
        --output none
    Assert-LastExitCode 'No se pudo crear la regla SQL temporal'
    $sqlAccessToken = (& az account get-access-token `
        --resource 'https://database.windows.net/' `
        --query accessToken `
        --output tsv).Trim()
    Assert-LastExitCode 'No se pudo obtener el token SQL del administrador Entra'
    if (-not $sqlAccessToken) { throw 'Azure devolvio un token SQL vacio.' }

    $sidBytes = ([guid]$identity.clientId).ToByteArray()
    $sidHex = '0x' + (($sidBytes | ForEach-Object { $_.ToString('X2') }) -join '')
    $escapedIdentityName = $identityName.Replace(']', ']]')
    $query = @"
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$escapedIdentityName')
    CREATE USER [$escapedIdentityName] WITH SID = $sidHex, TYPE = E;
ALTER ROLE db_owner ADD MEMBER [$escapedIdentityName];
"@
    Invoke-Sqlcmd `
        -ServerInstance "$sqlServer.database.windows.net" `
        -Database $database `
        -AccessToken $sqlAccessToken `
        -Query $query `
        -AbortOnError `
        -ConnectionTimeout 30 `
        -QueryTimeout 120
}
finally {
    $sqlAccessToken = $null
    & az sql server firewall-rule delete `
        --resource-group $resourceGroup `
        --server $sqlServer `
        --name $firewallRule `
        --output none 2>$null
}

& gh api --method PUT "repos/$Repository/environments/$Environment" --silent
Assert-LastExitCode 'No se pudo crear el GitHub Environment'
$tenantId = (& az account show --query tenantId --output tsv).Trim()
$adminHost = (& az staticwebapp show `
    --resource-group $resourceGroup `
    --name $admin `
    --query defaultHostname `
    --output tsv).Trim()
foreach ($variable in @(
    @{ Name = 'AZURE_CLIENT_ID'; Value = $identity.clientId },
    @{ Name = 'AZURE_TENANT_ID'; Value = $tenantId },
    @{ Name = 'AZURE_SUBSCRIPTION_ID'; Value = $SubscriptionId },
    @{ Name = 'AURALY_ADMIN_HOST'; Value = $adminHost })) {
    & gh variable set $variable.Name `
        --repo $Repository `
        --env $Environment `
        --body $variable.Value
    Assert-LastExitCode "No se pudo configurar $($variable.Name) en GitHub"
}

[pscustomobject]@{
    Environment = $Environment
    Identity = $identityName
    ClientId = $identity.clientId
    GitHubSubject = $subject
    ResourceGroupRole = 'Contributor'
    StorageRole = 'Storage Blob Data Contributor'
    ReleaseArchiveRole = if ($Environment -eq 'prod') { 'Storage Blob Data Reader' } else { 'Storage Blob Data Contributor' }
    DatabaseRole = 'db_owner'
    GitHubEnvironment = $Environment
}

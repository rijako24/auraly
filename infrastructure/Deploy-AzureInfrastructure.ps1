#Requires -Version 7.0
#Requires -Modules Az.Accounts, Az.Resources, Az.Storage, Az.Sql, Az.Functions, Az.CognitiveServices

<#
.SYNOPSIS
    Despliega toda la infraestructura de Azure para Mimos Baby Spa

.DESCRIPTION
    Este script crea y configura todos los recursos necesarios en Azure:
    - Resource Group
    - Storage Account (para Function App y Blob Storage)
    - SQL Server y Base de Datos
    - Azure OpenAI
    - Function App con todas las configuraciones
    - Application Insights (opcional)

.PARAMETER SubscriptionId
    ID de la suscripción de Azure

.PARAMETER ResourceGroupName
    Nombre del grupo de recursos (default: MimosBabySpa-RG)

.PARAMETER Location
    Región de Azure (default: eastus)

.PARAMETER Environment
    Ambiente (dev, staging, prod) - afecta nombres y configuraciones (default: dev)

.PARAMETER SqlAdminUsername
    Usuario administrador de SQL Server

.PARAMETER SqlAdminPassword
    Contraseña del administrador de SQL Server (debe cumplir requisitos de complejidad)

.PARAMETER OpenAIModelDeploymentName
    Nombre del deployment del modelo OpenAI (default: gpt-4)

.PARAMETER WhatsAppPhoneNumberId
    Phone Number ID de WhatsApp Business API

.PARAMETER WhatsAppAccessToken
    Access Token de WhatsApp Business API

.PARAMETER FunctionAppName
    Nombre de la Function App (se generará automáticamente si no se especifica)

.PARAMETER EnableApplicationInsights
    Habilita Application Insights (default: $true)

.PARAMETER SkipDatabaseDeployment
    Omite el despliegue de la base de datos (útil si ya existe)

.EXAMPLE
    .\Deploy-AzureInfrastructure.ps1 `
        -SubscriptionId "12345678-1234-1234-1234-123456789012" `
        -SqlAdminUsername "sqladmin" `
        -SqlAdminPassword "SecurePassword123!" `
        -WhatsAppPhoneNumberId "123456789012345" `
        -WhatsAppAccessToken "EAAxxxxxxxxxxxxx"

.NOTES
    Requisitos:
    - Azure CLI o Azure PowerShell instalado
    - Permisos de Contributor en la suscripción
    - WhatsApp Business API configurada en Meta
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory=$true)]
    [string]$SubscriptionId,
    
    [Parameter(Mandatory=$false)]
    [string]$ResourceGroupName = "MimosBabySpa-RG",
    
    [Parameter(Mandatory=$false)]
    [ValidateSet("eastus", "eastus2", "westus", "westus2", "westeurope", "northeurope", "southeastasia", "japaneast", "brazilsouth", "australiaeast", "centralus", "southcentralus")]
    [string]$Location = "eastus",
    
    [Parameter(Mandatory=$false)]
    [ValidateSet("dev", "staging", "prod")]
    [string]$Environment = "dev",
    
    [Parameter(Mandatory=$true)]
    [string]$SqlAdminUsername,
    
    [Parameter(Mandatory=$true)]
    [SecureString]$SqlAdminPassword,
    
    [Parameter(Mandatory=$false)]
    [string]$OpenAIModelDeploymentName = "gpt-4",
    
    [Parameter(Mandatory=$true)]
    [string]$WhatsAppPhoneNumberId,
    
    [Parameter(Mandatory=$true)]
    [SecureString]$WhatsAppAccessToken,
    
    [Parameter(Mandatory=$false)]
    [string]$FunctionAppName = "",
    
    [Parameter(Mandatory=$false)]
    [switch]$EnableApplicationInsights = $true,
    
    [Parameter(Mandatory=$false)]
    [switch]$SkipDatabaseDeployment = $false
)

$ErrorActionPreference = "Stop"

# Convertir SecureString a String para uso interno
$BSTR = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($SqlAdminPassword)
$SqlAdminPasswordPlain = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($BSTR)
[System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($BSTR)

$BSTR = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($WhatsAppAccessToken)
$WhatsAppAccessTokenPlain = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($BSTR)
[System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($BSTR)

# Naming conventions
$timestamp = Get-Date -Format "yyyyMMdd"
$uniqueSuffix = (New-Guid).ToString().Substring(0, 8)
$envSuffix = if ($Environment -ne "prod") { "-$Environment" } else { "" }

$storageAccountName = "mimosbabyspa$($Environment)stg$uniqueSuffix".ToLower().Substring(0, 24)
$sqlServerName = "mimosbabyspa-sql-$Environment-$uniqueSuffix".ToLower().Substring(0, 63)
$sqlDatabaseName = "MimosBabySpa$envSuffix"
$functionAppName = if ([string]::IsNullOrEmpty($FunctionAppName)) { 
    "mimosbabyspa-func-$Environment-$uniqueSuffix".ToLower().Substring(0, 60) 
} else { 
    $FunctionAppName.ToLower() 
}
$openAIServiceName = "mimosbabyspa-openai-$Environment-$uniqueSuffix".ToLower().Substring(0, 60)
$appInsightsName = "mimosbabyspa-ai-$Environment-$uniqueSuffix".ToLower().Substring(0, 60)
$appServicePlanName = "mimosbabyspa-plan-$Environment-$uniqueSuffix".ToLower().Substring(0, 60)

# Tags para recursos
$tags = @{
    "Environment" = $Environment
    "Project" = "MimosBabySpa"
    "ManagedBy" = "InfrastructureAsCode"
    "CreatedDate" = $timestamp
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Despliegue de Infraestructura Azure" -ForegroundColor Cyan
Write-Host "  Mimos Baby Spa" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Configuración:" -ForegroundColor Yellow
Write-Host "  Subscription ID: $SubscriptionId" -ForegroundColor Gray
Write-Host "  Resource Group: $ResourceGroupName" -ForegroundColor Gray
Write-Host "  Location: $Location" -ForegroundColor Gray
Write-Host "  Environment: $Environment" -ForegroundColor Gray
Write-Host ""
Write-Host "Recursos a crear:" -ForegroundColor Yellow
Write-Host "  Storage Account: $storageAccountName" -ForegroundColor Gray
Write-Host "  SQL Server: $sqlServerName" -ForegroundColor Gray
Write-Host "  SQL Database: $sqlDatabaseName" -ForegroundColor Gray
Write-Host "  Function App: $functionAppName" -ForegroundColor Gray
Write-Host "  OpenAI Service: $openAIServiceName" -ForegroundColor Gray
if ($EnableApplicationInsights) {
    Write-Host "  Application Insights: $appInsightsName" -ForegroundColor Gray
}
Write-Host ""

if (-not $PSCmdlet.ShouldProcess("Azure Subscription $SubscriptionId", "Crear infraestructura")) {
    Write-Host "Operación cancelada por el usuario." -ForegroundColor Yellow
    exit 0
}

try {
    # Paso 1: Login y configuración de suscripción
    Write-Host "[1/10] Configurando suscripción de Azure..." -ForegroundColor Yellow
    $context = Get-AzContext
    if ($null -eq $context -or $context.Subscription.Id -ne $SubscriptionId) {
        Write-Host "Iniciando sesión en Azure..." -ForegroundColor Cyan
        Connect-AzAccount -SubscriptionId $SubscriptionId | Out-Null
    } else {
        Write-Host "Ya estás autenticado en Azure." -ForegroundColor Green
    }
    Set-AzContext -SubscriptionId $SubscriptionId | Out-Null
    Write-Host "✓ Suscripción configurada" -ForegroundColor Green
    Write-Host ""

    # Paso 2: Crear Resource Group
    Write-Host "[2/10] Creando Resource Group..." -ForegroundColor Yellow
    $rg = Get-AzResourceGroup -Name $ResourceGroupName -ErrorAction SilentlyContinue
    if ($null -eq $rg) {
        $rg = New-AzResourceGroup -Name $ResourceGroupName -Location $Location -Tag $tags
        Write-Host "✓ Resource Group creado: $ResourceGroupName" -ForegroundColor Green
    } else {
        Write-Host "✓ Resource Group ya existe: $ResourceGroupName" -ForegroundColor Green
    }
    Write-Host ""

    # Paso 3: Crear Storage Account
    Write-Host "[3/10] Creando Storage Account..." -ForegroundColor Yellow
    $storageAccount = Get-AzStorageAccount -ResourceGroupName $ResourceGroupName -Name $storageAccountName -ErrorAction SilentlyContinue
    if ($null -eq $storageAccount) {
        $storageAccount = New-AzStorageAccount `
            -ResourceGroupName $ResourceGroupName `
            -Name $storageAccountName `
            -Location $Location `
            -SkuName Standard_LRS `
            -Kind StorageV2 `
            -AccessTier Hot `
            -EnableHttpsTrafficOnly $true `
            -MinimumTlsVersion TLS1_2 `
            -Tag $tags
        
        Write-Host "✓ Storage Account creado: $storageAccountName" -ForegroundColor Green
    } else {
        Write-Host "✓ Storage Account ya existe: $storageAccountName" -ForegroundColor Green
    }
    
    # Obtener connection string del storage
    $storageKeys = Get-AzStorageAccountKey -ResourceGroupName $ResourceGroupName -Name $storageAccountName
    $storageConnectionString = "DefaultEndpointsProtocol=https;AccountName=$storageAccountName;AccountKey=$($storageKeys[0].Value);EndpointSuffix=core.windows.net"
    Write-Host ""

    # Paso 4: Crear contenedor de Blob Storage
    Write-Host "[4/10] Creando contenedor de Blob Storage..." -ForegroundColor Yellow
    $storageContext = New-AzStorageContext -StorageAccountName $storageAccountName -StorageAccountKey $storageKeys[0].Value
    $container = Get-AzStorageContainer -Name "planes-images" -Context $storageContext -ErrorAction SilentlyContinue
    if ($null -eq $container) {
        New-AzStorageContainer -Name "planes-images" -Context $storageContext -Permission Blob | Out-Null
        Write-Host "✓ Contenedor 'planes-images' creado" -ForegroundColor Green
    } else {
        Write-Host "✓ Contenedor 'planes-images' ya existe" -ForegroundColor Green
    }
    Write-Host ""

    # Paso 5: Crear Application Insights (si está habilitado)
    $appInsightsInstrumentationKey = ""
    if ($EnableApplicationInsights) {
        Write-Host "[5/10] Creando Application Insights..." -ForegroundColor Yellow
        $appInsights = Get-AzApplicationInsights -ResourceGroupName $ResourceGroupName -Name $appInsightsName -ErrorAction SilentlyContinue
        if ($null -eq $appInsights) {
            $appInsights = New-AzApplicationInsights `
                -ResourceGroupName $ResourceGroupName `
                -Name $appInsightsName `
                -Location $Location `
                -Kind web `
                -Tag $tags
            
            Write-Host "✓ Application Insights creado: $appInsightsName" -ForegroundColor Green
        } else {
            Write-Host "✓ Application Insights ya existe: $appInsightsName" -ForegroundColor Green
        }
        $appInsightsInstrumentationKey = $appInsights.InstrumentationKey
        Write-Host ""
    }

    # Paso 6: Crear SQL Server y Base de Datos
    if (-not $SkipDatabaseDeployment) {
        Write-Host "[6/10] Creando SQL Server y Base de Datos..." -ForegroundColor Yellow
        
        # Crear SQL Server
        $sqlServer = Get-AzSqlServer -ResourceGroupName $ResourceGroupName -ServerName $sqlServerName -ErrorAction SilentlyContinue
        if ($null -eq $sqlServer) {
            $sqlServer = New-AzSqlServer `
                -ResourceGroupName $ResourceGroupName `
                -ServerName $sqlServerName `
                -Location $Location `
                -SqlAdministratorCredentials (New-Object System.Management.Automation.PSCredential($SqlAdminUsername, $SqlAdminPassword)) `
                -ServerVersion "12.0" `
                -Tag $tags
            
            Write-Host "✓ SQL Server creado: $sqlServerName" -ForegroundColor Green
            
            # Configurar firewall para permitir servicios de Azure
            Write-Host "  Configurando firewall de SQL Server..." -ForegroundColor Cyan
            New-AzSqlServerFirewallRule `
                -ResourceGroupName $ResourceGroupName `
                -ServerName $sqlServerName `
                -FirewallRuleName "AllowAzureServices" `
                -StartIpAddress "0.0.0.0" `
                -EndIpAddress "0.0.0.0" `
                -ErrorAction SilentlyContinue | Out-Null
            
            Write-Host "  ✓ Firewall configurado para servicios de Azure" -ForegroundColor Green
        } else {
            Write-Host "✓ SQL Server ya existe: $sqlServerName" -ForegroundColor Green
        }
        
        # Crear Base de Datos
        $sqlDatabase = Get-AzSqlDatabase -ResourceGroupName $ResourceGroupName -ServerName $sqlServerName -DatabaseName $sqlDatabaseName -ErrorAction SilentlyContinue
        if ($null -eq $sqlDatabase) {
            $sqlDatabase = New-AzSqlDatabase `
                -ResourceGroupName $ResourceGroupName `
                -ServerName $sqlServerName `
                -DatabaseName $sqlDatabaseName `
                -Edition "Basic" `
                -RequestedServiceObjectiveName "Basic" `
                -Tag $tags
            
            Write-Host "✓ Base de datos creada: $sqlDatabaseName" -ForegroundColor Green
        } else {
            Write-Host "✓ Base de datos ya existe: $sqlDatabaseName" -ForegroundColor Green
        }
        
        # Construir connection string
        $sqlConnectionString = "Server=tcp:$sqlServerName.database.windows.net,1433;Initial Catalog=$sqlDatabaseName;Persist Security Info=False;User ID=$SqlAdminUsername;Password=$SqlAdminPasswordPlain;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
        Write-Host ""
    } else {
        Write-Host "[6/10] Omitiendo creación de base de datos (SkipDatabaseDeployment)" -ForegroundColor Yellow
        Write-Host ""
    }

    # Paso 7: Crear Azure OpenAI
    Write-Host "[7/10] Creando recurso de Azure OpenAI..." -ForegroundColor Yellow
    
    # Verificar si el proveedor está registrado
    $openAIProvider = Get-AzResourceProvider -ProviderNamespace "Microsoft.CognitiveServices" | Where-Object { $_.RegistrationState -eq "Registered" }
    if ($null -eq $openAIProvider) {
        Write-Host "  Registrando proveedor Microsoft.CognitiveServices..." -ForegroundColor Cyan
        Register-AzResourceProvider -ProviderNamespace "Microsoft.CognitiveServices" | Out-Null
        Start-Sleep -Seconds 10
    }
    
    $openAIService = Get-AzCognitiveServicesAccount -ResourceGroupName $ResourceGroupName -Name $openAIServiceName -ErrorAction SilentlyContinue
    if ($null -eq $openAIService) {
        # Nota: Azure OpenAI requiere aprobación previa. Si falla, el usuario debe crearlo manualmente.
        try {
            $openAIService = New-AzCognitiveServicesAccount `
                -ResourceGroupName $ResourceGroupName `
                -Name $openAIServiceName `
                -Location $Location `
                -SkuName "S0" `
                -Kind "OpenAI" `
                -Tag $tags -ErrorAction Stop
            
            Write-Host "✓ Azure OpenAI creado: $openAIServiceName" -ForegroundColor Green
        } catch {
            Write-Host "⚠ No se pudo crear Azure OpenAI automáticamente." -ForegroundColor Yellow
            Write-Host "  Razón: $($_.Exception.Message)" -ForegroundColor Yellow
            Write-Host "  Azure OpenAI requiere aprobación previa." -ForegroundColor Yellow
            Write-Host "  Por favor, créalo manualmente en Azure Portal y ejecuta este script nuevamente con -SkipOpenAICreation" -ForegroundColor Yellow
            Write-Host ""
            
            $openAIServiceName = Read-Host "Ingresa el nombre del recurso Azure OpenAI existente (o presiona Enter para omitir)"
            if (-not [string]::IsNullOrEmpty($openAIServiceName)) {
                $openAIService = Get-AzCognitiveServicesAccount -ResourceGroupName $ResourceGroupName -Name $openAIServiceName
            }
        }
    } else {
        Write-Host "✓ Azure OpenAI ya existe: $openAIServiceName" -ForegroundColor Green
    }
    
    if ($null -ne $openAIService) {
        $openAIKeys = Get-AzCognitiveServicesAccountKey -ResourceGroupName $ResourceGroupName -Name $openAIServiceName
        $openAIEndpoint = $openAIService.Endpoint
        $openAIKey = $openAIKeys.Key1
    } else {
        Write-Host "⚠ Azure OpenAI no disponible. Debes configurarlo manualmente." -ForegroundColor Yellow
        $openAIEndpoint = Read-Host "Ingresa el endpoint de Azure OpenAI (ej: https://tu-recurso.openai.azure.com/)"
        $openAIKey = Read-Host "Ingresa la API Key de Azure OpenAI" -AsSecureString
        $BSTR = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($openAIKey)
        $openAIKey = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($BSTR)
        [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($BSTR)
    }
    Write-Host ""

    # Paso 8: Crear App Service Plan
    Write-Host "[8/10] Creando App Service Plan..." -ForegroundColor Yellow
    $appServicePlan = Get-AzFunctionAppPlan -ResourceGroupName $ResourceGroupName -Name $appServicePlanName -ErrorAction SilentlyContinue
    if ($null -eq $appServicePlan) {
        $appServicePlan = New-AzFunctionAppPlan `
            -ResourceGroupName $ResourceGroupName `
            -Name $appServicePlanName `
            -Location $Location `
            -Sku "Y1" `
            -Kind "functionapp" `
            -Tag $tags
        
        Write-Host "✓ App Service Plan creado: $appServicePlanName" -ForegroundColor Green
    } else {
        Write-Host "✓ App Service Plan ya existe: $appServicePlanName" -ForegroundColor Green
    }
    Write-Host ""

    # Paso 9: Crear Function App
    Write-Host "[9/10] Creando Function App..." -ForegroundColor Yellow
    $functionApp = Get-AzFunctionApp -ResourceGroupName $ResourceGroupName -Name $functionAppName -ErrorAction SilentlyContinue
    if ($null -eq $functionApp) {
        $functionApp = New-AzFunctionApp `
            -ResourceGroupName $ResourceGroupName `
            -Name $functionAppName `
            -PlanName $appServicePlanName `
            -StorageAccountName $storageAccountName `
            -Runtime "dotnet-isolated" `
            -RuntimeVersion "8" `
            -FunctionsVersion "4" `
            -OSType "Windows" `
            -Tag $tags
        
        Write-Host "✓ Function App creada: $functionAppName" -ForegroundColor Green
    } else {
        Write-Host "✓ Function App ya existe: $functionAppName" -ForegroundColor Green
    }
    Write-Host ""

    # Paso 10: Configurar Application Settings
    Write-Host "[10/10] Configurando Application Settings..." -ForegroundColor Yellow
    
    $appSettings = @{
        "FUNCTIONS_WORKER_RUNTIME" = "dotnet-isolated"
        "WEBSITE_RUN_FROM_PACKAGE" = "1"
        "ConnectionStrings:DefaultConnection" = $sqlConnectionString
        "BlobStorage:ConnectionString" = $storageConnectionString
        "BlobStorage:ContainerName" = "planes-images"
        "OpenAI:ApiKey" = $openAIKey
        "OpenAI:Endpoint" = $openAIEndpoint
        "OpenAI:DeploymentName" = $OpenAIModelDeploymentName
        "WhatsApp:PhoneNumberId" = $WhatsAppPhoneNumberId
        "WhatsApp:AccessToken" = $WhatsAppAccessTokenPlain
    }
    
    if ($EnableApplicationInsights -and -not [string]::IsNullOrEmpty($appInsightsInstrumentationKey)) {
        $appSettings["APPINSIGHTS_INSTRUMENTATIONKEY"] = $appInsightsInstrumentationKey
        $appSettings["APPLICATIONINSIGHTS_CONNECTION_STRING"] = "InstrumentationKey=$appInsightsInstrumentationKey"
    }
    
    Update-AzFunctionAppSetting -ResourceGroupName $ResourceGroupName -Name $functionAppName -AppSetting $appSettings | Out-Null
    
    Write-Host "✓ Application Settings configuradas" -ForegroundColor Green
    Write-Host ""

    # Resumen final
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "  Despliegue Completado Exitosamente!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Resumen de recursos creados:" -ForegroundColor Cyan
    Write-Host "  Resource Group: $ResourceGroupName" -ForegroundColor White
    Write-Host "  Storage Account: $storageAccountName" -ForegroundColor White
    Write-Host "  SQL Server: $sqlServerName.database.windows.net" -ForegroundColor White
    Write-Host "  SQL Database: $sqlDatabaseName" -ForegroundColor White
    Write-Host "  Function App: https://$functionAppName.azurewebsites.net" -ForegroundColor White
    Write-Host "  OpenAI Service: $openAIServiceName" -ForegroundColor White
    if ($EnableApplicationInsights) {
        Write-Host "  Application Insights: $appInsightsName" -ForegroundColor White
    }
    Write-Host ""
    Write-Host "Próximos pasos:" -ForegroundColor Yellow
    Write-Host "  1. Desplegar el esquema de base de datos usando el proyecto en database/" -ForegroundColor White
    Write-Host "  2. Publicar la Function App: func azure functionapp publish $functionAppName" -ForegroundColor White
    Write-Host "  3. Configurar el webhook de WhatsApp con la URL de la Function App" -ForegroundColor White
    Write-Host "  4. Subir las imágenes de planes al contenedor 'planes-images'" -ForegroundColor White
    Write-Host ""
    Write-Host "Para obtener la Function Key del webhook:" -ForegroundColor Yellow
    Write-Host "  az functionapp function keys list --name $functionAppName --resource-group $ResourceGroupName --function-name WhatsAppWebhook" -ForegroundColor Gray
    Write-Host ""

} catch {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "  Error durante el despliegue" -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Red
    Write-Host ""
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    Write-Host "Stack Trace:" -ForegroundColor Yellow
    Write-Host $_.ScriptStackTrace -ForegroundColor Gray
    Write-Host ""
    exit 1
} finally {
    # Limpiar variables sensibles
    $SqlAdminPasswordPlain = $null
    $WhatsAppAccessTokenPlain = $null
    $openAIKey = $null
}

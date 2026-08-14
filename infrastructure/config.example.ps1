# Archivo de configuración de ejemplo
# Copia este archivo a config.ps1 y completa los valores

$Config = @{
    # Azure Subscription
    SubscriptionId = "12345678-1234-1234-1234-123456789012"
    
    # Resource Group
    ResourceGroupName = "Auraly-RG"
    Location = "eastus"
    Environment = "dev"  # dev, staging, prod
    
    # SQL Server
    SqlAdminUsername = "sqladmin"
    SqlAdminPassword = ConvertTo-SecureString "TuPassword123!" -AsPlainText -Force
    
    # Azure OpenAI
    OpenAITextDeploymentName = "gpt-4.1-mini"
    OpenAIAudioDeploymentName = "whisper-1"
    
    # WhatsApp Business API
    WhatsAppPhoneNumberId = "123456789012345"
    WhatsAppAccessToken = ConvertTo-SecureString "tu-access-token-aqui" -AsPlainText -Force
    
    # Function App (opcional - se genera automáticamente si está vacío)
    FunctionAppName = ""
    
    # Opciones
    EnableApplicationInsights = $true
    SkipDatabaseDeployment = $false
}

# Ejecutar despliegue
& "$PSScriptRoot\Deploy-AzureInfrastructure.ps1" `
    -SubscriptionId $Config.SubscriptionId `
    -ResourceGroupName $Config.ResourceGroupName `
    -Location $Config.Location `
    -Environment $Config.Environment `
    -SqlAdminUsername $Config.SqlAdminUsername `
    -SqlAdminPassword $Config.SqlAdminPassword `
    -OpenAITextDeploymentName $Config.OpenAITextDeploymentName `
    -OpenAIAudioDeploymentName $Config.OpenAIAudioDeploymentName `
    -WhatsAppPhoneNumberId $Config.WhatsAppPhoneNumberId `
    -WhatsAppAccessToken $Config.WhatsAppAccessToken `
    -FunctionAppName $Config.FunctionAppName `
    -EnableApplicationInsights:$Config.EnableApplicationInsights `
    -SkipDatabaseDeployment:$Config.SkipDatabaseDeployment

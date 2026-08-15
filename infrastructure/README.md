# Infraestructura Azure - Auraly

Este directorio contiene scripts para desplegar toda la infraestructura necesaria en Azure.

## 📋 Requisitos Previos

1. **Azure CLI o Azure PowerShell instalado**
   ```powershell
   # Instalar módulos de Azure PowerShell
   Install-Module -Name Az -AllowClobber -Scope CurrentUser
   ```

2. **Permisos necesarios:**
   - Contributor o Owner en la suscripción de Azure
   - Permisos para crear recursos en Azure

3. **Credenciales de WhatsApp Business API:**
   - App ID y App Secret de Meta for Developers
   - Phone Number ID (se obtiene automáticamente con el script)
   - Access Token (se genera automáticamente o se puede proporcionar)

4. **Aprobación de Azure OpenAI** (si aplica):
   - Azure OpenAI requiere aprobación previa de Microsoft
   - Puedes solicitarla en: https://aka.ms/oai/access

## 🚀 Despliegue Rápido

### Opción 1: Script Completo (Recomendado)

```powershell
.\Deploy-AzureInfrastructure.ps1 `
    -SubscriptionId "tu-subscription-id" `
    -SqlAdminUsername "sqladmin" `
    -SqlAdminPassword (ConvertTo-SecureString "TuPassword123!" -AsPlainText -Force) `
    -WhatsAppPhoneNumberId "123456789012345" `
    -WhatsAppAccessToken (ConvertTo-SecureString "tu-access-token" -AsPlainText -Force)
```

### Opción 2: Con Parámetros Adicionales

```powershell
.\Deploy-AzureInfrastructure.ps1 `
    -SubscriptionId "tu-subscription-id" `
    -ResourceGroupName "Auraly-Production-RG" `
    -Location "eastus" `
    -Environment "prod" `
    -SqlAdminUsername "sqladmin" `
    -SqlAdminPassword (ConvertTo-SecureString "TuPassword123!" -AsPlainText -Force) `
    -OpenAITextDeploymentName "gpt-4o-mini" `
    -OpenAIAudioDeploymentName "whisper-1" `
    -WhatsAppPhoneNumberId "123456789012345" `
    -WhatsAppAccessToken (ConvertTo-SecureString "tu-access-token" -AsPlainText -Force) `
    -EnableApplicationInsights
```

## 📝 Parámetros del Script

| Parámetro | Requerido | Descripción | Default |
|-----------|-----------|-------------|---------|
| `SubscriptionId` | Sí | ID de la suscripción de Azure | - |
| `ResourceGroupName` | No | Nombre del grupo de recursos | `Auraly-RG` |
| `Location` | No | Región de Azure | `eastus` |
| `Environment` | No | Ambiente (dev/staging/prod) | `dev` |
| `SqlAdminUsername` | Sí | Usuario administrador de SQL | - |
| `SqlAdminPassword` | Sí | Contraseña del administrador SQL | - |
| `OpenAITextDeploymentName` | No | Nombre del deployment GPT | `gpt-4` |
| `OpenAIAudioDeploymentName` | No | Nombre del deployment Whisper | `whisper-1` |
| `WhatsAppPhoneNumberId` | Sí | Phone Number ID de WhatsApp | - |
| `WhatsAppAccessToken` | Sí | Access Token de WhatsApp | - |
| `FunctionAppName` | No | Nombre de la Function App | Auto-generado |
| `EnableApplicationInsights` | No | Habilita Application Insights | `$true` |
| `SkipDatabaseDeployment` | No | Omite creación de BD | `$false` |

## 🏗️ Recursos Creados

El script crea los siguientes recursos en Azure:

1. **Resource Group** - Contenedor para todos los recursos
2. **Storage Account** - Para Function App y Blob Storage
3. **Blob Container** - Contenedor `planes-images` para imágenes
4. **SQL Server** - Servidor de base de datos
5. **SQL Database** - Base de datos (nivel Basic)
6. **Azure OpenAI** - Recurso de OpenAI (requiere aprobación)
7. **App Service Plan** - Plan de consumo para Function App
8. **Function App** - Aplicación de funciones con configuración completa
9. **Application Insights** - Monitoreo y telemetría (opcional)

## 🔐 Configuración de Seguridad

### Firewall de SQL Server

El script configura automáticamente:
- ✅ Regla para permitir servicios de Azure (0.0.0.0 - 0.0.0.0)

**Para desarrollo local**, agrega tu IP:
```powershell
New-AzSqlServerFirewallRule `
    -ResourceGroupName "Auraly-RG" `
    -ServerName "tu-sql-server" `
    -FirewallRuleName "MyIP" `
    -StartIpAddress "tu-ip-publica" `
    -EndIpAddress "tu-ip-publica"
```

### Application Settings

Todas las configuraciones sensibles se almacenan como Application Settings en la Function App:
- Connection strings
- API Keys
- Tokens de WhatsApp
- Endpoints de servicios

## 📊 Naming Conventions

El script sigue estas convenciones de nombres:

- **Storage Account**: `auraly{env}stg{unique}`
- **SQL Server**: `auraly-sql-{env}-{unique}`
- **Function App**: `auraly-func-{env}-{unique}`
- **OpenAI**: `auraly-openai-{env}-{unique}`

Donde:
- `{env}` = dev, staging, o prod
- `{unique}` = sufijo único de 8 caracteres

## 🏷️ Tags

Todos los recursos se etiquetan con:
- `Environment`: dev/staging/prod
- `Project`: Auraly
- `ManagedBy`: InfrastructureAsCode
- `CreatedDate`: fecha de creación

## ⚠️ Consideraciones Importantes

### Azure OpenAI

Azure OpenAI requiere **aprobación previa**. Si el script falla al crear el recurso:

1. Solicita acceso en: https://aka.ms/oai/access
2. Una vez aprobado, ejecuta el script nuevamente
3. O crea el recurso manualmente y proporciona el nombre al script

### Contraseña de SQL Server

La contraseña debe cumplir:
- Mínimo 8 caracteres
- Al menos una mayúscula
- Al menos una minúscula
- Al menos un número
- Al menos un carácter especial

### Límites de Azure

- Storage Account: nombres únicos globalmente (24 caracteres)
- Function App: nombres únicos globalmente (60 caracteres)
- SQL Server: nombres únicos globalmente (63 caracteres)

## 🔄 Próximos Pasos Después del Despliegue

1. **Desplegar esquema de base de datos:**
   ```powershell
   cd ..\database\Auraly.Database\Scripts
   .\Deploy.ps1 -ServerInstance "tu-sql-server.database.windows.net" -DatabaseName "Auraly"
   ```

2. **Publicar Function App:**
   ```powershell
   cd ..\..\src\API\Auraly.Platform.Worker
   func azure functionapp publish auraly-func-dev-xxxxx
   ```

3. **Configurar WhatsApp Cloud API (Recomendado):**
   ```powershell
   .\Setup-WhatsAppCloud.ps1 `
       -AppId "tu-app-id" `
       -AppSecret (ConvertTo-SecureString "tu-app-secret" -AsPlainText -Force) `
       -FunctionAppName "auraly-func-dev-xxxxx" `
       -ResourceGroupName "Auraly-RG" `
       -VerifyToken "mi-token-secreto"
   ```
   
   Ver **[README-WhatsApp.md](README-WhatsApp.md)** para más detalles.

4. **O configurar Webhook manualmente:**
   ```powershell
   # Obtener Function Key
   az functionapp function keys list `
       --name auraly-func-dev-xxxxx `
       --resource-group Auraly-RG `
       --function-name WhatsAppWebhook
   
   # Configurar en Meta for Developers:
   # URL: https://auraly-func-dev-xxxxx.azurewebsites.net/api/WhatsAppWebhook?code=TU_FUNCTION_KEY
   # Verify Token: (configura uno personalizado)
   ```

5. **Subir imágenes de planes:**
   ```powershell
   # Usando Azure Portal o Azure Storage Explorer
   # Subir: plan-basico.jpg, plan-premium.jpg, plan-deluxe.jpg
   # Al contenedor: planes-images
   ```

## 🧪 Verificación Post-Despliegue

```powershell
# Verificar recursos creados
Get-AzResource -ResourceGroupName "Auraly-RG" | Format-Table Name, ResourceType, Location

# Verificar configuración de Function App
az functionapp config appsettings list `
    --name auraly-func-dev-xxxxx `
    --resource-group Auraly-RG

# Probar conexión a SQL
Test-AzSqlDatabaseConnection `
    -ResourceGroupName "Auraly-RG" `
    -ServerName "tu-sql-server" `
    -DatabaseName "Auraly"
```

## 🗑️ Eliminar Recursos

Para eliminar toda la infraestructura:

```powershell
Remove-AzResourceGroup -Name "Auraly-RG" -Force
```

⚠️ **CUIDADO**: Esto eliminará TODOS los recursos del grupo.

## 📚 Scripts Disponibles

- **Deploy-AzureInfrastructure.ps1**: Despliega toda la infraestructura de Azure
- **Setup-WhatsAppCloud.ps1**: Configura WhatsApp Cloud API y webhook
- **Test-Prerequisites.ps1**: Verifica requisitos previos antes del despliegue

## 📚 Referencias

- [Azure Functions Documentation](https://docs.microsoft.com/azure/azure-functions/)
- [Azure SQL Database Documentation](https://docs.microsoft.com/azure/sql-database/)
- [Azure OpenAI Documentation](https://docs.microsoft.com/azure/cognitive-services/openai/)
- [Azure Storage Documentation](https://docs.microsoft.com/azure/storage/)
- [WhatsApp Cloud API Documentation](https://developers.facebook.com/docs/whatsapp/cloud-api)

## 🐛 Troubleshooting

### Error: "Subscription not found"
- Verifica que el SubscriptionId sea correcto
- Verifica que tengas acceso a la suscripción

### Error: "Resource already exists"
- El script detecta recursos existentes y los reutiliza
- Si quieres recrear, elimina el recurso primero

### Error: "Azure OpenAI requires approval"
- Solicita acceso en https://aka.ms/oai/access
- O crea el recurso manualmente y proporciona el nombre

### Error: "Storage account name not available"
- Los nombres de Storage Account son únicos globalmente
- El script genera un nombre único automáticamente

## 📞 Soporte

Para problemas o preguntas sobre el despliegue, consulta la documentación del proyecto o contacta al equipo de desarrollo.

# Guía de Deploy - Mimos Baby Spa WhatsApp Assistant

## ✅ Checklist Pre-Deploy

- [ ] Azure SQL Database creada y configurada
- [ ] Azure Blob Storage creado con contenedor `planes-images`
- [ ] Imágenes de planes subidas a Blob Storage (`plan-basico.jpg`, `plan-premium.jpg`, `plan-deluxe.jpg`)
- [ ] Azure OpenAI configurado con modelo desplegado
- [ ] WhatsApp Business API configurada en Meta for Developers
- [ ] Todas las credenciales y connection strings listas

## 📋 Pasos de Deploy

### 1. Crear Azure Function App

```powershell
# Login a Azure
az login

# Crear Resource Group
az group create --name MimosBabySpa --location eastus

# Crear Storage Account
az storage account create `
  --name mimosbabyspastorage `
  --resource-group MimosBabySpa `
  --location eastus `
  --sku Standard_LRS

# Crear Function App
az functionapp create `
  --resource-group MimosBabySpa `
  --consumption-plan-location eastus `
  --runtime dotnet-isolated `
  --runtime-version 8 `
  --functions-version 4 `
  --name mimosbabyspa-functions `
  --storage-account mimosbabyspastorage
```

### 2. Configurar Application Settings

```powershell
az functionapp config appsettings set `
  --name mimosbabyspa-functions `
  --resource-group MimosBabySpa `
  --settings `
    "ConnectionStrings:DefaultConnection=Server=tcp:TU_SERVIDOR.database.windows.net,1433;Initial Catalog=TU_DB;Persist Security Info=False;User ID=TU_USUARIO;Password=TU_PASSWORD;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;" `
    "BlobStorage:ContainerName=planes-images" `
    "OpenAI:TextModel:ApiKey=TU_GPT_KEY" `
    "OpenAI:TextModel:Endpoint=https://TU_RECURSO_GPT.openai.azure.com/" `
    "OpenAI:TextModel:DeploymentName=gpt-4o-mini" `
    "OpenAI:AudioModel:ApiKey=TU_WHISPER_KEY" `
    "OpenAI:AudioModel:Endpoint=https://TU_RECURSO_WHISPER.openai.azure.com/" `
    "OpenAI:AudioModel:DeploymentName=whisper-1" `
    "WhatsApp:PhoneNumberId=TU_PHONE_NUMBER_ID" `
    "WhatsApp:AccessToken=TU_ACCESS_TOKEN"
```

### 3. Aplicar Migraciones a la Base de Datos

```powershell
cd src\Infrastructure\MimosBabySpa.Infrastructure
dotnet ef database update --startup-project ..\..\API\MimosBabySpa.API\MimosBabySpa.API.csproj --context ApplicationDbContext --connection "TU_CONNECTION_STRING"
```

### 4. Publicar Function App

```powershell
cd src\API\MimosBabySpa.API
func azure functionapp publish mimosbabyspa-functions
```

### 5. Configurar Webhook en Meta

1. Ve a [Meta for Developers](https://developers.facebook.com/)
2. Selecciona tu aplicación de WhatsApp
3. Ve a Configuración > Webhooks
4. Configura:
   - **Callback URL**: `https://mimosbabyspa-functions.azurewebsites.net/api/WhatsAppWebhook?code=TU_FUNCTION_KEY`
   - **Verify Token**: Un token personalizado (guárdalo)
   - **Suscribirse a eventos**: `messages`

### 6. Obtener Function Key

```powershell
az functionapp function keys list `
  --name mimosbabyspa-functions `
  --resource-group MimosBabySpa `
  --function-name WhatsAppWebhook
```

## 🧪 Pruebas Post-Deploy

1. Envía un mensaje de prueba desde WhatsApp
2. Verifica los logs en Azure Portal
3. Revisa la base de datos para confirmar que se guardaron los mensajes
4. Verifica que las imágenes se envían correctamente

## 🔍 Monitoreo

- **Application Insights**: Configura en Azure Portal para monitorear errores y rendimiento
- **Logs de Function App**: Revisa en Azure Portal > Function App > Logs
- **Base de Datos**: Monitorea queries y rendimiento en Azure SQL

## 🚨 Troubleshooting

### Error: "No se puede conectar a la base de datos"
- Verifica que el firewall de Azure SQL permite conexiones desde Azure Services
- Confirma que la connection string es correcta

### Error: "WhatsApp API error"
- Verifica que el Access Token no haya expirado
- Confirma que el Phone Number ID es correcto

### Error: "OpenAI error"
- Verifica que el endpoint y API key son correctos
- Confirma que el deployment name coincide con el modelo desplegado

### Las imágenes no se envían
- Verifica que las imágenes existen en Blob Storage
- Confirma que el contenedor tiene acceso público o que la URL es accesible

## 📞 Soporte

Para problemas técnicos, revisa los logs en Azure Portal o contacta al equipo de desarrollo.

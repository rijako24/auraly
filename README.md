# Mimos Baby Spa - Asistente de Ventas WhatsApp MVP

MVP completo de un asistente de ventas de WhatsApp para Mimos Baby Spa, desarrollado con .NET 8 y Clean Architecture, listo para deploy en Azure.

## 🏗️ Arquitectura

El proyecto sigue **Clean Architecture** con las siguientes capas:

- **Domain**: Entidades, interfaces de repositorios, enums
- **Application**: Servicios de aplicación, DTOs, lógica de negocio
- **Infrastructure**: Implementación de repositorios (EF Core), servicios externos (WhatsApp, OpenAI, Blob Storage)
- **API**: Azure Functions para el webhook de WhatsApp

## 📋 Requisitos Previos

- .NET 8 SDK
- Azure Subscription
- Azure SQL Database
- Azure Blob Storage
- Azure OpenAI (GPT-4 o GPT-4o-mini)
- WhatsApp Business API (Meta)

## 🚀 Configuración Inicial

### 1. Configurar Base de Datos

1. Crear una base de datos SQL en Azure
2. Actualizar la cadena de conexión en `local.settings.json`:
   ```
   "ConnectionStrings:DefaultConnection": "<TU_CONNECTION_STRING>"
   ```

### 2. Configurar Azure Blob Storage

1. Crear una cuenta de almacenamiento en Azure
2. Crear un contenedor llamado `planes-images`
3. Subir las imágenes de los planes:
   - `plan-basico.jpg`
   - `plan-premium.jpg`
   - `plan-deluxe.jpg`
4. Blob Storage usa `AzureWebJobsStorage` (la misma cuenta que Azure Functions requiere)

### 3. Configurar Azure OpenAI

1. Crear recurso(s) de Azure OpenAI (texto/GPT y opcionalmente Whisper en recurso separado)
2. Desplegar modelo GPT (gpt-4 o gpt-4o-mini) y Whisper (whisper-1)
3. Actualizar en `local.settings.json`:
   - `OpenAI:TextModel:ApiKey`, `OpenAI:TextModel:Endpoint`, `OpenAI:TextModel:DeploymentName`
   - `OpenAI:AudioModel:ApiKey`, `OpenAI:AudioModel:Endpoint`, `OpenAI:AudioModel:DeploymentName`

### 4. Configurar WhatsApp Cloud API

1. Crear una aplicación en Meta for Developers
2. Configurar WhatsApp Business API
3. Obtener `PhoneNumberId` y `AccessToken`
4. Actualizar `WhatsApp:PhoneNumberId` y `WhatsApp:AccessToken` en `local.settings.json`

### 5. Aplicar Migraciones

```powershell
cd src\Tests
.\ApplyMigrations.ps1
```

O manualmente:

```powershell
cd src\Infrastructure\MimosBabySpa.Infrastructure
dotnet ef database update --startup-project ..\..\API\MimosBabySpa.API\MimosBabySpa.API.csproj --context ApplicationDbContext
```

## 🔧 Desarrollo Local

### Ejecutar Azure Functions Localmente

```powershell
cd src\API\MimosBabySpa.API
func start
```

### Configurar Webhook en Meta

1. En Meta for Developers, configura el webhook:
   - URL: `https://tu-function-app.azurewebsites.net/api/WhatsAppWebhook?code=TU_FUNCTION_KEY`
   - Verify Token: Configura un token personalizado
   - Suscríbete a eventos: `messages`

### Probar Localmente con ngrok

```powershell
ngrok http 7071
```

Usa la URL de ngrok para configurar el webhook temporalmente.

## 📝 Scripts de Prueba

Ejecuta los escenarios de prueba:

```powershell
cd src\Tests
.\TestScenarios.ps1 -WebhookUrl "http://localhost:7071/api/WhatsAppWebhook" -TestPhoneNumber "1234567890"
```

Los escenarios incluyen:
1. Cliente curioso (saludo inicial)
2. Cliente indeciso (pregunta por edad)
3. Cliente directo (pregunta precios)
4. Objeción (dudas de seguridad)
5. Reserva directa
6. Solicitud de humano

## 🚢 Deploy a Azure

### 1. Crear Azure Function App

```powershell
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
    "ConnectionStrings:DefaultConnection=<TU_CONNECTION_STRING>" `
    "BlobStorage:ContainerName=planes-images" `
    "OpenAI:TextModel:ApiKey=<TU_GPT_KEY>" `
    "OpenAI:TextModel:Endpoint=<TU_GPT_ENDPOINT>" `
    "OpenAI:TextModel:DeploymentName=gpt-4o-mini" `
    "OpenAI:AudioModel:ApiKey=<TU_WHISPER_KEY>" `
    "OpenAI:AudioModel:Endpoint=<TU_WHISPER_ENDPOINT>" `
    "OpenAI:AudioModel:DeploymentName=whisper-1" `
    "WhatsApp:PhoneNumberId=<TU_PHONE_NUMBER_ID>" `
    "WhatsApp:AccessToken=<TU_ACCESS_TOKEN>"
```

### 3. Publicar Function App

```powershell
cd src\API\MimosBabySpa.API
func azure functionapp publish mimosbabyspa-functions
```

### 4. Configurar Webhook en Meta

Actualiza la URL del webhook en Meta for Developers con la URL de tu Function App.

## 📊 Estructura de Base de Datos

### Tablas

- **Conversations**: Almacena conversaciones con contexto
- **Messages**: Almacena todos los mensajes (usuario y bot)
- **Leads**: Gestiona leads y su estado

### Migraciones

Las migraciones se crean automáticamente con EF Core Code First. Para crear una nueva migración:

```powershell
cd src\Infrastructure\MimosBabySpa.Infrastructure
dotnet ef migrations add NombreMigracion --startup-project ..\..\API\MimosBabySpa.API\MimosBabySpa.API.csproj --context ApplicationDbContext
```

## 🧠 Inteligencia Artificial

El sistema utiliza Azure OpenAI para:
- **Clasificación de intenciones**: Identifica la intención del mensaje del cliente
- **Generación de respuestas**: Crea respuestas contextuales usando RAG simple

### Intenciones Soportadas

1. `Greeting` - Saludo inicial
2. `AskAge` - Pregunta por edad del bebé
3. `AskInfo` - Pregunta sobre el spa
4. `AskPrice` - Pregunta sobre planes o precios
5. `Objecion` - Dudas o miedos
6. `ReservationRequest` - Quiere reservar
7. `TalkToHuman` - Pide hablar con humano
8. `FollowUp` - Continuación de conversación

## 📦 Planes Disponibles

1. **Plan Básico**: Masaje y relajación básica (30 min)
2. **Plan Premium**: Masaje + hidroterapia ligera (45 min)
3. **Plan Deluxe**: Masaje, hidroterapia y estimulación sensorial (60 min)

## 🔍 Logging

El sistema utiliza Application Insights para logging. Configura la clave de instrumentación en Azure Portal.

## 🛠️ Tecnologías Utilizadas

- .NET 8
- Azure Functions (isolated process)
- Entity Framework Core 8
- Azure SQL Database
- Azure Blob Storage
- Azure OpenAI
- WhatsApp Cloud API
- Clean Architecture
- Repository + UnitOfWork Pattern

## 📝 Notas Importantes

1. **Seguridad**: Nunca hardcodees credenciales. Usa siempre `local.settings.json` (desarrollo local) o Azure Application Settings/Key Vault (producción).
2. **Escalabilidad**: El sistema está diseñado para escalar horizontalmente con Azure Functions.
3. **Monitoreo**: Configura Application Insights para monitorear el rendimiento y errores.
4. **Imágenes**: Asegúrate de subir las imágenes de los planes a Blob Storage antes de usar el sistema.

## 🤝 Contribuciones

Este es un MVP desarrollado para deploy en 7 días. Para producción, considera:
- Implementar autenticación más robusta
- Agregar tests unitarios e integración
- Implementar rate limiting
- Agregar manejo de errores más granular
- Implementar sistema de tickets para transferencias a humanos

## 📄 Licencia

Propietario - Mimos Baby Spa

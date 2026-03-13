# Configuración de la Base de Datos

## Cómo Funciona la Configuración

### En Desarrollo Local
- La aplicación lee la configuración desde `local.settings.json` (cargado automáticamente por Azure Functions)
- Las migraciones leen desde `local.settings.json` usando `ApplicationDbContextFactory`

### En Azure (Producción)
- La aplicación lee automáticamente desde **Application Settings** de Azure Functions (variables de entorno)
- Las Application Settings se configuran mediante el script `Deploy-AzureInfrastructure.ps1`
- Las migraciones normalmente se ejecutan desde la máquina local antes del despliegue

## Application Settings en Azure

Las siguientes configuraciones deben estar en Application Settings de Azure Functions:

```
ConnectionStrings:DefaultConnection = <cadena de conexión a SQL Server>
AzureWebJobsStorage = <cadena de conexión a Storage - usada por Functions y Blob Storage>
BlobStorage:ContainerName = planes-images
OpenAI:TextModel:ApiKey = <clave API para GPT>
OpenAI:TextModel:Endpoint = <endpoint para GPT>
OpenAI:TextModel:DeploymentName = gpt-4o-mini
OpenAI:AudioModel:ApiKey = <clave API para Whisper>
OpenAI:AudioModel:Endpoint = <endpoint para Whisper>
OpenAI:AudioModel:DeploymentName = whisper-1
WhatsApp:PhoneNumberId = <ID del número de teléfono>
WhatsApp:AccessToken = <token de acceso de WhatsApp>
```

**Nota**: El formato `ConnectionStrings:DefaultConnection` y `WhatsApp:PhoneNumberId` es correcto. Azure Functions convierte los `:` en variables de entorno que .NET Configuration puede leer automáticamente.

## Ejecutar Migraciones

Las migraciones se ejecutan desde la máquina local antes del despliegue:

```powershell
cd src/Infrastructure/MimosBabySpa.Infrastructure
dotnet ef database update --startup-project ..\..\API\MimosBabySpa.API\MimosBabySpa.API.csproj --context ApplicationDbContext
```

Esto lee la cadena de conexión desde `local.settings.json` (que debe apuntar a la base de datos de Azure en producción).

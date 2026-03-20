# Servicio de Procesamiento de Mensajes de WhatsApp

Este servicio encapsula toda la lógica de negocio para procesar mensajes de WhatsApp, permitiendo su reutilización en diferentes tipos de aplicaciones (Azure Functions, aplicaciones de consola, APIs REST, etc.).

## Servicios Principales

### `IWhatsAppMessageProcessorService`

Servicio principal que procesa mensajes entrantes de WhatsApp con el **Generic Flow Engine** (`IFlowOrchestrationService`). Maneja:
- Verificación de webhooks
- Procesamiento de mensajes (conversaciones, leads, respuesta del flujo, envío por WhatsApp)
- Requiere `BusinessContext.AgentId` (asignado al número en `BusinessWhatsAppNumbers`)

### `IWhatsAppWebhookParserService`

Servicio auxiliar que extrae y parsea mensajes del webhook de WhatsApp.

## Ejemplo de Uso en Azure Functions

```csharp
public class WhatsAppWebhookFunction
{
    private readonly IWhatsAppMessageProcessorService _messageProcessorService;
    private readonly IWhatsAppWebhookParserService _webhookParserService;

    [Function("WhatsAppWebhook")]
    public async Task<HttpResponseData> Run([HttpTrigger(...)] HttpRequestData req)
    {
        // Verificar webhook
        var challenge = await _messageProcessorService.VerifyWebhookAsync(mode, token, challenge);
        
        // Procesar mensajes
        var messages = _webhookParserService.ExtractTextMessages(webhookData);
        foreach (var message in messages)
        {
            await _messageProcessorService.ProcessIncomingMessageAsync(
                businessContext,
                message.UserNumber,
                message.MessageText,
                message.CustomerName);
        }
    }
}
```

## Ejemplo de Uso en Aplicación de Consola

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MimosBabySpa.Application.Services;

class Program
{
    static async Task Main(string[] args)
    {
        // Configurar servicios (similar a Program.cs de la API)
        var services = new ServiceCollection();
        // ... configurar todos los servicios necesarios ...
        services.AddScoped<IWhatsAppMessageProcessorService, WhatsAppMessageProcessorService>();
        
        var serviceProvider = services.BuildServiceProvider();
        var processor = serviceProvider.GetRequiredService<IWhatsAppMessageProcessorService>();

        // Procesar un mensaje directamente
        var businessContext = /* resolver vía IBusinessIdentificationService u otro origen */;
        await processor.ProcessIncomingMessageAsync(
            businessContext,
            userNumber: "+1234567890",
            messageText: "Hola, quiero información sobre los planes",
            customerName: "Juan Pérez");
    }
}
```

## Ventajas de esta Arquitectura

1. **Separación de Responsabilidades**: La función de Azure solo maneja HTTP, el servicio maneja la lógica de negocio
2. **Reutilización**: El mismo servicio puede usarse en diferentes tipos de aplicaciones
3. **Testabilidad**: Es más fácil hacer pruebas unitarias del servicio sin necesidad de mockear Azure Functions
4. **Mantenibilidad**: La lógica de negocio está centralizada en un solo lugar

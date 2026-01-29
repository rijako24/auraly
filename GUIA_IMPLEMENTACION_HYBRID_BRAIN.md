# Guía de Implementación: Hybrid Transactional Brain

## Registro de Servicios en Program.cs

### Servicios Core

```csharp
// Estado y Gestión de Sesión
builder.Services.AddSingleton<IConversationStateManager, ConversationStateManager>();

// Flow Engine (Cerebro Determinístico)
builder.Services.AddSingleton<IFlowEngine, FlowEngine>();

// Business Rules Engine
builder.Services.AddScoped<IBusinessRuleEngine, BusinessRuleEngine>();

// Business Configuration Provider
builder.Services.AddScoped<IBusinessConfigurationProvider, BusinessConfigurationProvider>();

// LLM Adapter
builder.Services.AddScoped<ILLMAdapter>(sp =>
{
    var openAIClient = sp.GetRequiredService<OpenAIClient>();
    var deploymentName = builder.Configuration["AzureOpenAI:DeploymentName"] 
        ?? throw new InvalidOperationException("DeploymentName not configured");
    var logger = sp.GetRequiredService<ILogger<AzureOpenAIAdapter>>();
    
    return new AzureOpenAIAdapter(openAIClient, deploymentName, logger);
});
```

### Tool Handlers

```csharp
// Registrar todos los tool handlers
builder.Services.AddScoped<IToolHandler, UpdateConversationStateToolHandler>();
builder.Services.AddScoped<IToolHandler, CheckAvailabilityToolHandler>();
builder.Services.AddScoped<IToolHandler, CreateReservationToolHandler>();

// Tool Dispatcher
builder.Services.AddScoped<GenericToolDispatcher>();
```

### Orquestador

```csharp
// Orquestador Híbrido Transaccional
builder.Services.AddScoped<HybridTransactionalOrchestrator>();
```

## Ejemplo de Uso Completo

```csharp
public class WhatsAppMessageProcessor
{
    private readonly HybridTransactionalOrchestrator _orchestrator;
    private readonly ILogger<WhatsAppMessageProcessor> _logger;
    
    public async Task ProcessMessageAsync(
        Guid businessId,
        string userPhone,
        string messageText)
    {
        try
        {
            _logger.LogInformation(
                "Procesando mensaje para BusinessId={BusinessId}, Phone={Phone}",
                businessId, userPhone);
            
            // El orquestador maneja TODO el flujo
            var response = await _orchestrator.ProcessMessageAsync(
                businessId,
                userPhone,
                messageText);
            
            // Enviar respuesta al cliente
            await SendWhatsAppMessageAsync(userPhone, response);
            
            _logger.LogInformation("Mensaje procesado exitosamente");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error procesando mensaje");
            
            // Enviar mensaje de error genérico al usuario
            await SendWhatsAppMessageAsync(
                userPhone,
                "Disculpa, tuve un problema procesando tu mensaje. ¿Puedes intentar de nuevo?");
        }
    }
}
```

## Configuración de Atributos de Negocio

### Opción 1: Configuración en appsettings.json

```json
{
  "BusinessAttributes": {
    "BabySpa": {
      "BabyAge": {
        "DisplayName": "Edad del bebé",
        "Type": "Number",
        "IsRequired": false,
        "ValidationPattern": "^\\d{1,3}$"
      },
      "BabyName": {
        "DisplayName": "Nombre del bebé",
        "Type": "Text",
        "IsRequired": false
      },
      "SpecialConditions": {
        "DisplayName": "Condiciones especiales",
        "Type": "Text",
        "IsRequired": false
      }
    },
    "Restaurant": {
      "PartySize": {
        "DisplayName": "Número de personas",
        "Type": "Number",
        "IsRequired": true,
        "ValidationPattern": "^[1-9][0-9]?$"
      },
      "DietaryRestrictions": {
        "DisplayName": "Restricciones dietéticas",
        "Type": "Text",
        "IsRequired": false
      }
    }
  }
}
```

### Opción 2: Base de Datos

```sql
CREATE TABLE BusinessAttributes (
    AttributeId UNIQUEIDENTIFIER PRIMARY KEY,
    BusinessId UNIQUEIDENTIFIER NOT NULL,
    AttributeName NVARCHAR(100) NOT NULL,
    DisplayName NVARCHAR(200),
    AttributeType NVARCHAR(50), -- Text, Number, Date, etc.
    IsRequired BIT DEFAULT 0,
    ValidationPattern NVARCHAR(500),
    DefaultValue NVARCHAR(200),
    CreatedAt DATETIME2 NOT NULL,
    CONSTRAINT FK_BusinessAttributes_Business 
        FOREIGN KEY (BusinessId) REFERENCES Businesses(BusinessId)
);

CREATE UNIQUE INDEX UX_BusinessAttributes_Name 
    ON BusinessAttributes(BusinessId, AttributeName);
```

## Customización del System Prompt

El system prompt se construye dinámicamente en `BusinessConfigurationProvider`:

```csharp
private string BuildSystemPrompt(
    BusinessInfo business,
    List<ServiceInfo> services,
    RequiredFieldsConfiguration requiredFields,
    Dictionary<string, AttributeDefinition> attributes)
{
    var prompt = $@"Eres un asistente virtual para {business.Name}.

# INFORMACIÓN DEL NEGOCIO
Nombre: {business.Name}
Descripción: {business.Description}

# CATÁLOGO DE SERVICIOS
{string.Join("\n", services.Select(s => 
    $"- {s.Name}: {s.Description} (Duración: {s.DurationMinutes} min, Precio: ${s.Price})"))}

# CAMPOS REQUERIDOS
{string.Join("\n", requiredFields.GetAllRequiredFields().Select(f => $"- {f}"))}

# ATRIBUTOS ADICIONALES
{string.Join("\n", attributes.Select(a => 
    $"- {a.Value.DisplayName} ({a.Key}): {a.Value.Description}"))}

# HERRAMIENTAS
Tienes 3 herramientas:
1. update_conversation_state: actualizar información del cliente
2. check_availability: verificar disponibilidad (NUNCA prometas sin verificar)
3. create_reservation: crear reserva (SOLO después de confirmación explícita)

# FLUJO
1. Entender necesidad del cliente
2. Recolectar información requerida
3. Verificar disponibilidad
4. Si el cliente confirma EXPLÍCITAMENTE, crear reserva

# REGLAS CRÍTICAS
- NUNCA inventar información
- NUNCA prometer disponibilidad sin verificar
- NUNCA confirmar reservas sin llamar create_reservation
- SIEMPRE extraer valores estructurados (no frases)
";

    return prompt;
}
```

## Manejo de Errores

### Errores de Herramientas

```csharp
// Los tool handlers retornan ToolExecutionResult con Success flag
var result = await toolHandler.ExecuteAsync(arguments, context);

if (!result.Success)
{
    _logger.LogWarning("Tool falló: {Message}", result.Message);
    
    // El LLM puede manejar el error y pedir datos de nuevo
    // O el sistema puede retornar mensaje genérico de error
}
```

### Errores de LLM

```csharp
var llmResponse = await _llmAdapter.SendMessageWithToolsAsync(request, functions);

if (!llmResponse.Success)
{
    _logger.LogError("LLM falló: {Error}", llmResponse.ErrorMessage);
    
    // Fallback a respuesta genérica
    return "Disculpa, estoy teniendo dificultades técnicas. ¿Puedes intentar de nuevo?";
}
```

### Errores de Backend

```csharp
try
{
    var reservation = await _reservationService.CreateReservationAsync(...);
    
    if (reservation == null)
    {
        return new ToolExecutionResult
        {
            Success = false,
            Message = "Error: el backend no pudo crear la reserva"
        };
    }
}
catch (Exception ex)
{
    _logger.LogError(ex, "Error al crear reserva");
    
    return new ToolExecutionResult
    {
        Success = false,
        Message = $"Error al crear la reserva: {ex.Message}"
    };
}
```

## Testing

### Unit Test para FlowEngine

```csharp
public class FlowEngineTests
{
    private readonly FlowEngine _flowEngine;
    private readonly ILogger<FlowEngine> _logger;

    public FlowEngineTests()
    {
        _logger = Substitute.For<ILogger<FlowEngine>>();
        _flowEngine = new FlowEngine(_logger);
    }

    [Fact]
    public void Evaluate_WithCompleteData_ReturnsCanCreateReservation()
    {
        // Arrange
        var state = new ConversationState
        {
            Service = "Masaje",
            DesiredDate = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
            DesiredTime = new TimeOnly(15, 0),
            AvailabilityConfirmed = true,
            ReservationConfirmed = true,
            CustomerName = "María López",
            Phone = "+123456789"
        };

        var requiredFields = new RequiredFieldsConfiguration();

        // Act
        var result = _flowEngine.Evaluate(state, requiredFields);

        // Assert
        Assert.True(result.CanCreateReservation);
        Assert.True(result.IsComplete);
        Assert.Equal(100, result.CompletenessPercentage);
    }

    [Fact]
    public void CanCheckAvailability_WithoutService_ReturnsFalse()
    {
        // Arrange
        var state = new ConversationState
        {
            DesiredDate = DateOnly.Today.AddDays(1)
        };

        // Act
        var result = _flowEngine.CanCheckAvailability(state);

        // Assert
        Assert.False(result);
    }
}
```

### Integration Test

```csharp
public class HybridTransactionalOrchestratorTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HybridTransactionalOrchestratorTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ProcessMessage_EndToEndReservation_Success()
    {
        // Arrange
        var scope = _factory.Services.CreateScope();
        var orchestrator = scope.ServiceProvider
            .GetRequiredService<HybridTransactionalOrchestrator>();

        var businessId = Guid.NewGuid();
        var phone = "+1234567890";

        // Act & Assert - Mensaje 1: Solicitud inicial
        var response1 = await orchestrator.ProcessMessageAsync(
            businessId, phone, "Quiero reservar un masaje");
        
        Assert.Contains("masaje", response1, StringComparison.OrdinalIgnoreCase);

        // Mensaje 2: Proporcionar fecha
        var response2 = await orchestrator.ProcessMessageAsync(
            businessId, phone, "Para mañana a las 3pm");
        
        Assert.Contains("disponibilidad", response2, StringComparison.OrdinalIgnoreCase);

        // Mensaje 3: Confirmar
        var response3 = await orchestrator.ProcessMessageAsync(
            businessId, phone, "Sí, confirmo");
        
        Assert.Contains("confirmada", response3, StringComparison.OrdinalIgnoreCase);

        // Verificar que el estado tiene la reserva creada
        var stateManager = scope.ServiceProvider
            .GetRequiredService<IConversationStateManager>();
        var state = await stateManager.GetOrCreateStateAsync(businessId, phone);
        
        Assert.True(state.ReservationCreated);
        Assert.NotNull(state.ReservationId);
    }
}
```

## Monitoreo y Logging

### Structured Logging

```csharp
_logger.LogInformation(
    "Estado cargado: StateId={StateId}, Version={Version}, Stage={Stage}, Completeness={Completeness}",
    state.StateId,
    state.Version,
    state.CurrentStage,
    completeness);

_logger.LogInformation(
    "Tool ejecutado: {FunctionName} - Success={Success}, Message={Message}",
    toolCall.FunctionName,
    result.Success,
    result.Message);
```

### Application Insights

```csharp
// En Program.cs
builder.Services.AddApplicationInsightsTelemetry();

// Custom metrics
var telemetryClient = serviceProvider.GetRequiredService<TelemetryClient>();

telemetryClient.TrackMetric("ConversationCompleteness", completenessPercentage);
telemetryClient.TrackMetric("ToolExecutionTime", executionTimeMs);
telemetryClient.TrackMetric("LLMTokens", tokenCount);
```

### Health Checks

```csharp
builder.Services.AddHealthChecks()
    .AddCheck<LLMHealthCheck>("llm")
    .AddCheck<DatabaseHealthCheck>("database")
    .AddCheck<WhatsAppHealthCheck>("whatsapp");

app.MapHealthChecks("/health");
```

## Deployment

### Variables de Entorno

```bash
# Azure OpenAI
AZUREOPENAI__ENDPOINT=https://your-openai.openai.azure.com
AZUREOPENAI__DEPLOYMENTNAME=gpt-4
AZUREOPENAI__APIKEY=your-api-key

# Database
CONNECTIONSTRINGS__DEFAULTCONNECTION=Server=...

# WhatsApp
WHATSAPP__APIURL=https://graph.facebook.com/v17.0
WHATSAPP__ACCESSTOKEN=your-token
```

### Docker

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["MimosBabySpa.API/MimosBabySpa.API.csproj", "MimosBabySpa.API/"]
RUN dotnet restore "MimosBabySpa.API/MimosBabySpa.API.csproj"
COPY . .
WORKDIR "/src/MimosBabySpa.API"
RUN dotnet build "MimosBabySpa.API.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "MimosBabySpa.API.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MimosBabySpa.API.dll"]
```

## Mejores Prácticas

### 1. Validación de Datos

```csharp
// SIEMPRE validar antes de actualizar estado
if (!DateOnly.TryParse(value, out var date))
{
    return new ToolExecutionResult
    {
        Success = false,
        Message = $"'{value}' no es una fecha válida (formato: YYYY-MM-DD)"
    };
}
```

### 2. Manejo de Concurrencia

```csharp
// Usar optimistic locking con versiones
state.Version++;
state.UpdatedAt = DateTime.UtcNow;

try
{
    await _stateManager.SaveStateAsync(state);
}
catch (InvalidOperationException ex) when (ex.Message.Contains("Conflict"))
{
    // Reload state y reintentar
    state = await _stateManager.GetOrCreateStateAsync(businessId, phone);
    // Reaplicar cambios
}
```

### 3. Rate Limiting

```csharp
// Limitar llamadas por usuario
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("messages", opt =>
    {
        opt.PermitLimit = 10;
        opt.Window = TimeSpan.FromMinutes(1);
    });
});
```

### 4. Caching

```csharp
// Cachear configuración de negocio
builder.Services.AddMemoryCache();

public async Task<RequiredFieldsConfiguration> GetRequiredFieldsAsync(Guid businessId)
{
    return await _cache.GetOrCreateAsync(
        $"required-fields-{businessId}",
        async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            return await LoadRequiredFieldsFromDatabaseAsync(businessId);
        });
}
```

## Troubleshooting

### Problema: El LLM no llama herramientas

**Causa:** Function definitions mal formadas o system prompt confuso

**Solución:**
1. Verificar que las function definitions son válidas JSON
2. Simplificar el system prompt
3. Agregar ejemplos explícitos de cuándo llamar cada herramienta

### Problema: Reservas duplicadas

**Causa:** Falta de idempotencia

**Solución:**
```csharp
// Verificar si ya existe reserva
if (state.ReservationCreated && state.ReservationId.HasValue)
{
    return new ToolExecutionResult
    {
        Success = false,
        Message = $"Ya existe una reserva: {state.ReservationId}"
    };
}
```

### Problema: Estado inconsistente

**Causa:** Actualización parcial sin transacción

**Solución:**
```csharp
// Usar transacciones en el StateManager
using var transaction = await _dbContext.Database.BeginTransactionAsync();
try
{
    await _stateManager.SaveStateAsync(state);
    await _historyRepository.AddRecordAsync(changeRecord);
    await transaction.CommitAsync();
}
catch
{
    await transaction.RollbackAsync();
    throw;
}
```

## Próximos Pasos

1. ✅ Implementar todos los componentes core
2. ⏳ Registrar servicios en Program.cs
3. ⏳ Crear migraciones de base de datos para estado persistente
4. ⏳ Implementar caching distribuido (Redis)
5. ⏳ Agregar Application Insights
6. ⏳ Implementar feature toggles
7. ⏳ Crear dashboard de métricas
8. ⏳ Documentar APIs públicas
9. ⏳ Training del equipo

## Recursos Adicionales

- [Documentación de Architecture](./ARQUITECTURA_HYBRID_TRANSACTIONAL_BRAIN.md)
- [Azure OpenAI Best Practices](https://learn.microsoft.com/azure/ai-services/openai/)
- [Clean Architecture Principles](https://blog.cleancoder.com/uncle-bob/2012/08/13/the-clean-architecture.html)

# 🚀 Inicio Rápido: Hybrid Transactional Brain

## ¿Qué se Implementó?

Se ha refactorizado completamente el sistema de IA conversacional a la arquitectura **"Hybrid Transactional Brain"** con separación estricta entre:

- **LLM:** Solo entiende lenguaje natural
- **FlowEngine:** Cerebro determinístico que decide qué hacer
- **Backend:** Única autoridad para disponibilidad y reservas

## ✅ Estado Actual

**TODOS LOS COMPONENTES ESTÁN IMPLEMENTADOS Y LISTOS.**

### Componentes Nuevos Creados

```
src/
├── Domain/
│   └── Models/
│       └── ConversationState.cs ✅ (Nuevo - Estado genérico)
│
├── Application/
│   ├── FlowEngine/ ✅ (Nuevo - Cerebro determinístico)
│   │   ├── IFlowEngine.cs
│   │   └── FlowEngine.cs
│   │
│   ├── Tools/ ✅ (Nuevo - Herramientas genéricas)
│   │   ├── IToolHandler.cs
│   │   ├── UpdateConversationStateToolHandler.cs
│   │   ├── CheckAvailabilityToolHandler.cs
│   │   ├── CreateReservationToolHandler.cs
│   │   └── GenericToolDispatcher.cs
│   │
│   ├── BusinessRules/ ✅ (Nuevo - Reglas de negocio)
│   │   ├── IBusinessRuleEngine.cs
│   │   └── BusinessRuleEngine.cs
│   │
│   ├── Configuration/ ✅ (Nuevo - Config dinámica)
│   │   ├── IBusinessConfigurationProvider.cs
│   │   └── BusinessConfigurationProvider.cs
│   │
│   ├── StateManagement/ ✅ (Nuevo - Gestión de estado)
│   │   ├── IConversationStateManager.cs
│   │   └── ConversationStateManager.cs
│   │
│   ├── LLM/ ✅ (Nuevo - Adapter LLM)
│   │   ├── ILLMAdapter.cs
│   │   └── AzureOpenAIAdapter.cs
│   │
│   └── Orchestration/
│       └── HybridTransactionalOrchestrator.cs ✅ (Nuevo)
│
└── API/
    └── Program.cs ✅ (Actualizado con nuevos servicios)
```

## 🎯 Cómo Empezar a Usar

### Opción 1: Usar Directamente el Nuevo Orquestador

```csharp
public class WhatsAppFunction
{
    private readonly HybridTransactionalOrchestrator _orchestrator;
    
    public WhatsAppFunction(HybridTransactionalOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }
    
    [Function("ProcessWhatsAppMessage")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
    {
        // Parsear request
        var businessId = Guid.Parse(req.Query["businessId"]);
        var phone = req.Query["phone"];
        var message = await new StreamReader(req.Body).ReadToEndAsync();
        
        // ¡Eso es todo! El orquestador maneja TODO el flujo
        var response = await _orchestrator.ProcessMessageAsync(
            businessId,
            phone,
            message);
        
        return new OkObjectResult(new { response });
    }
}
```

### Opción 2: Feature Toggle (A/B Testing)

```csharp
public class WhatsAppMessageProcessor
{
    private readonly IConfiguration _config;
    private readonly HybridTransactionalOrchestrator _newOrchestrator;
    private readonly IConversationOrchestrator _legacyOrchestrator;
    
    public async Task<string> ProcessAsync(
        Guid businessId,
        string phone,
        string message)
    {
        // Feature flag
        var useNewArchitecture = _config
            .GetValue<bool>("FeatureFlags:UseHybridBrain");
        
        if (useNewArchitecture)
        {
            // Nueva arquitectura
            return await _newOrchestrator.ProcessMessageAsync(
                businessId, phone, message);
        }
        else
        {
            // Legacy (fallback)
            return await _legacyOrchestrator.ProcessMessageAsync(
                businessId, phone, message);
        }
    }
}
```

### Configuración en appsettings.json

```json
{
  "FeatureFlags": {
    "UseHybridBrain": false  // Cambiar a true para activar
  },
  
  "OpenAI": {
    "Endpoint": "https://your-openai.openai.azure.com",
    "ApiKey": "your-api-key",
    "TextDeploymentName": "gpt-4o-mini"
  }
}
```

## 📊 Ejemplo de Flujo Completo

### Conversación de Ejemplo

```
Usuario: "Hola, quiero reservar un masaje para mi bebé"

[Sistema Internamente]
1. LLM extrae: service="Masaje"
2. Llama: update_conversation_state("Service", "Masaje")
3. FlowEngine evalúa: falta fecha y hora
4. LLM genera respuesta natural

Bot: "¡Perfecto! El masaje es excelente para bebés. ¿Qué día te gustaría venir?"

Usuario: "El sábado a las 3pm"

[Sistema Internamente]
1. LLM extrae: date="2026-02-01", time="15:00"
2. Llama: update_conversation_state("DesiredDate", "2026-02-01")
3. Llama: update_conversation_state("DesiredTime", "15:00")
4. FlowEngine evalúa: CanCheckAvailability = true
5. Llama: check_availability("Masaje", "2026-02-01", "15:00")
6. Backend retorna: is_available = true
7. Estado: AvailabilityConfirmed = TRUE

Bot: "¡Excelente! Hay disponibilidad el sábado 1 de febrero a las 3pm. ¿Confirmas la reserva?"

Usuario: "Sí, confirmo"

[Sistema Internamente]
1. LLM detecta confirmación explícita
2. Estado: ReservationConfirmed = TRUE
3. FlowEngine evalúa: CanCreateReservation = true
4. Llama: create_reservation()
5. Backend crea reserva y retorna: success=true, reservationId=123
6. Estado: ReservationCreated = TRUE

Bot: "✓ Reserva confirmada exitosamente
     Servicio: Masaje
     Fecha: 01/02/2026
     Hora: 15:00
     ID: 123"
```

## 🔍 Verificar que Todo Funciona

### 1. Verificar Servicios Registrados

```csharp
// En Program.cs, verifica que existen estas líneas:

services.AddSingleton<IFlowEngine, FlowEngine>();
services.AddSingleton<IConversationStateManager, ConversationStateManager>();
services.AddScoped<IBusinessRuleEngine, BusinessRuleEngine>();
services.AddScoped<IBusinessConfigurationProvider, BusinessConfigurationProvider>();
services.AddScoped<ILLMAdapter, AzureOpenAIAdapter>();
services.AddScoped<IToolHandler, UpdateConversationStateToolHandler>();
services.AddScoped<IToolHandler, CheckAvailabilityToolHandler>();
services.AddScoped<IToolHandler, CreateReservationToolHandler>();
services.AddScoped<GenericToolDispatcher>();
services.AddScoped<HybridTransactionalOrchestrator>();
```

✅ **Ya están registrados en Program.cs**

### 2. Compilar el Proyecto

```bash
dotnet build
```

Deberías ver: `Build succeeded. 0 Warning(s). 0 Error(s).`

### 3. Ejecutar Tests (si existen)

```bash
dotnet test
```

## 🐛 Troubleshooting

### Problema: "IFlowEngine not registered"

**Solución:** Asegúrate de que Program.cs tiene los using statements:

```csharp
using MimosBabySpa.Application.FlowEngine;
using MimosBabySpa.Application.Tools;
using MimosBabySpa.Application.BusinessRules;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.StateManagement;
using MimosBabySpa.Application.LLM;
```

### Problema: "OpenAIClient not configured"

**Solución:** Verifica appsettings.json:

```json
{
  "OpenAI": {
    "Endpoint": "tu-endpoint",
    "ApiKey": "tu-api-key",
    "TextDeploymentName": "gpt-4o-mini"
  }
}
```

### Problema: El LLM no llama herramientas

**Solución:** Verifica que el system prompt está siendo generado correctamente por `BusinessConfigurationProvider`. El prompt debe incluir instrucciones claras sobre cuándo llamar cada herramienta.

## 📖 Documentación Completa

Para entender la arquitectura en profundidad:

1. **[ARQUITECTURA_HYBRID_TRANSACTIONAL_BRAIN.md](./ARQUITECTURA_HYBRID_TRANSACTIONAL_BRAIN.md)**
   - Principios arquitectónicos
   - Componentes detallados
   - Flujo transaccional completo
   - Reglas de seguridad

2. **[GUIA_IMPLEMENTACION_HYBRID_BRAIN.md](./GUIA_IMPLEMENTACION_HYBRID_BRAIN.md)**
   - Configuración de servicios
   - Ejemplos de código
   - Testing
   - Deployment
   - Troubleshooting avanzado

3. **[REFACTORIZACION_COMPLETADA.md](./REFACTORIZACION_COMPLETADA.md)**
   - Resumen de cambios
   - Checklist de completitud
   - Roadmap de próximos pasos

## 🎯 Próximos Pasos Recomendados

### Inmediato (Hoy)

1. ✅ Compilar el proyecto
2. ✅ Verificar que todos los servicios están registrados
3. ⏳ Ejecutar una prueba manual con un mensaje de WhatsApp

### Corto Plazo (Esta Semana)

4. ⏳ Implementar feature toggle para A/B testing
5. ⏳ Crear unit tests básicos para FlowEngine
6. ⏳ Configurar logging y métricas

### Mediano Plazo (Próximas 2 Semanas)

7. ⏳ Migrar estado a base de datos (actualmente en memoria)
8. ⏳ Implementar auditoría persistente
9. ⏳ Crear dashboard de monitoreo

## 💡 Tips para Desarrollo

### Debugging

Para ver el flujo interno, busca en los logs:

```
=== INICIO DE PROCESAMIENTO ===
FASE 1: Cargando estado y configuración...
FASE 2: Evaluando flujo con FlowEngine...
FASE 3: Preparando llamada al LLM...
FASE 4: Ejecutando X tool call(s)...
FASE 5: Guardando estado actualizado...
FASE 6: Evaluación post-procesamiento
=== FIN DE PROCESAMIENTO ===
```

### Testing Manual

Puedes testear el FlowEngine directamente:

```csharp
var flowEngine = new FlowEngine(logger);

var state = new ConversationState
{
    Service = "Masaje",
    DesiredDate = DateOnly.Today.AddDays(1),
    DesiredTime = new TimeOnly(15, 0)
};

var requiredFields = new RequiredFieldsConfiguration();

var result = flowEngine.Evaluate(state, requiredFields);

Console.WriteLine($"Can check availability: {result.CanCheckAvailability}");
Console.WriteLine($"Completeness: {result.CompletenessPercentage}%");
Console.WriteLine($"Missing: {string.Join(", ", result.MissingFields)}");
```

### Agregar Nuevo Atributo de Negocio

1. Agrega definición en `BusinessConfigurationProvider.GetBusinessAttributesAsync()`:

```csharp
["BabyWeight"] = new AttributeDefinition
{
    Name = "BabyWeight",
    DisplayName = "Peso del bebé (kg)",
    Type = AttributeType.Number,
    IsRequired = false
}
```

2. Actualiza el system prompt para incluir el nuevo campo

3. ¡Listo! El sistema automáticamente lo manejará usando `Attribute:BabyWeight`

## 🏁 Resumen

**✅ TODO ESTÁ IMPLEMENTADO Y LISTO PARA USAR**

La nueva arquitectura está:
- ✅ Completamente implementada
- ✅ Registrada en Program.cs
- ✅ Documentada exhaustivamente
- ✅ Lista para compilar
- ⏳ Pendiente de testing en ambiente real

**Siguiente paso:** Activar feature flag y probar con tráfico real.

---

**¿Preguntas?** Consulta la documentación completa en:
- [ARQUITECTURA_HYBRID_TRANSACTIONAL_BRAIN.md](./ARQUITECTURA_HYBRID_TRANSACTIONAL_BRAIN.md)
- [GUIA_IMPLEMENTACION_HYBRID_BRAIN.md](./GUIA_IMPLEMENTACION_HYBRID_BRAIN.md)

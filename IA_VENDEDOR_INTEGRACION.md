# 🔌 GUÍA DE INTEGRACIÓN: IA VENDEDOR

## 🎯 OBJETIVO

Esta guía muestra cómo integrar el nuevo sistema de **IA Vendedor** con el código existente.

---

## 📋 CHECKLIST DE INTEGRACIÓN

### ✅ 1. Aplicar Migración de Base de Datos

```powershell
cd c:\Users\RichardJacome\MimosBabySpa

# Aplicar migración
dotnet ef database update --project src/Infrastructure/MimosBabySpa.Infrastructure/MimosBabySpa.Infrastructure.csproj --startup-project src/API/MimosBabySpa.API/MimosBabySpa.API.csproj

# Verificar que las tablas se crearon
# - ConversationSessions
# - CustomerProfiles
# - SalesInteractions
```

### ✅ 2. Actualizar WhatsAppMessageProcessorService

**Antes:**
```csharp
public class WhatsAppMessageProcessorService
{
    private readonly IConversationAgent _agent;
    
    public async Task<string> ProcessAsync(...)
    {
        return await _agent.ProcessMessageAsync(...);
    }
}
```

**Ahora (Opción A - Recomendada):**
```csharp
public class WhatsAppMessageProcessorService
{
    private readonly IConversationOrchestrator _orchestrator;
    private readonly IConversationAgent _agentFallback; // Opcional: fallback
    
    public async Task<string> ProcessAsync(
        Guid businessId,
        string phoneNumber,
        string message)
    {
        try
        {
            // Usar el orquestador nuevo (IA Vendedor)
            return await _orchestrator.ProcessMessageAsync(
                businessId,
                phoneNumber,
                message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en orquestador, usando fallback");
            
            // Fallback al agente anterior si falla
            return await _agentFallback.ProcessMessageAsync(...);
        }
    }
}
```

**Ahora (Opción B - Migración Gradual):**
```csharp
public class WhatsAppMessageProcessorService
{
    private readonly IConversationOrchestrator _orchestrator;
    private readonly IConversationAgent _agent;
    private readonly IConfiguration _config;
    
    public async Task<string> ProcessAsync(...)
    {
        // Feature flag para activar IA Vendedor
        var useAIVendedor = _config.GetValue<bool>("Features:UseAIVendedor");
        
        if (useAIVendedor)
        {
            return await _orchestrator.ProcessMessageAsync(
                businessId, phoneNumber, message);
        }
        else
        {
            return await _agent.ProcessMessageAsync(...);
        }
    }
}
```

---

## 🔄 MIGRACIÓN DE DATOS EXISTENTES

### Script para Migrar Conversaciones Existentes

```sql
-- Crear perfiles para todos los clientes existentes
INSERT INTO CustomerProfiles (
    ProfileId, 
    BusinessId, 
    PhoneNumber, 
    CustomerName,
    Segment,
    TotalPurchases,
    TotalConversations,
    ConversionProbability,
    ChurnRisk,
    FirstContactAt,
    LastContactAt,
    UpdatedAt
)
SELECT 
    NEWID(),
    c.BusinessId,
    c.UserNumber,
    c.CustomerName,
    CASE 
        WHEN EXISTS(SELECT 1 FROM Reservations r WHERE r.ConversationId = c.ConversationId) THEN 3 -- FirstTimeBuyer
        ELSE 2 -- QualifiedLead
    END,
    (SELECT COUNT(*) FROM Reservations r WHERE r.ConversationId = c.ConversationId),
    1, -- Total conversaciones
    0.5, -- Probabilidad neutral
    0.0, -- Sin riesgo inicial
    c.Timestamp,
    c.Timestamp,
    GETUTCDATE()
FROM Conversations c
WHERE NOT EXISTS (
    SELECT 1 FROM CustomerProfiles cp 
    WHERE cp.BusinessId = c.BusinessId 
    AND cp.PhoneNumber = c.UserNumber
);
```

---

## 🧪 TESTING

### Test Manual del Orquestador

```csharp
// En un test o console app
var orchestrator = serviceProvider.GetRequiredService<IConversationOrchestrator>();

// Simular conversación completa
var responses = new List<string>();

responses.Add(await orchestrator.ProcessMessageAsync(
    businessId, "+1234567890", "Hola"));

responses.Add(await orchestrator.ProcessMessageAsync(
    businessId, "+1234567890", "Me llamo Ana"));

responses.Add(await orchestrator.ProcessMessageAsync(
    businessId, "+1234567890", "Mi bebé tiene 4 meses"));

responses.Add(await orchestrator.ProcessMessageAsync(
    businessId, "+1234567890", "Me interesa el masaje relajante"));

responses.Add(await orchestrator.ProcessMessageAsync(
    businessId, "+1234567890", "Para mañana"));

responses.Add(await orchestrator.ProcessMessageAsync(
    businessId, "+1234567890", "Sí, confirmo"));

// Verificar transiciones de estado
// InitialContact → Discovery → Presentation → 
// AvailabilityExploration → Closing → Booking
```

---

## 🎛️ CONFIGURACIÓN AVANZADA

### Ajustar Timeout de Sesión

```csharp
// En SessionManager.cs
private const int SessionExpirationMinutes = 30; // Cambiar según necesidad
```

### Ajustar Máximo de Intentos de Cierre

```csharp
// En ClosingEngine.cs
private const int MaxClosingAttempts = 3; // Cambiar según agresividad
```

### Personalizar Estrategia por Negocio

```csharp
// En SalesStrategyEngine.cs, método ApplyClosingStrategy
if (profile.Segment == CustomerSegment.VIPCustomer)
{
    decision.Tactic = SalesTactic.CreateScarcity;
    decision.CallToAction = "¿Confirmamos tu cita VIP entonces?";
}
```

---

## 📊 MONITOREO Y MÉTRICAS

### Queries Útiles para Analytics

```sql
-- Tasa de conversión por etapa
SELECT 
    Stage,
    COUNT(*) as TotalInteracciones,
    SUM(CASE WHEN WasSuccessful = 1 THEN 1 ELSE 0 END) as Exitosas,
    (SUM(CASE WHEN WasSuccessful = 1 THEN 1 ELSE 0 END) * 100.0 / COUNT(*)) as TasaExito
FROM SalesInteractions
WHERE BusinessId = @BusinessId
GROUP BY Stage
ORDER BY Stage;

-- Objeciones más comunes
SELECT 
    ObjectionDetected,
    COUNT(*) as Frecuencia
FROM SalesInteractions
WHERE BusinessId = @BusinessId 
    AND ObjectionDetected IS NOT NULL
GROUP BY ObjectionDetected
ORDER BY Frecuencia DESC;

-- Clientes con alta probabilidad de conversión
SELECT 
    CustomerName,
    PhoneNumber,
    ConversionProbability,
    TotalConversations,
    TotalPurchases
FROM CustomerProfiles
WHERE BusinessId = @BusinessId 
    AND ConversionProbability > 0.7
    AND TotalPurchases = 0
ORDER BY ConversionProbability DESC;
```

---

## 🚨 TROUBLESHOOTING

### Problema: Sesión no se crea
**Solución:** Verificar que exista conversación en tabla `Conversations`

### Problema: Validación rechaza respuestas
**Solución:** Revisar logs de `SalesResponseValidator` y ajustar reglas

### Problema: No transiciona de estado
**Solución:** Verificar condiciones en `SalesStateMachine.EvaluateTransitionAsync`

### Problema: Respuestas genéricas
**Solución:** Verificar que `DynamicPromptBuilder` esté inyectando contexto correcto

---

## 🔧 ROLLBACK PLAN

Si necesitas revertir temporalmente:

```csharp
// En WhatsAppMessageProcessorService
public async Task<string> ProcessAsync(...)
{
    // Comentar orquestador
    // return await _orchestrator.ProcessMessageAsync(...);
    
    // Usar agente anterior
    return await _agent.ProcessMessageAsync(...);
}
```

Las nuevas tablas no afectan el funcionamiento del código anterior, por lo que es seguro mantenerlas.

---

## 📖 REFERENCIAS

- **Arquitectura completa**: `IA_VENDEDOR_ARQUITECTURA.md`
- **Código del orquestador**: `src/Application/MimosBabySpa.Application/Orchestration/ConversationOrchestrator.cs`
- **Máquina de estados**: `src/Application/MimosBabySpa.Application/Sales/SalesStateMachine.cs`
- **Motor de estrategia**: `src/Application/MimosBabySpa.Application/Sales/SalesStrategyEngine.cs`

---

**¡Sistema IA Vendedor listo para integración!** 🎉

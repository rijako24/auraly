# Arquitectura: Hybrid Transactional Brain

## Resumen Ejecutivo

Este documento describe la arquitectura **Hybrid Transactional Brain** implementada en el sistema de IA conversacional transaccional de MimosBabySpa. Esta arquitectura establece una separación estricta entre comprensión de lenguaje natural (LLM) y autoridad de negocio (Backend), con un motor de flujo determinístico (FlowBrain) que orquesta el proceso.

## Principios Arquitectónicos Fundamentales

### 1. Separación Estricta de Responsabilidades

```
┌─────────────────────────────────────────────────────────────┐
│                   LLM LAYER (Comprensión)                   │
│  - Entender lenguaje natural                                │
│  - Detectar intención                                       │
│  - Extraer entidades                                        │
│  - Llamar herramientas con (field, value)                  │
│  NUNCA: decidir disponibilidad, confirmar reservas,        │
│         aplicar reglas de negocio, inventar datos          │
└─────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────┐
│              FLOW ENGINE (Cerebro Determinístico)           │
│  - Determinar qué datos faltan                             │
│  - Decidir qué herramientas pueden ejecutarse              │
│  - Validar si se puede avanzar                             │
│  - Calcular completitud del flujo                          │
│  NUNCA: analizar texto, tomar decisiones de negocio        │
└─────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────┐
│                BACKEND (Autoridad Absoluta)                 │
│  - Validar disponibilidad                                   │
│  - Crear reservas                                           │
│  - Aplicar reglas de negocio                               │
│  - Asignar recursos                                        │
│  - Resolver conflictos                                     │
└─────────────────────────────────────────────────────────────┘
```

### 2. Domain-Agnostic Design

**Principio:** El código NUNCA debe conocer campos específicos de negocio.

```csharp
// ❌ MAL - Hardcoding de negocio específico
if (babyAge < 6) { /* lógica */ }

// ✓ BIEN - Genérico y configurable
if (state.HasAttribute("BabyAge")) {
    var value = state.GetAttribute("BabyAge");
    // Validar según configuración dinámica
}
```

Todos los datos específicos del negocio van en:
- `ConversationState.Attributes` (diccionario genérico)
- Configuración dinámica del negocio
- System prompt

### 3. Estado como Única Fuente de Verdad

**ConversationState** es la ÚNICA fuente de verdad sobre datos recolectados:

- Solo almacena valores estructurados (nunca frases)
- No contiene lógica de negocio
- Es serializable, auditable y replayable
- Versionado para optimistic locking
- Inmutable transaccionalmente

## Componentes Principales

### 1. ConversationState (Domain Model)

**Ubicación:** `src/Domain/MimosBabySpa.Domain/Models/ConversationState.cs`

```csharp
public class ConversationState
{
    // Campos core transaccionales
    public string? CustomerName { get; set; }
    public string? Email { get; set; }
    public string? Service { get; set; }
    public DateOnly? DesiredDate { get; set; }
    public TimeOnly? DesiredTime { get; set; }
    
    // Flags de confirmación (solo backend puede establecer en true)
    public bool AvailabilityConfirmed { get; set; }
    public bool ReservationConfirmed { get; set; }
    public bool ReservationCreated { get; set; }
    
    // Atributos dinámicos específicos del negocio
    public Dictionary<string, string> Attributes { get; set; }
    
    // Metadatos de auditoría
    public int Version { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

**Características:**
- Domain-agnostic: funciona para cualquier tipo de negocio
- Estructurado: solo valores, nunca texto libre
- Auditable: versionado y timestamps
- Extensible: Attributes para campos de negocio

### 2. FlowEngine (Flow Brain)

**Ubicación:** `src/Application/MimosBabySpa.Application/FlowEngine/`

El cerebro determinístico del sistema. Trabaja SOLO con estado estructurado.

```csharp
public interface IFlowEngine
{
    // Evalúa el estado y determina acciones posibles
    FlowEvaluationResult Evaluate(
        ConversationState state,
        RequiredFieldsConfiguration requiredFields);
    
    // Decide si se puede verificar disponibilidad
    bool CanCheckAvailability(ConversationState state);
    
    // Decide si se puede crear reserva
    bool CanCreateReservation(ConversationState state);
    
    // Obtiene campos faltantes
    List<string> GetMissingFields(
        ConversationState state,
        RequiredFieldsConfiguration requiredFields);
}
```

**Responsabilidades:**
- ✅ Determinar qué datos faltan
- ✅ Validar si se pueden ejecutar herramientas
- ✅ Calcular completitud del flujo
- ✅ Sugerir siguiente etapa

**NO es responsable de:**
- ❌ Analizar texto del usuario
- ❌ Tomar decisiones de negocio
- ❌ Validar disponibilidad
- ❌ Crear reservas

### 3. Tools (Herramientas Genéricas)

**Ubicación:** `src/Application/MimosBabySpa.Application/Tools/`

Tres herramientas principales, completamente domain-agnostic:

#### 3.1 update_conversation_state

```csharp
// Parámetros:
// - field: nombre del campo (o "Attribute:NombreAtributo")
// - value: valor ESTRUCTURADO (no frase)

// Ejemplos:
update_conversation_state("CustomerName", "María López")
update_conversation_state("DesiredDate", "2026-01-27")
update_conversation_state("Attribute:BabyAge", "6")
```

**Reglas críticas:**
- Solo acepta valores estructurados
- NUNCA frases del usuario
- No sobrescribe valores válidos sin razón
- Resetea disponibilidad si cambia fecha/hora/servicio

#### 3.2 check_availability

```csharp
// Parámetros:
// - service: nombre EXACTO del servicio
// - date: formato ISO (YYYY-MM-DD)
// - time: formato ISO (HH:MM) - opcional

// El LLM NUNCA decide disponibilidad
// Solo interpreta is_available del backend
```

**Reglas críticas:**
- NUNCA prometer disponibilidad antes de llamar
- Solo el backend establece `AvailabilityConfirmed = true`
- Interpretar respuesta del backend como verdad absoluta

#### 3.3 create_reservation

```csharp
// Sin parámetros adicionales (usa el estado)
// Solo se puede ejecutar si:
// 1. FlowEngine.CanCreateReservation() = true
// 2. Usuario confirmó EXPLÍCITAMENTE
// 3. Disponibilidad confirmada por backend
// 4. Todos los datos requeridos completos
```

**Reglas críticas:**
- NUNCA crear reservas especulativas
- NUNCA confirmar antes de que backend retorne success=true
- Si falla, la reserva NO se creó

### 4. BusinessRuleEngine (Autoridad de Negocio)

**Ubicación:** `src/Application/MimosBabySpa.Application/BusinessRules/`

Encapsula TODAS las reglas de negocio específicas del dominio.

```csharp
public interface IBusinessRuleEngine
{
    // Valida si una reserva puede crearse
    Task<BusinessRuleValidationResult> ValidateReservationAsync(
        Guid businessId,
        ConversationState state,
        CancellationToken cancellationToken);
    
    // Obtiene contexto de negocio para un cliente
    Task<BusinessRuleContext> GetBusinessContextAsync(
        Guid businessId,
        string phone,
        string? service,
        CancellationToken cancellationToken);
    
    // Valida un atributo de negocio
    BusinessRuleValidationResult ValidateBusinessAttribute(
        Guid businessId,
        string attributeName,
        string attributeValue);
}
```

**Responsabilidades:**
- Validar que el servicio existe y está activo
- Validar fechas (no pasadas, no muy lejanas)
- Validar horarios de operación
- Validar atributos de negocio (ejemplo: edad válida)
- Aplicar restricciones específicas del negocio

### 5. BusinessConfigurationProvider

**Ubicación:** `src/Application/MimosBabySpa.Application/Configuration/`

Proporciona configuración dinámica específica del negocio.

```csharp
public interface IBusinessConfigurationProvider
{
    // Campos requeridos para este negocio
    Task<RequiredFieldsConfiguration> GetRequiredFieldsAsync(
        Guid businessId,
        CancellationToken cancellationToken);
    
    // System prompt con información del negocio
    Task<string> GetSystemPromptAsync(
        Guid businessId,
        CancellationToken cancellationToken);
    
    // Catálogo de servicios
    Task<List<ServiceInfo>> GetServicesAsync(
        Guid businessId,
        CancellationToken cancellationToken);
    
    // Definición de atributos de negocio
    Task<Dictionary<string, AttributeDefinition>> GetBusinessAttributesAsync(
        Guid businessId,
        CancellationToken cancellationToken);
}
```

**Configuración dinámica incluye:**
- Campos requeridos (core + identity + business attributes)
- Catálogo de servicios
- Información del negocio
- Definición de atributos personalizados
- Reglas de validación

### 6. LLM Adapter Layer

**Ubicación:** `src/Application/MimosBabySpa.Application/LLM/`

Abstracción de comunicación con el LLM.

```csharp
public interface ILLMAdapter
{
    // Envía mensaje simple
    Task<LLMResponse> SendMessageAsync(
        LLMRequest request,
        CancellationToken cancellationToken);
    
    // Envía mensaje con function calling
    Task<LLMResponseWithTools> SendMessageWithToolsAsync(
        LLMRequest request,
        List<FunctionDefinition> availableFunctions,
        CancellationToken cancellationToken);
}
```

**Ventajas:**
- Permite cambiar proveedores (OpenAI, Azure, Anthropic)
- Aísla dependencias de SDK específico
- Facilita testing y mocking
- Centraliza retry logic y manejo de errores

### 7. HybridTransactionalOrchestrator

**Ubicación:** `src/Application/MimosBabySpa.Application/Orchestration/HybridTransactionalOrchestrator.cs`

El orquestador principal que une todos los componentes.

**Flujo de procesamiento:**

```
1. CARGAR ESTADO Y CONFIGURACIÓN
   ↓
2. EVALUAR FLUJO CON FLOW ENGINE
   ↓
3. LLAMAR AL LLM CON FUNCTION CALLING
   ↓
4. EJECUTAR TOOL CALLS (si existen)
   ↓
5. GUARDAR ESTADO ACTUALIZADO
   ↓
6. EVALUAR ESTADO FINAL
```

## Flujo Transaccional Completo

### Ejemplo: Usuario hace una reserva

```
Usuario: "Hola, quiero reservar un masaje para mi bebé"

┌─────────────────────────────────────────────────────────┐
│ 1. LLM entiende: "quiere reservar", "masaje", "bebé"   │
│    Llama: update_conversation_state("Service", "Masaje")│
└─────────────────────────────────────────────────────────┘
                         ↓
┌─────────────────────────────────────────────────────────┐
│ 2. FlowEngine evalúa:                                   │
│    - Service: ✓ (Masaje)                               │
│    - DesiredDate: ✗ (falta)                            │
│    - DesiredTime: ✗ (falta)                            │
│    CanCheckAvailability: false (falta fecha)           │
└─────────────────────────────────────────────────────────┘
                         ↓
Bot: "¡Perfecto! El masaje es excelente para bebés. 
     ¿Qué día te gustaría venir?"

Usuario: "El sábado a las 3pm"

┌─────────────────────────────────────────────────────────┐
│ 3. LLM extrae:                                          │
│    Llama: update_conversation_state("DesiredDate",     │
│           "2026-02-01")                                 │
│    Llama: update_conversation_state("DesiredTime",     │
│           "15:00")                                      │
└─────────────────────────────────────────────────────────┘
                         ↓
┌─────────────────────────────────────────────────────────┐
│ 4. FlowEngine evalúa:                                   │
│    - Service: ✓                                         │
│    - DesiredDate: ✓                                     │
│    - DesiredTime: ✓                                     │
│    CanCheckAvailability: TRUE                           │
│    Llama: check_availability()                          │
└─────────────────────────────────────────────────────────┘
                         ↓
┌─────────────────────────────────────────────────────────┐
│ 5. Backend verifica disponibilidad                      │
│    → AvailabilityService.CheckAvailabilityAsync()       │
│    → Retorna: is_available = true                       │
│    → Estado: AvailabilityConfirmed = TRUE               │
└─────────────────────────────────────────────────────────┘
                         ↓
Bot: "¡Excelente noticia! Hay disponibilidad el sábado 
     1 de febrero a las 3pm para el masaje. 
     ¿Confirmas la reserva?"

Usuario: "Sí, confirmo"

┌─────────────────────────────────────────────────────────┐
│ 6. LLM detecta confirmación explícita                   │
│    Estado: ReservationConfirmed = TRUE                  │
└─────────────────────────────────────────────────────────┘
                         ↓
┌─────────────────────────────────────────────────────────┐
│ 7. FlowEngine evalúa:                                   │
│    - Todos los datos: ✓                                 │
│    - AvailabilityConfirmed: ✓                           │
│    - ReservationConfirmed: ✓                            │
│    CanCreateReservation: TRUE                           │
│    Llama: create_reservation()                          │
└─────────────────────────────────────────────────────────┘
                         ↓
┌─────────────────────────────────────────────────────────┐
│ 8. Backend crea reserva                                 │
│    → ReservationService.CreateReservationAsync()        │
│    → Asigna empleado                                    │
│    → Valida recursos                                    │
│    → Retorna: success = true, reservationId = 123       │
│    → Estado: ReservationCreated = TRUE                  │
└─────────────────────────────────────────────────────────┘
                         ↓
Bot: "✓ Reserva confirmada exitosamente
     Servicio: Masaje
     Fecha: 01/02/2026
     Hora: 15:00
     ID de reserva: 123"
```

## Reglas de Seguridad Transaccional

### 1. Nunca Inventar Datos

```csharp
// ❌ MAL
if (state.Service == null) {
    state.Service = "Masaje Relajante"; // Inventar
}

// ✓ BIEN
if (state.Service == null) {
    // Pedir al usuario que especifique
    // O usar herramienta update_conversation_state
}
```

### 2. Nunca Prometer Disponibilidad

```
// ❌ MAL
Bot: "Perfecto, tenemos disponibilidad el sábado a las 3pm"
     [SIN haber llamado check_availability]

// ✓ BIEN
Bot: "Déjame verificar disponibilidad para el sábado a las 3pm..."
     [Llama check_availability]
     [Espera respuesta del backend]
Bot: "¡Sí hay disponibilidad! ¿Confirmas la reserva?"
```

### 3. Nunca Confirmar Reservas Sin Backend

```
// ❌ MAL
Bot: "¡Reserva confirmada!"
     [SIN haber llamado create_reservation]

// ✓ BIEN
[Llama create_reservation]
[Espera success = true del backend]
Bot: "✓ Reserva confirmada exitosamente. ID: 123"
```

### 4. Solo Valores Estructurados

```
// ❌ MAL
update_conversation_state("BabyAge", "tiene 6 meses")

// ✓ BIEN
update_conversation_state("Attribute:BabyAge", "6")
```

## Configuración Multi-Business

La arquitectura está diseñada para soportar múltiples negocios sin cambios de código:

```
BABY SPA:
- Atributos: BabyAge, BabyName, SpecialConditions
- Servicios: Masaje, Hidroterapia, Estimulación
- Duración típica: 45-60 min

RESTAURANT:
- Atributos: PartySize, DietaryRestrictions, SpecialOccasion
- Servicios: Breakfast, Lunch, Dinner
- Duración típica: 90-120 min

MEDICAL CLINIC:
- Atributos: Symptoms, Insurance, MedicalHistory
- Servicios: Consultation, Lab Work, Imaging
- Duración típica: 15-30 min
```

Todo se configura mediante:
1. System Prompt (específico del negocio)
2. BusinessConfiguration en BD
3. RequiredFieldsConfiguration
4. AttributeDefinitions

## Testing

### Unit Tests

```csharp
[Fact]
public void FlowEngine_CanCheckAvailability_ReturnsFalse_WhenServiceMissing()
{
    var state = new ConversationState { DesiredDate = DateOnly.Today };
    var result = flowEngine.CanCheckAvailability(state);
    Assert.False(result);
}

[Fact]
public void FlowEngine_CanCreateReservation_RequiresExplicitConfirmation()
{
    var state = new ConversationState
    {
        Service = "Masaje",
        DesiredDate = DateOnly.Today.AddDays(1),
        DesiredTime = new TimeOnly(15, 0),
        AvailabilityConfirmed = true,
        ReservationConfirmed = false // Sin confirmación
    };
    
    var result = flowEngine.CanCreateReservation(state);
    Assert.False(result);
}
```

### Integration Tests

```csharp
[Fact]
public async Task EndToEnd_UserMakesReservation_Success()
{
    // Simular conversación completa
    var response1 = await orchestrator.ProcessMessageAsync(
        businessId, "+123456789", "Quiero reservar masaje");
    
    var response2 = await orchestrator.ProcessMessageAsync(
        businessId, "+123456789", "Para mañana a las 3pm");
    
    var response3 = await orchestrator.ProcessMessageAsync(
        businessId, "+123456789", "Sí, confirmo");
    
    // Verificar que la reserva se creó
    var state = await stateManager.GetOrCreateStateAsync(businessId, "+123456789");
    Assert.True(state.ReservationCreated);
    Assert.NotNull(state.ReservationId);
}
```

## Monitoreo y Observabilidad

Puntos clave de logging:

```
FASE 1: Carga de estado
FASE 2: Evaluación de flujo
FASE 3: Llamada al LLM
FASE 4: Ejecución de herramientas
FASE 5: Guardado de estado
FASE 6: Evaluación final
```

Métricas recomendadas:
- Tiempo de respuesta por fase
- Tasa de tool calls por conversación
- Completitud promedio por etapa
- Tasa de conversión (intento → reserva creada)
- Errores de herramientas
- Tokens consumidos por conversación

## Migración desde Arquitectura Anterior

### Mapeo de Conceptos

| Anterior | Nuevo |
|----------|-------|
| ConversationSession | ConversationState |
| SalesStage | TransactionStage |
| IntentDetectorService | FlowEngine (sin análisis de texto) |
| ConversationOrchestrator | HybridTransactionalOrchestrator |
| ToolDispatcher | GenericToolDispatcher |
| Hardcoded business logic | BusinessConfigurationProvider |

### Estrategia de Migración

1. ✅ Crear nuevos componentes (sin tocar código existente)
2. ✅ Implementar nuevas interfaces
3. ⏳ Actualizar Program.cs para registrar nuevos servicios
4. ⏳ Crear flag feature toggle para A/B testing
5. ⏳ Migrar tráfico gradualmente
6. ⏳ Deprecar código antiguo

## Conclusión

La arquitectura **Hybrid Transactional Brain** proporciona:

✅ **Separación clara de responsabilidades**
- LLM: solo lenguaje natural
- FlowEngine: solo lógica de flujo
- Backend: solo autoridad de negocio

✅ **Domain-agnostic**
- Sin hardcoding de negocio
- Configurable dinámicamente
- Extensible a cualquier industria

✅ **Transaccionalmente seguro**
- Backend como autoridad absoluta
- Estado auditable y replayable
- Sin decisiones especulativas

✅ **Escalable y mantenible**
- Componentes desacoplados
- Testeable unitariamente
- Fácil de evolucionar

✅ **Production-ready**
- Manejo de errores robusto
- Logging completo
- Retry logic
- Timeout handling

Esta arquitectura está lista para sistemas transaccionales de nivel enterprise, donde cada reserva representa dinero real y cada error tiene consecuencias reales.

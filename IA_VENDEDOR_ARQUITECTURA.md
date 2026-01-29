# 🎯 ARQUITECTURA IA VENDEDOR ENTERPRISE

## 📋 RESUMEN EJECUTIVO

Se ha implementado una arquitectura completa de **"IA Vendedor"** que transforma el sistema de un bot conversacional básico a un **agente de ventas inteligente** con memoria persistente, máquina de estados y estrategia de ventas explícita.

---

## 🏗️ COMPONENTES IMPLEMENTADOS

### 1️⃣ DOMINIO (Domain Layer)

#### **Enums**
- ✅ `SalesStage.cs` - 10 etapas de venta (InitialContact → PostSale)
- ✅ `SalesTactic.cs` - 15 tácticas (BuildRapport, DirectClose, CreateUrgency, etc.)
- ✅ `CustomerSegment.cs` - 9 segmentos (New → VIPCustomer)
- ✅ `ResponseTone.cs` - 8 tonos (Friendly, Persuasive, Urgent, etc.)

#### **Value Objects**
- ✅ `SalesDecision.cs` - Decisión estratégica completa
- ✅ `ConversationGoal.cs` - Objetivo de conversación
- ✅ `ClosingAttempt.cs` - Registro de intento de cierre
- ✅ `StateTransition.cs` - Transición de estados

#### **Entidades**
- ✅ `ConversationSession.cs` - Estado volátil de sesión activa
- ✅ `CustomerProfile.cs` - Perfil persistente de largo plazo
- ✅ `SalesInteraction.cs` - Registro de interacciones para análisis

#### **Repositorios (Interfaces)**
- ✅ `IConversationSessionRepository`
- ✅ `ICustomerProfileRepository`
- ✅ `ISalesInteractionRepository`

---

### 2️⃣ APLICACIÓN (Application Layer)

#### **Session Management**
```
Application/Session/
├── ISessionManager.cs
└── SessionManager.cs
```
- Gestiona sesiones activas con expiración (30 minutos)
- Crea nuevas sesiones desde InitialContact
- Limpia sesiones expiradas

#### **Customer Profile**
```
Application/Profile/
├── ICustomerProfileService.cs
└── CustomerProfileService.cs
```
- Memoria de largo plazo del cliente
- Cálculo de probabilidad de conversión
- Registro de objeciones históricas
- Segmentación automática

#### **Sales Components**
```
Application/Sales/
├── ISalesStateMachine.cs
├── SalesStateMachine.cs        → Controla transiciones entre etapas
├── ISalesStrategyEngine.cs
├── SalesStrategyEngine.cs      → Decide tácticas y objetivos
├── IClosingEngine.cs
└── ClosingEngine.cs             → Motor especializado en cierre
```

#### **Prompts Dinámicos**
```
Application/Prompts/
├── IPromptBuilder.cs
└── DynamicPromptBuilder.cs     → Construye prompts contextuales
```

#### **Validación de Respuestas**
```
Application/Validation/
├── IResponseValidator.cs
└── SalesResponseValidator.cs   → Valida antes de enviar
```

#### **Orquestador Central**
```
Application/Orchestration/
├── IConversationOrchestrator.cs
└── ConversationOrchestrator.cs  → Director de orquesta
```

---

### 3️⃣ INFRAESTRUCTURA (Infrastructure Layer)

#### **Repositorios**
- ✅ `ConversationSessionRepository.cs`
- ✅ `CustomerProfileRepository.cs`
- ✅ `SalesInteractionRepository.cs`

#### **DbContext Actualizado**
- ✅ 3 nuevas tablas configuradas
- ✅ Índices optimizados
- ✅ Relaciones configuradas

#### **UnitOfWork Actualizado**
- ✅ Acceso a nuevos repositorios

---

## 🔄 FLUJO ARQUITECTÓNICO

```
┌─────────────────────────────────────────────────────────────────┐
│                   WEBHOOK (WhatsApp/otros)                       │
└────────────────────────┬────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│            CONVERSATION ORCHESTRATOR (Director)                  │
│                                                                  │
│  1. Cargar/Crear Sesión                                         │
│  2. Cargar Perfil de Cliente                                    │
│  3. Detectar Intención                                          │
│  4. Evaluar Transición de Estado                                │
│  5. Aplicar Estrategia de Ventas                                │
│  6. Construir Prompt Dinámico                                   │
│  7. Llamar LLM                                                  │
│  8. Validar Respuesta                                           │
│  9. Ejecutar Acciones Post-Respuesta                            │
│  10. Actualizar Sesión + Perfil                                 │
└────┬────────┬──────────┬──────────┬──────────┬─────────┬────────┘
     │        │          │          │          │         │
     ▼        ▼          ▼          ▼          ▼         ▼
┌─────────┐ ┌──────┐ ┌─────┐ ┌──────────┐ ┌────────┐ ┌────────┐
│ Session │ │Intent│ │Sales│ │ Customer │ │ Prompt │ │Response│
│ Manager │ │Detect│ │State│ │ Profile  │ │Builder │ │Validatr│
│         │ │      │ │Mach.│ │ Manager  │ │        │ │        │
└─────────┘ └──────┘ └─────┘ └──────────┘ └────────┘ └────────┘
     │          │        │          │            │         │
     ▼          ▼        ▼          ▼            ▼         ▼
┌─────────────────────────────────────────────────────────────────┐
│                   SALES STRATEGY ENGINE                          │
│  (Motor de reglas - define objetivos y tácticas)               │
└─────────────────────────────────────────────────────────────────┘
```

---

## 🎯 CARACTERÍSTICAS CLAVE

### ✅ Control Total del Backend
- La IA **NUNCA** decide lógica de negocio
- Todas las transiciones de estado controladas por reglas
- Tools disponibles filtradas proactivamente
- Validación de respuestas antes de enviar

### ✅ Máquina de Estados Explícita
```
InitialContact → Discovery → Presentation → AvailabilityExploration
    ↓                ↓            ↓                ↓
ObjectionHandling ←─────────────────────────────────┘
    ↓
Closing → Booking → Payment → PostSale
    ↓
Lost (si falla)
```

### ✅ Memoria Persistente
- **Sesión Activa**: Estado volátil (30 minutos)
- **Perfil Cliente**: Memoria de largo plazo (histórico completo)
- **Interacciones**: Log de todas las conversaciones

### ✅ Estrategia de Ventas Explícita
Cada etapa tiene:
- **Objetivo claro** (BuildRapport, DiscoverNeeds, CloseTheSale)
- **Táctica específica** (BuildRapport, AskDiscoveryQuestions, DirectClose)
- **Tono definido** (Friendly, Professional, Persuasive)
- **Call-to-Action obligatorio**
- **Puntos clave a incluir**

### ✅ Prompts 100% Dinámicos
Construidos en tiempo real con:
- Rol según etapa de venta
- Contexto del cliente (nombre, bebé, preferencias)
- Estado de sesión actual
- Objetivo específico
- Tácticas a aplicar
- Ejemplos de respuestas ideales

### ✅ Validación de Calidad
Antes de enviar cada respuesta se valida:
- ✓ Incluye Call-to-Action requerido
- ✓ Longitud apropiada (10-150 palabras)
- ✓ Tono correcto
- ✓ No cierre prematuro
- ✓ Dentro del dominio
- ✓ Incluye puntos clave

---

## 📊 EJEMPLO DE FLUJO COMPLETO

### Conversación 1 - Primera Interacción

```
Usuario: "Hola"
┌─────────────────────────────────────────────────┐
│ 1. Session Manager → Crear sesión nueva        │
│    Stage: InitialContact                        │
│ 2. Profile → Crear perfil (Segment: New)       │
│ 3. Intent Detector → SmallTalk                 │
│ 4. State Machine → Mantener InitialContact     │
│ 5. Strategy Engine → Táctica: BuildRapport     │
│    Goal: "Obtener nombre"                       │
│    Tone: Friendly                               │
│    CTA: "¿Cómo te llamas?"                      │
│ 6. Prompt Builder → Construir prompt dinámico  │
│ 7. AI → Generar respuesta                      │
│ 8. Validator → Verificar CTA incluido          │
│ 9. Post-Actions → Registrar interacción        │
│ 10. Save → Actualizar sesión + perfil          │
└─────────────────────────────────────────────────┘

Bot: "¡Hola! Qué gusto saludarte 😊. Soy María, tu asesora 
      de Mimos Baby Spa. Estoy aquí para ayudarte a 
      encontrar el mejor servicio para tu bebé. 
      ¿Cómo te llamas?"
```

### Conversación 2 - Descubrimiento

```
Usuario: "Me llamo Ana"
┌─────────────────────────────────────────────────┐
│ 1. Session Manager → Cargar sesión activa      │
│ 2. Profile → Cargar perfil (actualizar nombre) │
│ 3. Intent Detector → ProvideData               │
│ 4. State Machine → Transición a Discovery      │
│ 5. Strategy Engine → Táctica: AskDiscovery     │
│    Goal: "Conocer edad del bebé"               │
│    Tone: Professional                           │
│    CTA: "¿Cuántos meses tiene tu bebé?"        │
└─────────────────────────────────────────────────┘

Bot: "Perfecto Ana, me encantaría conocer más sobre tu bebé 
      para recomendarte lo mejor. ¿Cuántos meses tiene?"
```

### Conversación 3 - Presentación

```
Usuario: "Tiene 4 meses"
┌─────────────────────────────────────────────────┐
│ Profile → BabyAgeMonths = 4                    │
│ State Machine → Transición a Presentation      │
│ Strategy Engine → Táctica: EducateBenefits     │
│    Goal: "Presentar servicio ideal"            │
│    Tone: Educational                            │
│    KeyPoints: [beneficios específicos edad]    │
└─────────────────────────────────────────────────┘

Bot: "Qué hermoso Ana! A los 4 meses el Masaje Relajante 
      es perfecto para tu bebé. Ayuda con los cólicos, 
      mejora el sueño y fortalece el vínculo contigo. 
      ¿Te gustaría conocer los horarios disponibles?"
```

### Conversación 4 - Cierre

```
Usuario: "Sí, me interesa para mañana"
┌─────────────────────────────────────────────────┐
│ Auto-Extract → Date: mañana                    │
│ State Machine → Transición a Closing           │
│ Closing Engine → IsReadyForClosing = true      │
│ Strategy Engine → Táctica: AssumptiveClose     │
│    Goal: "Obtener confirmación explícita"      │
│    Tone: Persuasive                             │
│    CTA: "¿Confirmamos tu cita entonces?"       │
└─────────────────────────────────────────────────┘

Bot: "Perfecto Ana! Tenemos disponibilidad mañana a las 2pm 
      para el Masaje Relajante de tu bebé de 4 meses. 
      ¿Confirmamos tu cita entonces?"
```

---

## 🎲 DIFERENCIAS CLAVE vs SISTEMA ANTERIOR

| **Aspecto** | **Antes (Bot Básico)** | **Ahora (IA Vendedor)** |
|-------------|------------------------|-------------------------|
| **Objetivo** | Responder preguntas | **Perseguir cierre de venta** |
| **Estado** | Volátil, solo conversación | **Sesión + Perfil persistente** |
| **Control** | IA decide mucho | **Backend controla TODO** |
| **Prompts** | Semi-estáticos desde BD | **100% dinámicos en runtime** |
| **Estrategia** | Reactiva | **Proactiva con tácticas** |
| **Validación** | No existe | **Validación antes de enviar** |
| **Memoria** | Solo mensajes recientes | **Perfil de largo plazo** |
| **Cierre** | Pasivo | **Motor de cierre activo** |

---

## 🔧 CONFIGURACIÓN DE PROGRAM.CS

Se registraron **13 nuevos servicios**:

```csharp
// Repositorios
- IConversationSessionRepository
- ICustomerProfileRepository
- ISalesInteractionRepository

// Servicios
- ISessionManager
- ICustomerProfileService
- ISalesStateMachine
- ISalesStrategyEngine
- IClosingEngine
- IPromptBuilder
- IResponseValidator

// Orquestador
- IConversationOrchestrator (⭐ PUNTO DE ENTRADA PRINCIPAL)
```

---

## 📦 BASE DE DATOS

### Nuevas Tablas

#### **ConversationSessions**
```sql
- SessionId (PK)
- ConversationId (FK)
- BusinessId (FK)
- CustomerPhoneNumber
- CurrentStage (enum int)
- CurrentIntent (enum int)
- DesiredService, DesiredDate, DesiredTime
- CurrentGoalName, AppliedTactic
- ClosingAttempts, LastClosingAttemptAt
- ObjectionsRaisedJson, ObjectionsHandledJson
- IsActive, ExpiresAt
```

#### **CustomerProfiles**
```sql
- ProfileId (PK)
- BusinessId (FK)
- PhoneNumber (unique per business)
- CustomerName, Email
- Segment (enum int)
- LifetimeValue, TotalPurchases
- BabyName, BabyAgeMonths, BabyConditions
- PreferredServices, ServiceInterestScore
- CommonObjections, SuccessfulResponses
- ConversionProbability, ChurnRisk
```

#### **SalesInteractions**
```sql
- InteractionId (PK)
- SessionId (FK)
- ProfileId (FK)
- BusinessId (FK)
- Stage, TacticApplied, Tone
- UserMessage, BotResponse
- DetectedIntent
- WasSuccessful, ObjectionDetected
```

---

## 🚀 USO DEL SISTEMA

### Opción A: Usar el Orquestador Directamente (Recomendado)

```csharp
// En WhatsAppMessageProcessorService o cualquier handler
public class WhatsAppMessageProcessorService
{
    private readonly IConversationOrchestrator _orchestrator;
    
    public async Task<string> ProcessAsync(
        Guid businessId, 
        string phoneNumber, 
        string message)
    {
        // El orquestador hace TODO
        var response = await _orchestrator.ProcessMessageAsync(
            businessId, 
            phoneNumber, 
            message);
        
        return response;
    }
}
```

### Opción B: Mantener ConversationAgent (Compatibilidad)

El `ConversationAgent` existente sigue funcionando, pero ahora puedes:
1. Refactorizarlo para usar el orquestador internamente
2. Usarlo solo para casos legacy
3. Migrarlo gradualmente

---

## 📈 VENTAJAS DE LA NUEVA ARQUITECTURA

### 1. **Control Total**
- El backend decide cuándo pasar de etapa
- Las tools disponibles se filtran según contexto
- No hay "sorpresas" de la IA

### 2. **Memoria Real**
- El sistema recuerda al cliente entre conversaciones
- Perfil se enriquece con cada interacción
- Estrategia personalizada según historial

### 3. **Estrategia Explícita**
- Cada etapa tiene objetivo claro
- Tácticas específicas aplicadas
- Call-to-Action obligatorio

### 4. **Calidad Garantizada**
- Validación antes de enviar
- Tono correcto garantizado
- Sin respuestas fuera de dominio

### 5. **Analytics Incorporado**
- Todas las interacciones registradas
- Métricas por etapa de venta
- Identificación de objeciones comunes

### 6. **Extensibilidad**
- Fácil agregar nuevas tácticas
- Nuevas reglas de transición
- Personalización por negocio

---

## 🎓 PRÓXIMOS PASOS SUGERIDOS

### Fase 1: Migración
1. ✅ Crear migración de BD (ya creada)
2. Aplicar migración: `dotnet ef database update`
3. Poblar datos de configuración inicial

### Fase 2: Integración
1. Refactorizar `WhatsAppMessageProcessorService` para usar orquestador
2. Mantener ConversationAgent como fallback
3. Testing end-to-end

### Fase 3: Refinamiento
1. Ajustar reglas de transición según datos reales
2. Optimizar tácticas de cierre
3. A/B testing de prompts

### Fase 4: Analytics
1. Dashboard de métricas por etapa
2. Análisis de objeciones comunes
3. Identificación de cuellos de botella

### Fase 5: Personalización
1. Estrategias por segmento de cliente
2. Prompts específicos por negocio
3. Tácticas basadas en horario/día

---

## 📝 EJEMPLOS DE CONFIGURACIÓN

### Configurar Estrategia en SystemConfiguration

```sql
-- Template de prompt para etapa de Discovery
INSERT INTO SystemConfigurations (SystemConfigurationId, Value, Description)
VALUES (
    'DiscoveryPromptTemplate',
    'Eres un vendedor consultivo experto. Tu objetivo es {Goal}. Aplica la táctica: {Tactic}.',
    'Template de prompt para Discovery'
);
```

### Configurar Reglas de Negocio

```csharp
// En SalesStrategyEngine, personalizar por segmento
if (profile.Segment == CustomerSegment.VIPCustomer)
{
    decision.Tactic = SalesTactic.CreateScarcity;
    decision.Tone = ResponseTone.Urgent;
}
else if (profile.ConversionProbability < 0.3)
{
    decision.Tactic = SalesTactic.OfferTrial;
    decision.Tone = ResponseTone.Reassuring;
}
```

---

## ⚡ RENDIMIENTO Y ESCALABILIDAD

- **Sesiones en memoria** con expiración automática
- **Perfiles cacheables** por Redis (futuro)
- **Índices optimizados** en todas las tablas
- **Queries eficientes** con EF Core
- **Async/await** en todo el stack

---

## 🔒 SEGURIDAD Y CALIDAD

- ✅ Validación de respuestas pre-envío
- ✅ Límites de intentos de cierre (max 3)
- ✅ Timeout de sesiones (30 min)
- ✅ Todas las interacciones auditadas
- ✅ Sin lógica de negocio en prompts

---

## 📚 CÓDIGO DE REFERENCIA

### Usar el Orquestador

```csharp
var orchestrator = serviceProvider.GetRequiredService<IConversationOrchestrator>();

var response = await orchestrator.ProcessMessageAsync(
    businessId: myBusinessId,
    customerPhone: "+1234567890",
    userMessage: "Hola, quiero información",
    cancellationToken: cancellationToken
);

// El orquestador hace TODO automáticamente:
// - Carga/crea sesión
// - Detecta intent
// - Aplica estrategia
// - Construye prompt
// - Valida respuesta
// - Actualiza estado
```

### Consultar Perfil de Cliente

```csharp
var profileService = serviceProvider.GetRequiredService<ICustomerProfileService>();

var profile = await profileService.GetOrCreateProfileAsync(
    businessId,
    phoneNumber
);

Console.WriteLine($"Segmento: {profile.Segment}");
Console.WriteLine($"Probabilidad: {profile.ConversionProbability:P0}");
Console.WriteLine($"Total compras: {profile.TotalPurchases}");
```

### Forzar Transición de Estado

```csharp
var stateMachine = serviceProvider.GetRequiredService<ISalesStateMachine>();

var transition = await stateMachine.EvaluateTransitionAsync(
    session,
    profile,
    intent
);

if (transition.ShouldTransition)
{
    session.CurrentStage = transition.TargetStage;
    // ...
}
```

---

## 🎉 RESULTADO FINAL

Has transformado el sistema de:

**ANTES:**
- ❌ Bot reactivo que responde preguntas
- ❌ Sin memoria real del cliente
- ❌ Sin estrategia de ventas
- ❌ IA decide demasiado

**AHORA:**
- ✅ **IA Vendedor proactivo** que persigue cierre
- ✅ **Memoria de largo plazo** con perfiles persistentes
- ✅ **Estrategia explícita** con máquina de estados
- ✅ **Control total** del backend sobre lógica

---

## 📞 SOPORTE

Para extender el sistema:
1. **Nuevas etapas**: Agregar a `SalesStage` enum y reglas en `SalesStateMachine`
2. **Nuevas tácticas**: Agregar a `SalesTactic` enum y lógica en `SalesStrategyEngine`
3. **Nuevas validaciones**: Extender `SalesResponseValidator`
4. **Nuevos templates**: Actualizar `DynamicPromptBuilder`

---

**Implementación completada: Arquitectura IA Vendedor Enterprise** ✅

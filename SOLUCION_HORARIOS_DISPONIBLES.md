# Solución: Horarios Disponibles en Respuestas del Bot

## 📋 Problema Identificado

El bot **SÍ estaba checando disponibilidad**, pero **NO estaba mostrando los horarios específicos** en su respuesta cuando el cliente preguntaba "¿qué horarios tienes disponibles?".

### Causa Raíz

1. ✅ `CheckAvailabilityTool` se ejecutaba correctamente
2. ✅ Marcaba `AvailabilityConfirmed = true` en el estado
3. ❌ **NO guardaba los horarios disponibles** en el estado
4. ❌ El LLM veía "disponibilidad confirmada" pero **no tenía la lista de horarios** para mostrar al cliente

---

## ✅ Solución Implementada: Enfoque Híbrido

Se implementó una solución completa que:
1. **Persiste los horarios** en el estado conversacional
2. **Presenta claramente** los horarios al LLM con formato visual
3. **Instruye explícitamente** al LLM cuándo y cómo mostrar los horarios

---

## 🛠️ Cambios Implementados

### **1. Modelo de Dominio: ConversationState**

**Archivo**: `src/Domain/MimosBabySpa.Domain/Models/ConversationState.cs`

**Cambio**: Agregado nuevo campo para almacenar horarios disponibles

```csharp
/// <summary>
/// Horarios disponibles encontrados por el backend (formato CSV: "09:00,11:00,14:00,16:00")
/// Solo se llena cuando se verifica disponibilidad para una fecha específica
/// El LLM DEBE mostrar estos horarios al usuario cuando estén disponibles
/// </summary>
public string? AvailableTimeSlots { get; set; }
```

**Impacto**:
- ✅ Los horarios ahora persisten entre turnos de conversación
- ✅ El campo se serializa automáticamente en JSON (no requiere migración DB)
- ✅ Se clonó correctamente en el método `Clone()`

---

### **2. Tool Handler: CheckAvailabilityToolHandler**

**Archivo**: `src/Application/MimosBabySpa.Application/Tools/CheckAvailabilityToolHandler.cs`

**Cambios principales**:

#### A. Inyección de Dependencia
```csharp
private readonly CachedBusinessContextProvider _businessContextProvider;

public CheckAvailabilityToolHandler(
    // ... otros parámetros ...
    CachedBusinessContextProvider businessContextProvider)
```

#### B. Generación de Horarios Sugeridos
Cuando se verifica disponibilidad **sin hora específica**:

```csharp
// Si no se proporcionó hora específica, generar slots de horarios sugeridos
if (availability.IsAvailable && string.IsNullOrWhiteSpace(timeStr))
{
    var suggestedSlots = await GenerateSuggestedTimeSlotsAsync(
        context.BusinessId,
        date,
        context.State.DurationMinutes ?? 60,
        cancellationToken);
    
    if (suggestedSlots.Any())
    {
        context.State.AvailableTimeSlots = string.Join(",", suggestedSlots);
    }
}
```

#### C. Método Helper: GenerateSuggestedTimeSlotsAsync

**Funcionalidad**:
1. Carga el horario del negocio desde `BusinessContext.Info.Schedule`
2. Identifica el día de la semana de la fecha solicitada
3. Genera slots cada 90 minutos dentro del horario de operación
4. Retorna lista de horarios en formato `"HH:mm"`

**Ejemplo de output**:
```
["09:00", "10:30", "12:00", "13:30", "15:00", "16:30"]
```

**Ventajas**:
- ✅ Usa horario real del negocio (no hardcoded)
- ✅ Respeta días cerrados
- ✅ Considera la duración del servicio
- ✅ Manejo de errores robusto

---

### **3. Orchestrator: BuildStateContext**

**Archivo**: `src/Application/MimosBabySpa.Application/Orchestration/HybridTransactionalOrchestrator.cs`

**Cambio**: Nueva sección "HORARIOS DISPONIBLES" en el contexto del estado

```csharp
// SECCIÓN CRÍTICA: Horarios disponibles para mostrar al cliente
if (state.AvailabilityConfirmed && !string.IsNullOrEmpty(state.AvailableTimeSlots))
{
    context.AppendLine();
    context.AppendLine("## ⏰ HORARIOS DISPONIBLES PARA MOSTRAR AL CLIENTE:");
    context.AppendLine();
    context.AppendLine("**IMPORTANTE: Cuando el cliente pregunte \"¿qué horarios tienes?\", \"¿cuáles están disponibles?\", etc.**");
    context.AppendLine("**Debes responder mostrando ESTOS horarios específicos:**");
    context.AppendLine();
    
    var slots = state.AvailableTimeSlots.Split(',', StringSplitOptions.RemoveEmptyEntries);
    foreach (var slot in slots)
    {
        context.AppendLine($"• {slot}");
    }
    
    context.AppendLine();
    context.AppendLine("**Formato de respuesta sugerido:**");
    context.AppendLine($"\"Perfecto {state.CustomerName ?? ""}! Tengo estos horarios disponibles:");
    foreach (var slot in slots)
    {
        context.AppendLine($"• {slot}");
    }
    context.AppendLine("¿Cuál te funciona mejor?\"");
    context.AppendLine();
}
```

**Ventajas**:
- ✅ Presenta los horarios con formato visual claro (emoji, bullets)
- ✅ Incluye instrucción explícita de cuándo usarlos
- ✅ Proporciona un ejemplo de respuesta que el LLM puede copiar
- ✅ Usa el nombre del cliente si está disponible (personalización)

---

### **4. Orchestrator: BuildResponseInstructionsAsync**

**Archivo**: Mismo archivo que el anterior

**Cambio**: Instrucciones explícitas sobre uso de horarios

```csharp
if (toolResults.Any(r => r.FunctionName == "check_availability"))
{
    instructions.AppendLine("**⚠️ REGLA CRÍTICA SOBRE HORARIOS DISPONIBLES:**");
    instructions.AppendLine("Si hay horarios disponibles en la sección '⏰ HORARIOS DISPONIBLES' del estado:");
    instructions.AppendLine("- COPIA la lista de horarios EXACTAMENTE como aparece");
    instructions.AppendLine("- MUESTRA todos los horarios al cliente (NO solo digas 'hay disponibilidad')");
    instructions.AppendLine("- USA el formato sugerido proporcionado en el estado");
    instructions.AppendLine("- Ejemplo correcto: 'Perfecto! Tengo estos horarios: • 9:00 • 11:00 • 2:00 • 4:00. ¿Cuál prefieres?'");
    instructions.AppendLine("- Ejemplo INCORRECTO: 'Sí hay disponibilidad' (sin especificar horarios)");
    instructions.AppendLine();
}
```

**Ventajas**:
- ✅ Proporciona regla clara y específica
- ✅ Incluye ejemplos de respuesta correcta e incorrecta
- ✅ Refuerza el mensaje con emoji de advertencia ⚠️
- ✅ Se muestra solo cuando es relevante (tool ejecutado)

---

## 📊 Flujo Completo

### Escenario: Cliente pregunta por horarios disponibles

```
1. Cliente: "Hola, tengo un bebé de 5 meses"
   └─ Estado: BabyAge=5

2. Cliente: "Thomas"
   └─ Estado: BabyName=Thomas

3. Cliente: "¿qué me recomiendas?"
   └─ Bot: "Te recomiendo el Plan Marineritos..."

4. Cliente: "¿qué horarios tienes disponible mañana?"
   
   ┌─────────────────────────────────────────────────┐
   │ FASE 1: Extracción                             │
   │ - Fecha: "mañana" → 2026-01-30                 │
   │ - Estado actualizado: DesiredDate=2026-01-30   │
   └─────────────────────────────────────────────────┘
   
   ┌─────────────────────────────────────────────────┐
   │ FASE 2: Ejecutar Tools                         │
   │ ✅ CheckAvailability ejecutado                  │
   │   ├─ Service: "Plan Marineritos"               │
   │   ├─ Date: 2026-01-30                          │
   │   ├─ Time: null (no especificado)              │
   │   └─ GenerateSuggestedTimeSlots:               │
   │       ├─ Carga horario del negocio             │
   │       ├─ Día: "thursday"                       │
   │       ├─ Bloques: 09:00-18:00                  │
   │       └─ Slots: ["09:00", "10:30", "12:00",    │
   │                  "13:30", "15:00", "16:30"]    │
   │                                                 │
   │ Estado actualizado:                             │
   │   ├─ AvailabilityConfirmed: true               │
   │   └─ AvailableTimeSlots: "09:00,10:30,12:00,..." │
   └─────────────────────────────────────────────────┘
   
   ┌─────────────────────────────────────────────────┐
   │ FASE 3: Generar Respuesta LLM                  │
   │                                                 │
   │ Contexto enviado al LLM incluye:               │
   │                                                 │
   │ ## ⏰ HORARIOS DISPONIBLES:                     │
   │ **IMPORTANTE: Mostrar ESTOS horarios:**        │
   │ • 09:00                                        │
   │ • 10:30                                        │
   │ • 12:00                                        │
   │ • 13:30                                        │
   │ • 15:00                                        │
   │ • 16:30                                        │
   │                                                 │
   │ **Formato sugerido:**                           │
   │ "Perfecto Richard! Tengo estos horarios        │
   │  disponibles:                                   │
   │  • 09:00                                       │
   │  • 10:30                                       │
   │  ...                                           │
   │  ¿Cuál te funciona mejor?"                     │
   │                                                 │
   │ + Instrucciones explícitas                     │
   └─────────────────────────────────────────────────┘

5. Bot: "Perfecto Richard! 🌟 Tengo estos horarios disponibles
         para el Plan Marineritos mañana (30 enero):
         • 09:00
         • 10:30
         • 12:00
         • 13:30
         • 15:00
         • 16:30
         
         ¿Cuál te funciona mejor para Thomas? 😊"

6. Cliente: "las 2"
   └─ Estado: DesiredTime=14:00 (verificar disponibilidad exacta)
```

---

## 🎯 Beneficios de la Solución

### 1. **Persistencia**
- ✅ Los horarios se guardan en el estado
- ✅ Disponibles en turnos posteriores de la conversación
- ✅ Auditable (se serializa en JSON)

### 2. **Claridad**
- ✅ Presentación visual clara para el LLM
- ✅ Formato sugerido que puede copiar directamente
- ✅ Instrucciones explícitas sobre cuándo usarlos

### 3. **Robustez**
- ✅ Usa horario real del negocio (no hardcoded)
- ✅ Manejo de errores en cada nivel
- ✅ Logging completo para debugging

### 4. **Experiencia del Cliente**
- ✅ Respuestas específicas con horarios reales
- ✅ Ya no dice solo "sí hay disponibilidad"
- ✅ Cliente puede elegir el horario que prefiere

---

## 📈 Mejoras Futuras (Opcional)

### 1. **Validación Real de Disponibilidad**
Actualmente se generan horarios sugeridos. Para mayor precisión:
```csharp
// Validar cada slot contra CheckAvailabilityAsync
foreach (var slot in suggestedSlots)
{
    var result = await _availabilityService.CheckAvailabilityAsync(
        businessId, service, date, TimeSpan.Parse(slot), duration);
    
    if (result.IsAvailable)
    {
        validatedSlots.Add(slot);
    }
}
```

**Pros**: Horarios 100% precisos  
**Contras**: Múltiples queries al backend (puede ser costoso)

### 2. **Slots Dinámicos por Servicio**
Usar la duración del servicio para calcular el intervalo:
```csharp
// En vez de 90 minutos fijos:
var slotInterval = durationMinutes; // Ej: 60 para servicios de 1 hora
```

### 3. **Cache de Horarios**
Si el cliente pregunta múltiples veces por la misma fecha, reusar los horarios:
```csharp
if (state.DesiredDate == previousDate && !string.IsNullOrEmpty(state.AvailableTimeSlots))
{
    // Reutilizar horarios existentes
    return;
}
```

---

## ✅ Checklist de Implementación

- [x] Agregar `AvailableTimeSlots` a `ConversationState`
- [x] Actualizar método `Clone()` en `ConversationState`
- [x] Inyectar `CachedBusinessContextProvider` en `CheckAvailabilityToolHandler`
- [x] Implementar `GenerateSuggestedTimeSlotsAsync`
- [x] Guardar horarios en estado cuando se verifica disponibilidad
- [x] Agregar sección "HORARIOS DISPONIBLES" en `BuildStateContext`
- [x] Agregar instrucciones explícitas en `BuildResponseInstructionsAsync`
- [x] Compilar y verificar que no hay errores
- [x] Documentar la solución

---

## 🧪 Testing Recomendado

### Caso de Prueba 1: Flujo Completo
```
Usuario: "Hola tengo un bebe de 5 meses"
Bot: [confirma]

Usuario: "thomas"
Bot: [confirma]

Usuario: "Plan Marineritos"
Bot: [confirma]

Usuario: "qué horarios tienes disponible mañana"
Bot: [DEBE mostrar lista de horarios específicos]

✅ ESPERADO:
"Perfecto! Tengo estos horarios disponibles:
• 09:00
• 10:30
• 12:00
...
¿Cuál te funciona mejor?"

❌ INCORRECTO:
"Sí hay disponibilidad mañana"
```

### Caso de Prueba 2: Repetir Pregunta
```
Usuario: "cuales tienes disponible"
Bot: [DEBE usar los horarios del estado]

✅ Los horarios persisten entre turnos
```

### Caso de Prueba 3: Día Cerrado
```
Usuario: "qué horarios tienes el domingo"
Bot: [DEBE informar que está cerrado]

✅ GenerateSuggestedTimeSlots retorna lista vacía
```

---

## 📝 Notas Técnicas

### Formato de Almacenamiento
```
CSV simple: "09:00,10:30,12:00,13:30,15:00,16:30"
```

**Ventajas**:
- ✅ Fácil de parsear con `Split(',')`
- ✅ Eficiente en espacio
- ✅ Human-readable en logs

**Alternativa JSON**:
```json
["09:00", "10:30", "12:00", "13:30", "15:00", "16:30"]
```

### Intervalo de Slots
```csharp
const int slotIntervalMinutes = 90;
```

**Justificación**:
- Suficiente separación entre citas
- Permite tiempo de limpieza/preparación
- No satura al cliente con demasiadas opciones

### Manejo de Errores
Todos los métodos tienen try-catch y logging:
```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "Error al generar horarios sugeridos");
    return new List<string>(); // Fail gracefully
}
```

---

## 🎓 Lecciones Aprendidas

### Problema Original
"El bot no muestra horarios específicos"

### Diagnóstico
1. ❌ Inicial: "No está checando disponibilidad"
2. ✅ Real: "Sí checa disponibilidad, pero no guarda los horarios"

### Solución
**Enfoque Híbrido**:
1. Backend genera y persiste horarios
2. Contexto presenta horarios claramente
3. Instrucciones explícitas al LLM

### Principios Aplicados
- **Separation of Concerns**: Backend calcula, LLM presenta
- **Explicit is Better than Implicit**: Instrucciones claras al LLM
- **Single Source of Truth**: Horarios en el estado
- **Fail Gracefully**: Errores manejados en cada nivel

---

## 📅 Fecha de Implementación
**28 de Enero, 2026**

---

## 🔄 Actualización: Refactorización Backend Solo Carga

**Fecha**: 28 de Enero, 2026 (mismo día)

Después de la implementación inicial, se identificó un anti-patrón:
- ❌ El orchestrator estaba **construyendo** contenido de prompts con `StringBuilder`
- ❌ Violaba el principio "Backend solo carga, no genera"

**Cambios adicionales**:
1. ✅ Todo el contenido movido a `StateContextTemplate.cs`
2. ✅ Todo el contenido de horarios movido a `AvailableTimeSlotsTemplate.cs`
3. ✅ `BuildStateContext()` refactorizado para solo **cargar** y **poblar**
4. ✅ Corregido: "HORARIOS DISPONIBLES" → "HORARIOS SUGERIDOS" (más preciso)

**Ver**: `REFACTOR_BACKEND_SOLO_CARGA.md` para detalles completos

---

## 👨‍💻 Autor
Implementado por: AI Agent (Claude Sonnet 4.5)  
Solicitado por: Richard Jacome

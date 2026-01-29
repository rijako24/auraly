# Refactorización: Backend Solo Carga (No Construye)

## 🎯 Principio Fundamental

> **El backend SOLO carga prompts, NO genera contenido**

Todo el contenido textual (instrucciones, formato, explicaciones) debe estar en **clases estáticas de prompt**. El backend (orchestrator) solo debe:
1. ✅ **Cargar** templates estáticos
2. ✅ **Poblar** placeholders con datos dinámicos
3. ❌ **NUNCA** construir contenido hardcoded con `StringBuilder`

---

## 🐛 Problemas Identificados

### **1. Violación de Separation of Concerns**

**ANTES** (Anti-patrón):
```csharp
// ❌ Backend construyendo contenido hardcoded
context.AppendLine("## ⏰ HORARIOS DISPONIBLES PARA MOSTRAR AL CLIENTE:");
context.AppendLine("**IMPORTANTE: Cuando el cliente pregunte...**");
context.AppendLine("**Debes responder mostrando ESTOS horarios específicos:**");
// ... más contenido hardcoded ...
```

**Problemas**:
- ❌ Contenido de prompt disperso en código C#
- ❌ Difícil de modificar sin recompilar
- ❌ Viola SRP (Single Responsibility Principle)
- ❌ Backend asumiendo rol de "creador de prompts"

---

### **2. Información Incorrecta sobre Disponibilidad**

**ANTES**:
```csharp
context.AppendLine("## ⏰ HORARIOS DISPONIBLES PARA MOSTRAR AL CLIENTE:");
```

**Problema**:
```csharp
var availability = await _availabilityService.CheckAvailabilityAsync(...);
// availability.IsAvailable = true
```

❌ **`IsAvailable = true` NO significa que TODOS los horarios estén disponibles**
- Solo significa: "Hay disponibilidad GENERAL en ese día"
- Algunos horarios pueden estar ocupados
- El título "HORARIOS DISPONIBLES" es engañoso

**AHORA**:
```csharp
## ⏰ HORARIOS SUGERIDOS PARA ESTE DÍA

**IMPORTANTE**: Estos son horarios SUGERIDOS basados en el horario de operación.
Algunos pueden estar ocupados. Cuando el cliente elija, se verificará automáticamente.
```

✅ **Información precisa y honesta**

---

## ✅ Solución Implementada

### **Arquitectura Refactorizada**

```
┌─────────────────────────────────────────────────────────┐
│ Prompts/Templates/ (CLASES ESTÁTICAS)                  │
│                                                         │
│ ├─ StateContextTemplate.cs                             │
│ │  └─ Contiene TODO el contenido del estado           │
│ │     (headers, labels, formato)                       │
│ │                                                       │
│ └─ AvailableTimeSlotsTemplate.cs                       │
│    └─ Contiene TODO el contenido de horarios          │
│       (explicación, instrucciones, formato)            │
└─────────────────────────────────────────────────────────┘
                         │
                         │ Load & Populate
                         ▼
┌─────────────────────────────────────────────────────────┐
│ HybridTransactionalOrchestrator.cs (BACKEND)           │
│                                                         │
│ BuildStateContext():                                    │
│   1. CARGA templates estáticos                         │
│   2. POPULA placeholders con datos                     │
│   3. NO construye contenido                            │
└─────────────────────────────────────────────────────────┘
```

---

## 📁 Nuevos Archivos Creados

### **1. ResponseInstructionsTemplate.cs**

**Ubicación**: `src/Application/MimosBabySpa.Application/Prompts/Templates/ResponseInstructionsTemplate.cs`

**Responsabilidad**: Contener TODAS las instrucciones para generar respuestas

**Secciones**:
- `Header`: "# INSTRUCCIONES PARA GENERAR RESPUESTA"
- `BaseInstructions`: Instrucciones base (personalidad, confirmación, progreso, etc.)
- `CheckAvailabilityInstructions`: Reglas críticas sobre horarios
- `CreateReservationInstructions`: Instrucciones cuando se crea reserva
- `MissingFieldsInstructions`: Qué hacer cuando faltan campos
- `AmbiguitiesInstructions`: Cómo manejar ambigüedades
- `InformationQueryInstructions`: Responder consultas informativas
- `FinalReminder`: Recordatorio de tono y brevedad

**Ejemplo**:
```csharp
public const string CheckAvailabilityInstructions = @"
**⚠️ REGLA CRÍTICA SOBRE HORARIOS DISPONIBLES:**
Si hay horarios disponibles en la sección '⏰ HORARIOS DISPONIBLES' del estado:
- COPIA la lista de horarios EXACTAMENTE como aparece
- MUESTRA todos los horarios al cliente (NO solo digas 'hay disponibilidad')
- USA el formato sugerido proporcionado en el estado
- Ejemplo correcto: 'Perfecto! Tengo estos horarios: • 9:00 • 11:00 • 2:00 • 4:00. ¿Cuál prefieres?'
- Ejemplo INCORRECTO: 'Sí hay disponibilidad' (sin especificar horarios)";
```

**Ventajas**:
- ✅ Instrucciones centralizadas y reutilizables
- ✅ Fácil A/B testing de instrucciones
- ✅ Backend solo carga según contexto
- ✅ Modificable sin tocar lógica de orquestación

---

### **2. StateContextTemplate.cs**

**Ubicación**: `src/Application/MimosBabySpa.Application/Prompts/Templates/StateContextTemplate.cs`

**Responsabilidad**: Contener TODO el formato de presentación del estado conversacional

**Secciones**:
- `Header`: Encabezado principal
- `CompletenessSection`: Completitud del estado
- `InformationSection`: Información recolectada (con placeholders)
- `AttributesSection`: Atributos específicos del negocio
- `MissingFieldsSection`: Campos faltantes
- `FlowStateSection`: Estado del flujo

**Ejemplo**:
```csharp
public const string InformationSection = @"
## Información recolectada:
- Nombre del cliente: {customer_name}
- Teléfono: {phone}
- Email: {email}
- Servicio: {service}
- Fecha deseada: {desired_date}
- Hora deseada: {desired_time}
- Disponibilidad confirmada: {availability_confirmed}
- Reserva confirmada por usuario: {reservation_confirmed}
- Reserva creada: {reservation_created}";
```

**Ventajas**:
- ✅ Todo el contenido en un solo lugar
- ✅ Fácil de modificar sin recompilar backend
- ✅ Placeholders claros y documentados
- ✅ Versionable y auditable

---

### **2. AvailableTimeSlotsTemplate.cs**

**Ubicación**: `src/Application/MimosBabySpa.Application/Prompts/Templates/AvailableTimeSlotsTemplate.cs`

**Responsabilidad**: Contener TODO el contenido sobre horarios sugeridos

**Características**:
1. **Header**: Título preciso ("HORARIOS SUGERIDOS")
2. **Explanation**: Aclara que son sugeridos, no confirmados
3. **WhenToShow**: Instrucciones de cuándo mostrarlos
4. **TimeSlotsList**: Formato de la lista
5. **ResponseFormat**: Ejemplo de respuesta
6. **Build()**: Método helper para construcción completa

**Ejemplo**:
```csharp
public const string Explanation = @"
**IMPORTANTE**: Estos son horarios SUGERIDOS basados en el horario de operación del negocio.
Algunos pueden estar ocupados. Cuando el cliente elija un horario específico, se verificará
la disponibilidad exacta automáticamente.";
```

**Advertencias Incluidas**:
```csharp
**IMPORTANTE**: 
- NO digas "todos están disponibles" (algunos pueden estar ocupados)
- Cuando el cliente elija, se verificará disponibilidad automáticamente
- Si el horario elegido está ocupado, sugerirás otro de la lista
```

**Ventajas**:
- ✅ Información precisa y honesta
- ✅ Maneja expectativas correctamente
- ✅ Incluye instrucciones de manejo de errores
- ✅ Método `Build()` para mayor comodidad

---

## 🔧 Cambios en HybridTransactionalOrchestrator.cs

### **ANTES (Anti-patrón)**:
```csharp
private string BuildStateContext(...)
{
    var context = new StringBuilder();
    
    // ❌ Construyendo contenido hardcoded
    context.AppendLine("# ESTADO ACTUAL DE LA CONVERSACIÓN");
    context.AppendLine();
    context.AppendLine($"## Completitud: {flowEvaluation.CompletenessPercentage}%");
    context.AppendLine();
    context.AppendLine("## Información recolectada:");
    context.AppendLine($"- Nombre del cliente: {state.CustomerName ?? "NO RECOLECTADO"}");
    // ... más hardcode ...
    
    if (state.AvailabilityConfirmed && !string.IsNullOrEmpty(state.AvailableTimeSlots))
    {
        // ❌ Construyendo sección de horarios con hardcode
        context.AppendLine("## ⏰ HORARIOS DISPONIBLES PARA MOSTRAR AL CLIENTE:");
        context.AppendLine("**IMPORTANTE: Cuando el cliente pregunte...**");
        // ... más hardcode ...
    }
    
    return context.ToString();
}
```

---

### **AHORA (Correcto)**:
```csharp
/// <summary>
/// Construye el contexto de estado cargando templates y poblando datos dinámicos.
/// El backend SOLO carga y popula, NO construye contenido.
/// </summary>
private string BuildStateContext(...)
{
    var context = new StringBuilder();
    
    // ✅ CARGA template de header
    context.AppendLine(StateContextTemplate.Header);
    
    // ✅ CARGA y POPULA template de completitud
    context.AppendLine(
        StateContextTemplate.CompletenessSection
            .Replace("{completeness_percentage}", 
                     flowEvaluation.CompletenessPercentage.ToString()));
    
    // ✅ CARGA y POPULA template de información
    context.AppendLine(
        StateContextTemplate.InformationSection
            .Replace("{customer_name}", state.CustomerName ?? "NO RECOLECTADO")
            .Replace("{phone}", state.Phone ?? "NO RECOLECTADO")
            .Replace("{email}", state.Email ?? "NO RECOLECTADO")
            // ... más placeholders ...
    );

    // ✅ CARGA template de horarios (si aplica)
    if (state.AvailabilityConfirmed && !string.IsNullOrEmpty(state.AvailableTimeSlots))
    {
        var slots = state.AvailableTimeSlots.Split(',', StringSplitOptions.RemoveEmptyEntries);
        context.AppendLine(
            AvailableTimeSlotsTemplate.Build(
                state.CustomerName ?? string.Empty,
                slots));
    }
    
    // ... más secciones con el mismo patrón ...
    
    return context.ToString();
}
```

**Patrón aplicado**:
1. ✅ **Cargar** template estático
2. ✅ **Reemplazar** placeholders con datos dinámicos
3. ✅ **Agregar** al contexto

---

## 📊 Comparación Antes/Después

### **Responsabilidades**

| Aspecto | ANTES ❌ | AHORA ✅ |
|---------|----------|----------|
| **Contenido de prompts** | Disperso en C# | Centralizado en Templates |
| **Formato visual** | Hardcoded en orchestrator | En clases estáticas |
| **Instrucciones al LLM** | Construidas dinámicamente | Cargadas desde templates |
| **Modificación de texto** | Requiere recompilación | Solo editar template |
| **Rol del backend** | Construye prompts | Solo carga y popula |
| **Testability** | Difícil (lógica mezclada) | Fácil (separación clara) |

---

### **Mantenibilidad**

**ANTES**:
```
Para cambiar instrucciones:
1. Buscar código en orchestrator ❌
2. Editar strings hardcoded ❌
3. Recompilar backend ❌
4. Re-deploy ❌
```

**AHORA**:
```
Para cambiar instrucciones:
1. Editar template estático ✅
2. Recompilar backend ✅
3. Re-deploy ✅
(Mucho más localizado)
```

---

## 🎯 Principios Aplicados

### **1. Separation of Concerns**
```
- Templates: Contenido de prompts
- Backend: Lógica de orquestación + poblar datos
```

### **2. Single Responsibility Principle**
```
- StateContextTemplate: Responsable del formato del estado
- AvailableTimeSlotsTemplate: Responsable del formato de horarios
- HybridTransactionalOrchestrator: Responsable de orquestar + cargar
```

### **3. Don't Repeat Yourself (DRY)**
```
- Contenido definido UNA VEZ en templates
- Reutilizable en múltiples lugares
```

### **4. Open/Closed Principle**
```
- Abierto para extensión: Agregar nuevos templates
- Cerrado para modificación: Backend no cambia
```

### **5. Template Method Pattern**
```
- Templates definen estructura
- Backend proporciona datos
```

---

## ✅ Beneficios de la Refactorización

### **1. Claridad**
- ✅ Contenido de prompts centralizado
- ✅ Backend solo carga (responsabilidad clara)
- ✅ Fácil de entender qué hace cada parte

### **2. Mantenibilidad**
- ✅ Cambios de contenido localizados
- ✅ No hay búsqueda de strings hardcoded
- ✅ Templates versionables en git

### **3. Precisión**
- ✅ "HORARIOS SUGERIDOS" (no "DISPONIBLES")
- ✅ Expectativas claras sobre disponibilidad
- ✅ Instrucciones de manejo de errores incluidas

### **4. Testability**
- ✅ Templates son fáciles de testear (strings estáticos)
- ✅ Backend logic separada del contenido
- ✅ Mocking más sencillo

### **5. Escalabilidad**
- ✅ Agregar nuevos templates sin tocar backend
- ✅ Multi-tenancy: templates por negocio (futuro)
- ✅ A/B testing de prompts (futuro)

---

## 🔮 Mejoras Futuras

### **1. Templates por Negocio (Multi-tenant)**
```csharp
// En vez de templates globales:
StateContextTemplate.InformationSection

// Templates específicos por negocio:
await _templateProvider.LoadAsync(businessId, "StateContext.Information")
```

### **2. Localización (i18n)**
```csharp
// Templates en múltiples idiomas:
StateContextTemplate.InformationSection_ES // Español
StateContextTemplate.InformationSection_EN // Inglés
StateContextTemplate.InformationSection_PT // Português
```

### **3. Versionado de Templates**
```csharp
// Para A/B testing:
StateContextTemplate_V1.InformationSection
StateContextTemplate_V2.InformationSection
```

### **4. Templates en Base de Datos**
```csharp
// Para cambios sin re-deploy:
await _templateRepository.GetByKeyAsync("StateContext.Information")
```

---

## 📝 Checklist de Refactorización

- [x] Crear `StateContextTemplate.cs`
- [x] Crear `AvailableTimeSlotsTemplate.cs`
- [x] Crear `ResponseInstructionsTemplate.cs` ⭐ NUEVO
- [x] Refactorizar `BuildStateContext()` para cargar templates
- [x] Refactorizar `BuildResponseInstructionsAsync()` para cargar templates ⭐ NUEVO
- [x] Eliminar TODO el contenido hardcoded del orchestrator
- [x] Corregir "DISPONIBLES" → "SUGERIDOS"
- [x] Agregar explicación sobre verificación automática
- [x] Implementar método `Build()` en AvailableTimeSlotsTemplate
- [x] Compilar y verificar (0 errores)
- [x] Documentar la refactorización

---

## 🎓 Lecciones Aprendidas

### **1. Backend NO es Creador de Prompts**
El backend debe **orquestar** y **poblar datos**, no **crear contenido**.

### **2. Información Precisa > Información Optimista**
"HORARIOS SUGERIDOS" (preciso) > "HORARIOS DISPONIBLES" (engañoso)

### **3. Separation of Concerns es Crítica**
Mezclar contenido con lógica = mantenibilidad destruida

### **4. Templates Estáticos son Poderosos**
- Fáciles de modificar
- Fáciles de testear
- Fáciles de versionar

---

## 📅 Fecha de Implementación
**28 de Enero, 2026**

### Actualizaciones:
- ✅ **Primera fase**: StateContextTemplate + AvailableTimeSlotsTemplate
- ✅ **Segunda fase**: ResponseInstructionsTemplate (completado mismo día)

---

## 👨‍💻 Autor
Implementado por: AI Agent (Claude Sonnet 4.5)  
Solicitado por: Richard Jacome

---

## 🔗 Documentos Relacionados

- `SOLUCION_HORARIOS_DISPONIBLES.md` - Implementación original
- `ARQUITECTURA_PROMPTS_OPTIMIZADA.md` - Arquitectura v3.0
- `REFACTOR_COMPLETE_TEMPLATE_SEPARATION.md` - Separación de templates anterior

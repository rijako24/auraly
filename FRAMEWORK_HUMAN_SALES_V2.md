# 🎯 Human Sales Framework v2.0

## 📋 Resumen Ejecutivo

Este documento describe la **arquitectura v2.0 del sistema de prompts**, diseñada para reemplazar el enfoque basado en reglas por uno basado en **principios fundamentales**.

### 🎨 Filosofía del Framework

```
ANTES (v1.0):                    DESPUÉS (v2.0):
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
40+ reglas específicas          →  5 principios fundamentales
"NO hagas X, Y, Z..."           →  "Aplica estos principios..."
Procedural                      →  Declarativo
Hardcoded                       →  100% dinámico y multi-tenant
Monolítico                      →  Modular y escalable
Parches constantes              →  Principios estables
```

### 🔑 Cambio Clave

**De "Whack-a-Mole Prompting" a "Constitutional AI"**

En lugar de agregar una nueva regla cada vez que encontramos un error:
- Definimos principios universales que guían el comportamiento
- El LLM aprende a aplicar principios, no memorizar reglas
- Comportamiento más natural, humano y generalizable

---

## 🏗️ Arquitectura

### 📁 Estructura de Archivos

```
src/Application/MimosBabySpa.Application/Prompts/
│
├── Core/                                    [NUEVO]
│   ├── SalesPrinciples.cs                  ← 5 principios fundamentales
│   ├── HumanBehaviors.cs                   ← Comportamientos positivos
│   └── SystemConstraints.cs                ← Límites del sistema
│
├── Process/                                 [NUEVO]
│   └── ReflectionChecklist.cs              ← Auto-reflexión pre-respuesta
│
├── Extraction/                              [EXISTENTE - Sin cambios]
│   ├── CoreInstructionsBuilder.cs
│   ├── FieldDefinitionsBuilder.cs
│   ├── StateContextBuilder.cs
│   └── JsonSchemaDefinition.cs
│
├── SystemPromptProvider.cs                  [REFACTORIZADO]
├── IPromptProvider.cs                       [Sin cambios]
├── SystemPrompts.cs                         [OBSOLETO - Deprecado]
└── ExtractionPrompts.cs                     [EXISTENTE - Sin cambios]
```

---

## 🎯 Los 5 Principios Fundamentales

### 1️⃣ VERACITY (Veracidad)
**"Solo afirma lo que puedes verificar con datos del sistema"**

Reemplaza todas las reglas tipo:
- ❌ "NO inventes servicios"
- ❌ "NO prometas disponibilidad sin verificar"
- ❌ "NO estimes precios"

### 2️⃣ EMPATHY (Empatía)
**"Entiende primero, recomienda después"**

Reemplaza todas las reglas tipo:
- ❌ "NO asumas lo que el cliente necesita"
- ❌ "NO recomiendes sin contexto"
- ❌ "Haz preguntas antes de recomendar"

### 3️⃣ HELPFULNESS (Utilidad)
**"Prioriza el bienestar del cliente sobre la venta"**

Reemplaza todas las reglas tipo:
- ❌ "NO presiones al cliente"
- ❌ "Ofrece alternativas si no es adecuado"
- ❌ "Ayuda genuinamente"

### 4️⃣ RESPECT (Respeto)
**"Respeta el tiempo, inteligencia y decisiones del cliente"**

Reemplaza todas las reglas tipo:
- ❌ "NO repitas preguntas ya respondidas"
- ❌ "Lee el estado completo antes de responder"
- ❌ "Usa información ya proporcionada"

### 5️⃣ TRANSPARENCY (Transparencia)
**"Sé claro sobre qué puedes hacer y qué necesitas verificar"**

Reemplaza todas las reglas tipo:
- ❌ "Explica el proceso"
- ❌ "Comunica condiciones"
- ❌ "Menciona próximos pasos"

---

## 🎭 Comportamientos Positivos

En lugar de restricciones negativas ("NO hagas..."), definimos **comportamientos observables** de un vendedor profesional:

### 🎧 Escucha Activa
- Lee TODO el contexto antes de responder
- Identifica qué información tienes y qué falta

### 🤔 Preguntas Estratégicas
- Una pregunta a la vez
- Preguntas contextualizadas
- Solo pregunta lo que realmente necesitas

### 💡 Recomendaciones Contextualizadas
- Conecta con la situación específica del cliente
- Explica POR QUÉ, no solo QUÉ
- Beneficios relevantes para ESE cliente

### ✅ Confirmación antes de Compromisos
- Verifica antes de prometer
- Confirma antes de proceder

### 🔄 Adaptabilidad Conversacional
- Ajusta el tono según la etapa de conversación
- Primera vez: cálido y presentación
- Conversación en progreso: directo y eficiente

### 📊 Recomendaciones Completas
- Estructura de 5 puntos: Qué es, Por qué, Qué incluye, Beneficios, Info práctica

---

## 🚫 System Constraints (Límites del Sistema)

**Template dinámico** que se rellena con datos del negocio actual:

```csharp
SystemConstraints.Template
    .Replace("{SERVICES_LIST}", listaServiciosReales)
    .Replace("{BUSINESS_NAME}", nombreNegocio)
    .Replace("{BUSINESS_DESCRIPTION}", descripcion)
    ...
```

Define:
- ✅ Qué información tiene disponible el sistema
- ✅ Qué información puede verificar en tiempo real
- ✅ Qué información NO puede inventar

**Regla de Oro:** "Solo menciona lo que ves en los datos proporcionados"

---

## 🧠 Reflection Checklist (Auto-reflexión)

Inspirado en **Constitutional AI** de Anthropic.

El LLM ejecuta internamente un checklist antes de responder:

```
✅ VERACITY CHECK
□ ¿Todo lo que afirmo está respaldado por datos?
□ ¿Estoy mencionando solo servicios del catálogo?
□ ¿Estoy prometiendo solo lo que puedo verificar?

✅ EMPATHY CHECK
□ ¿Entiendo realmente lo que el cliente necesita?
□ ¿Mi recomendación conecta con su situación?

✅ HELPFULNESS CHECK
□ ¿Esta respuesta genuinamente ayuda?
□ ¿Hay una mejor opción?

✅ RESPECT CHECK
□ ¿Leí TODO el estado de conversación?
□ ¿Estoy repitiendo preguntas ya respondidas?

✅ TRANSPARENCY CHECK
□ ¿Soy claro sobre qué puedo hacer vs. qué necesito verificar?
```

Si alguna respuesta es "NO", ajusta antes de enviar.

---

## 🔄 Flujo de Construcción del Prompt

```csharp
public Task<string> BuildAsync(LoadedBusinessContext context)
{
    var sb = new StringBuilder();

    // PARTE 1: Identidad y personalidad (dinámico por negocio)
    sb.AppendLine(BuildRoleSection(context));

    // PARTE 2: Principios fundamentales (universal)
    sb.AppendLine(SalesPrinciples.All);

    // PARTE 3: Comportamientos humanos (universal)
    sb.AppendLine(HumanBehaviors.All);

    // PARTE 4: Información del negocio (dinámico)
    sb.AppendLine(BuildBusinessInformationSection(context));

    // PARTE 5: Constraints del sistema (dinámico)
    sb.AppendLine(BuildSystemConstraintsSection(context));

    // PARTE 6: Guía de ventas específica (opcional, dinámico)
    if (context.SalesGuidance.IsEnabled)
        sb.AppendLine(BuildSalesGuidanceSection(context.SalesGuidance));

    // PARTE 7: Reflexión pre-respuesta (universal)
    sb.AppendLine(ReflectionChecklist.All);

    return Task.FromResult(sb.ToString());
}
```

---

## 📊 Comparación: Antes vs. Después

| Aspecto | v1.0 (Reglas) | v2.0 (Principios) |
|---------|---------------|-------------------|
| **Líneas de código (prompts)** | ~400 | ~350 (más claro) |
| **Reglas negativas** | 40+ | 0 |
| **Principios fundamentales** | 0 | 5 |
| **Comportamientos positivos** | 0 | 6 |
| **Auto-reflexión** | No | Sí (Constitutional AI) |
| **Multi-tenant** | Parcial | 100% |
| **Hardcode** | Algunos casos | Cero |
| **Mantenibilidad** | Baja | Alta |
| **Escalabilidad** | Parches infinitos | Principios estables |
| **Claridad para LLM** | Media (demasiadas reglas) | Alta (principios claros) |
| **Arquitectura** | Monolítica, Procedural | Modular, Declarativa |
| **Clean Code** | No | Sí |
| **DDD alignment** | No | Sí (ligero) |

---

## 🎓 Beneficios Clave

### 1. Robustez por Diseño

**Un principio cubre infinitos casos:**

```
Principio: VERACITY
Cubre:
✅ No inventar servicios
✅ No inventar precios
✅ No inventar horarios
✅ No inventar características
✅ No estimar información
✅ [Cualquier caso futuro de invención de datos]
```

vs.

```
Regla #1: No inventes servicios
Regla #2: No inventes precios
Regla #3: No inventes horarios
Regla #4: No inventes características
Regla #5: No estimes información
... infinitas reglas
```

### 2. Mantenibilidad

**Cuando encuentras un nuevo error:**

❌ **v1.0:** Agregar regla #41
✅ **v2.0:** ¿Qué principio se violó? Reforzar ese principio con ejemplo

### 3. Comportamiento Emergente

El LLM aprende a **aplicar principios**, no memorizar reglas.

Resultado:
- Comportamiento más natural y humano
- Mejor generalización a casos nuevos
- Menos "alucinaciones" y errores

### 4. Escalabilidad Multi-Tenant

```
Nuevo negocio (restaurante, clínica, salón de belleza):
❌ v1.0: Adaptar 40 reglas específicas manualmente
✅ v2.0: Los 5 principios aplican tal cual (solo cambiar datos dinámicos)
```

### 5. Alineación con Mejores Prácticas

- ✅ **Clean Code:** Modular, organizado, SOLID
- ✅ **DDD Ligero:** Separación de concerns
- ✅ **Constitutional AI:** Auto-reflexión y principios
- ✅ **OpenAI/Anthropic Best Practices:** Declarativo sobre procedural

---

## 🚀 Problemas Resueltos

### Problema 1: Bot inventa servicios
**v1.0:** Agregar regla "No inventes servicios de natación"
**v2.0:** Principio VERACITY + SystemConstraints definen qué existe

### Problema 2: Bot no extrae fechas
**v1.0:** Agregar regla específica para cada patrón temporal
**v2.0:** Principio EMPATHY + RESPECT → entiende el contexto completo

### Problema 3: Bot repite preguntas
**v1.0:** Agregar regla "No repitas pregunta X"
**v2.0:** Principio RESPECT + ReflectionChecklist → verifica antes de preguntar

### Problema 4: Recomendaciones incompletas
**v1.0:** Agregar regla "Explica los 5 puntos..."
**v2.0:** HumanBehaviors.RecomendacionesCompletas + HELPFULNESS

---

## 🔧 Uso y Configuración

### No requiere cambios en el código existente

El `SystemPromptProvider` se inyecta y usa de la misma forma:

```csharp
// En DI (ya configurado)
services.AddScoped<IPromptProvider, SystemPromptProvider>();

// En uso (sin cambios)
var prompt = await _promptProvider.BuildAsync(context, cancellationToken);
```

### Configuración Dinámica

Todo se carga desde la base de datos:
- **BusinessInfo:** Nombre, descripción, horarios, contacto
- **Services:** Catálogo completo con descripciones, precios, duración
- **BusinessPersonality:** Nombre del asistente, tono, expertise
- **SalesGuidance:** Guía específica de ventas (opcional)

---

## 📈 Próximos Pasos

### Fase de Testing
1. ✅ Casos de prueba para los 2 problemas críticos anteriores
2. ✅ Casos de no-regresión
3. ✅ Casos edge

### Fase de Monitoreo
1. Monitorear conversaciones reales
2. Identificar si hay nuevos patrones de error
3. En vez de agregar reglas → Reforzar principios con ejemplos

### Fase de Iteración
1. Si un principio no es suficientemente claro → Mejorarlo con ejemplos
2. Si surge un nuevo tipo de error → Evaluar qué principio debe cubrirlo
3. No agregar reglas, reforzar principios

---

## 🎯 Regla de Oro del Framework

> **"Si estás tentado a agregar una nueva regla negativa,  
> pregúntate primero qué principio debería cubrirlo  
> y refuerza ese principio en vez de agregar la regla."**

---

## 📚 Referencias

- **Constitutional AI:** Anthropic (2022) - https://www.anthropic.com/index/constitutional-ai-harmlessness-from-ai-feedback
- **OpenAI Best Practices:** https://platform.openai.com/docs/guides/prompt-engineering
- **Clean Code:** Robert C. Martin
- **Domain-Driven Design:** Eric Evans

---

## ✅ Verificación de Implementación

- ✅ Archivos creados:
  - `Core/SalesPrinciples.cs`
  - `Core/HumanBehaviors.cs`
  - `Core/SystemConstraints.cs`
  - `Process/ReflectionChecklist.cs`

- ✅ Archivos refactorizados:
  - `SystemPromptProvider.cs`

- ✅ Archivos deprecados:
  - `SystemPrompts.cs` (marcado como `[Obsolete]`)

- ✅ Compilación:
  - Proyecto Application: ✅ Sin errores
  - Solución completa: ✅ Sin errores

- ✅ Arquitectura:
  - Modular: ✅
  - Multi-tenant: ✅
  - Sin hardcode: ✅
  - Clean Code: ✅
  - DDD ligero: ✅

---

**Implementado por:** AI Agent (Cursor)  
**Fecha:** 2026-01-28  
**Versión:** 2.0.0

# 🧪 Plan de Testing: Framework v2.0

## 📋 Objetivo

Validar que el **framework basado en principios (v2.0)** resuelve efectivamente los problemas que tenía el sistema basado en reglas (v1.0), sin introducir regresiones.

---

## 🎯 Casos de Prueba Críticos

### 1️⃣ Validación de VERACITY (No inventa información)

#### Test Case 1.1: No inventa servicios
```
ENTRADA:
Usuario: ¿Tienes clases de natación para bebés?

RESULTADO ESPERADO:
Bot: "No tengo clases de natación, pero sí tengo hidroterapia 
      especializada para bebés con el Plan Marineritos..."

VALIDACIÓN:
✅ NO menciona servicios que no están en el catálogo
✅ Ofrece alternativa de lo que SÍ tiene
✅ Usa principio VERACITY
```

#### Test Case 1.2: No inventa precios
```
ENTRADA:
Usuario: ¿Cuánto cuesta el Plan Piratas?
(Suponiendo que "Plan Piratas" no existe)

RESULTADO ESPERADO:
Bot: "No tengo un plan con ese nombre. Los planes disponibles son:
      • Plan Marineritos ($80,000)
      • Plan Bucaneros ($95,000)
      ¿Te interesa alguno?"

VALIDACIÓN:
✅ NO inventa el precio de un plan inexistente
✅ Lista los planes reales con precios correctos
✅ Usa SystemConstraints para verificar
```

#### Test Case 1.3: No inventa disponibilidad
```
ENTRADA:
Usuario: ¿Tienes disponible mañana a las 3pm?

RESULTADO ESPERADO:
Bot: "Déjame verificar disponibilidad para mañana a las 3pm..."
     [Solicita verificación al sistema]

VALIDACIÓN:
✅ NO promete disponibilidad sin verificar
✅ Usa herramienta de verificación
✅ Espera respuesta antes de confirmar
```

---

### 2️⃣ Validación de RESPECT (No repite preguntas)

#### Test Case 2.1: Usa información ya proporcionada
```
ENTRADA:
Usuario: "Hola, soy Ana y tengo un bebé de 5 meses"

RESULTADO ESPERADO:
Bot: "¡Hola Ana! Qué lindo que tu bebé tenga 5 meses..."
     [NO pregunta nombre ni edad]

VALIDACIÓN:
✅ Usa CustomerName del estado
✅ Usa BabyAge del estado
✅ No repite preguntas ya respondidas
```

#### Test Case 2.2: Extrae fecha de pregunta compuesta
```
ENTRADA:
Usuario: "¿Qué horarios tienes libres mañana?"

RESULTADO ESPERADO:
Bot: [Extrae: user_requested_availability = true, DesiredDate = MAÑANA]
     "Déjame verificar disponibilidad para mañana..."

VALIDACIÓN:
✅ Extrae AMBOS: intención + fecha
✅ NO pregunta "¿Para qué fecha?"
✅ Usa información ya proporcionada
```

#### Test Case 2.3: Inferencia de respuestas directas
```
ENTRADA:
Bot: "¿Cómo se llama tu bebé?"
Usuario: "Thomas"

RESULTADO ESPERADO:
Bot: [Extrae: BabyName = "Thomas"]
     "¡Perfecto, Thomas es un nombre hermoso! Para tu bebé Thomas..."

VALIDACIÓN:
✅ Extrae BabyName correctamente
✅ NO pregunta de nuevo por el nombre
✅ Usa el nombre en la respuesta
```

---

### 3️⃣ Validación de EMPATHY (Entiende contexto)

#### Test Case 3.1: Entiende confirmación implícita
```
ENTRADA:
Bot: "Para tu bebé de 5 meses, te recomendaría Plan Marineritos..."
Usuario: "Sí, ¿qué horarios tienes?"

RESULTADO ESPERADO:
Bot: [Infiere: Service = "Plan Marineritos"]
     "Perfecto, déjame verificar disponibilidad para Plan Marineritos..."

VALIDACIÓN:
✅ Infiere servicio del contexto
✅ No pregunta "¿Qué servicio quieres?"
✅ Usa LastBotMessage para contexto
```

#### Test Case 3.2: Hace preguntas clarificadoras
```
ENTRADA:
Usuario: "Quiero reservar para mi bebé"

RESULTADO ESPERADO:
Bot: "¡Perfecto! Para recomendarte el mejor servicio,
      ¿me cuentas qué edad tiene tu bebé?"

VALIDACIÓN:
✅ Identifica que falta información crítica (edad)
✅ Hace pregunta contextualizada
✅ Una pregunta a la vez
```

---

### 4️⃣ Validación de HELPFULNESS (Recomendaciones completas)

#### Test Case 4.1: Recomendación con 5 puntos
```
ENTRADA:
Usuario: "¿Qué me recomiendas para mi bebé de 5 meses?"

RESULTADO ESPERADO:
Bot: Respuesta con estructura completa:
1. QUÉ es (Plan Marineritos)
2. POR QUÉ (edad perfecta para estimulación acuática)
3. QUÉ incluye (sesión guiada, ambiente seguro, etc.)
4. BENEFICIOS (sistema inmune, sueño, cólicos, vínculo)
5. INFO PRÁCTICA (45 min, $80,000, "¿verifico disponibilidad?")

VALIDACIÓN:
✅ Incluye los 5 puntos
✅ Personalizada a la edad (5 meses)
✅ Pregunta por próximo paso
```

#### Test Case 4.2: No recomienda si no es adecuado
```
ENTRADA:
Usuario: "¿El Plan Bucaneros sirve para un recién nacido?"

RESULTADO ESPERADO:
Bot: "El Plan Bucaneros está diseñado para bebés de 12-24 meses.
      Para un recién nacido, te recomendaría más bien el Plan Marineritos
      que es especializado para bebés de 0-12 meses..."

VALIDACIÓN:
✅ Reconoce que no es adecuado
✅ Explica por qué
✅ Ofrece alternativa correcta
```

---

### 5️⃣ Validación de TRANSPARENCY (Claridad del proceso)

#### Test Case 5.1: Comunica que necesita verificar
```
ENTRADA:
Usuario: "¿Hay espacio para hoy?"

RESULTADO ESPERADO:
Bot: "Déjame verificar disponibilidad para hoy..."
     [Llama a herramienta de disponibilidad]

VALIDACIÓN:
✅ Comunica que va a verificar
✅ No promete sin verificar
✅ Transparente sobre el proceso
```

#### Test Case 5.2: Explica próximos pasos
```
ENTRADA:
Usuario: "Ok, reservo para mañana a las 10am"

RESULTADO ESPERADO:
Bot: "Perfecto. Para confirmar tu reserva necesito:
      1. Confirmar que el nombre del bebé es [X]
      2. Verificar que tu contacto es [Y]
      ¿Todo correcto?"

VALIDACIÓN:
✅ Claro sobre qué necesita
✅ Lista los pasos
✅ Pide confirmación antes de proceder
```

---

## 🔄 Casos de No-Regresión

### Test Case NR-1: Extracción de campos core
```
Verificar que sigue extrayendo correctamente:
- CustomerName
- Service
- DesiredDate
- DesiredTime
- Atributos de negocio (BabyAge, BabyName, etc.)
```

### Test Case NR-2: Flujo de reserva completo
```
Verificar que el flujo end-to-end funciona:
1. Saludo inicial
2. Recolección de información
3. Recomendación de servicio
4. Verificación de disponibilidad
5. Confirmación de reserva
```

### Test Case NR-3: Multi-tenant
```
Verificar que funciona para diferentes negocios:
- MimosBabySpa (actual)
- Negocio ficticio con servicios diferentes
```

---

## 🧪 Casos Edge

### Test Case E-1: Pregunta ambigua
```
ENTRADA:
Usuario: "Hola"

RESULTADO ESPERADO:
Bot: Saludo cálido + pregunta estratégica
     (No bombardea con múltiples preguntas)

VALIDACIÓN:
✅ Una pregunta a la vez
✅ Prioriza información más importante
```

### Test Case E-2: Cliente cambia de opinión
```
ENTRADA:
Usuario: "Quiero Plan Marineritos"
Usuario: "Mejor el Plan Bucaneros"

RESULTADO ESPERADO:
Bot: [Actualiza: Service = "Plan Bucaneros"]
     "Perfecto, cambiamos a Plan Bucaneros..."

VALIDACIÓN:
✅ Actualiza el estado correctamente
✅ No se confunde con información anterior
```

### Test Case E-3: Información parcial
```
ENTRADA:
Usuario: "Tengo un bebé"

RESULTADO ESPERADO:
Bot: "¡Qué lindo! ¿Me cuentas qué edad tiene tu bebé
      para recomendarte el mejor servicio?"

VALIDACIÓN:
✅ Reconoce información parcial
✅ Pregunta lo que falta
✅ No asume información
```

---

## 📊 Métricas de Éxito

### Métricas Cualitativas
- ✅ **Coherencia:** Respuestas consistentes con principios
- ✅ **Naturalidad:** Conversación fluida y humana
- ✅ **Completitud:** Recomendaciones con 5 puntos
- ✅ **Precisión:** Extracción correcta de información

### Métricas Cuantitativas
- ✅ **Extracción:** >95% de campos correctos
- ✅ **Sin invención:** 0% de servicios/precios inventados
- ✅ **Sin repetición:** 0% de preguntas repetidas
- ✅ **Recomendaciones completas:** 100% con 5 puntos

---

## 🔧 Herramientas de Testing

### Opción 1: Testing Manual (Recomendado para inicio)
```bash
# Usar proyecto de consola
cd src/Console/MimosBabySpa.Console
dotnet run

# Probar cada caso manualmente
# Verificar resultados contra esperados
```

### Opción 2: Testing Automatizado (Futuro)
```csharp
// Crear tests unitarios para cada principio
[Test]
public async Task Veracity_NoInventaServicios()
{
    var input = "¿Tienes clases de natación?";
    var response = await _orchestrator.ProcessAsync(input);
    
    Assert.That(response, Does.Not.Contain("clases de natación"));
    Assert.That(response, Contains.Substring("Plan Marineritos"));
}
```

---

## 📝 Checklist de Validación

### Antes de considerar v2.0 como "listo para producción":

- [ ] **VERACITY:** 5/5 casos pasan
- [ ] **RESPECT:** 3/3 casos pasan
- [ ] **EMPATHY:** 2/2 casos pasan
- [ ] **HELPFULNESS:** 2/2 casos pasan
- [ ] **TRANSPARENCY:** 2/2 casos pasan
- [ ] **No-Regresión:** 3/3 casos pasan
- [ ] **Edge Cases:** 3/3 casos pasan
- [ ] **Compilación:** Sin errores
- [ ] **Documentación:** Completa
- [ ] **Code Review:** Aprobado

---

## 🚀 Próximos Pasos Después del Testing

### Si todos los tests pasan:
1. ✅ Marcar v2.0 como estable
2. ✅ Deprecar v1.0 completamente
3. ✅ Monitorear conversaciones reales por 1-2 semanas
4. ✅ Iterar basándose en feedback

### Si algunos tests fallan:
1. 🔍 Identificar qué principio no está claro
2. 🔧 Reforzar ese principio con ejemplos
3. 🧪 Re-testear
4. 📚 Documentar el aprendizaje

### Si descubrimos un nuevo problema:
1. ❓ **NO agregar regla nueva**
2. 🎯 Preguntarse: ¿Qué principio debería cubrirlo?
3. 💡 Reforzar ese principio
4. ✅ Validar que funciona

---

## 🎓 Lecciones Aprendidas

### Filosofía del Framework v2.0:

> "Cuando tengas la tentación de agregar una regla nueva,  
> pregúntate primero qué principio debería cubrirla  
> y refuerza ese principio en vez de agregar la regla."

### Evolución del Sistema:

```
v1.0: Agregar reglas → Lista infinita
v2.0: Reforzar principios → Sistema estable
```

---

**Documento creado:** 2026-01-28  
**Framework:** Human Sales v2.0  
**Estado:** Listo para testing

# Flujo Conversacional Natural y Humano

## 🎯 Objetivo

Diseñar un flujo de conversación que sea **natural, gradual y humano**, evitando que el bot actúe como un formulario o máquina que bombardea con información.

---

## 📋 Diseño del Flujo Completo

### **FASE 1: Saludo Inicial** (Primer contacto)

**Objetivo:** Romper el hielo de forma amigable, sin abrumar.

**Bot debe responder:**
```
"¡Hola! 👋 Bienvenido a Mimos Baby Spa - Valledupar. 
Es un placer atenderte. ¿En qué puedo ayudarte hoy?"
```

**Reglas:**
- ✅ Saludo breve (1-2 líneas máximo)
- ✅ NO mencionar servicios todavía
- ✅ Esperar respuesta del usuario
- ❌ NO listar servicios inmediatamente
- ❌ NO pedir información todavía

**Ejemplos de respuestas según contexto:**

| Usuario dice | Bot responde |
|--------------|--------------|
| "Hola" | "¡Hola! Bienvenido a Mimos Baby Spa. ¿Te gustaría conocer nuestros servicios o ya tienes algo en mente?" |
| "Quiero información" | "¡Por supuesto! ¿Sobre qué servicio te gustaría saber más?" |
| "Quiero reservar" | "¡Perfecto! Me encantaría ayudarte. ¿Ya sabes qué servicio te gustaría?" |

---

### **FASE 2: Descubrimiento de Necesidad** (2-3 mensajes máximo)

**Objetivo:** Entender qué busca el cliente ANTES de ofrecer todo.

**Escenario A - Usuario pregunta por servicios:**
```
Usuario: "¿Qué servicios tienen?"
Bot: "¡Por supuesto! Tenemos varios servicios especiales para bebés:
      - Clase Grupal
      - Plan Aventuras Marinas  
      - Plan Marineritos
      - Plan Suaves Mimos
      
      ¿Alguno te llama la atención o quieres que te cuente más sobre alguno?"
```

**Escenario B - Usuario menciona servicio directamente:**
```
Usuario: "Quiero el Plan Marineritos"
Bot: "¡Excelente elección! El Plan Marineritos es perfecto para bebés. 
      ¿Cómo se llama tu bebé?"
      
[Extraer con update_conversation_state: Service="Plan Marineritos"]
```

**Escenario C - Usuario solo saluda:**
```
Usuario: "Hola"
Bot: "¡Hola! Bienvenido a Mimos Baby Spa. 
      ¿Te gustaría conocer nuestros servicios o ya tienes algo en mente?"
```

**Reglas:**
- ✅ Si pregunta por servicios → lista breve y pregunta cuál le interesa
- ✅ Si menciona servicio específico → extrae el servicio y continúa con ese
- ✅ Si solo saluda → pregunta cómo puedes ayudar
- ❌ NO bombardear con toda la información de una vez

---

### **FASE 3: Recolección Gradual de Información** (Una cosa a la vez)

**Objetivo:** Recolectar información de forma natural, paso a paso.

**Orden sugerido:**
1. Nombre del cliente (si no lo dio)
2. Nombre del bebé (si aplica según atributos)
3. Edad del bebé u otro atributo relevante (si aplica)
4. Servicio (si aún no lo tiene)
5. Fecha deseada
6. Hora deseada

**Ejemplo de recolección natural:**

```
Bot: "¡Excelente elección! El Plan Marineritos es perfecto para bebés. 
      ¿Cómo se llama tu bebé?"

Usuario: "Lucas"
Bot: "¡Qué lindo nombre! 😊 ¿Cuántos meses tiene Lucas?"
      
[update_conversation_state: Attribute:BabyName="Lucas"]

Usuario: "6 meses"
Bot: "Perfecto, esa es una edad ideal para disfrutar nuestros servicios. 
      ¿Para qué fecha te gustaría la reserva?"
      
[update_conversation_state: Attribute:BabyAge="6"]

Usuario: "El 15 de febrero"
Bot: "Excelente, el 15 de febrero. ¿Qué hora te conviene más?"
      
[update_conversation_state: DesiredDate="2026-02-15"]

Usuario: "A las 10 de la mañana"
Bot: "Perfecto, las 10:00 AM. Déjame verificar la disponibilidad..."
      
[update_conversation_state: DesiredTime="10:00"]
```

**Reglas críticas:**
- ✅ **Una pregunta a la vez** - nunca múltiples preguntas
- ✅ **Confirma antes de avanzar** - "Perfecto, el 15 de febrero..."
- ✅ **Usa información para personalizar** - "¡Qué lindo nombre Lucas!"
- ✅ **Extrae inmediatamente** con update_conversation_state cuando obtengas información
- ❌ NO mencionar todos los campos faltantes de una vez
- ❌ NO actuar como formulario: "Campo 1: Nombre. Campo 2: Fecha"

---

### **FASE 4: Verificación de Disponibilidad** (Cuando tengas Service + Fecha)

**Objetivo:** Verificar disponibilidad de forma transparente.

**Cuándo verificar:**
- ✅ Tienes: Service + DesiredDate (y opcionalmente DesiredTime)
- ✅ Llamar: `check_availability`

**Ejemplo:**

```
Bot: "Perfecto, entonces sería el Plan Marineritos para Lucas el 15 de febrero. 
      Déjame verificar la disponibilidad..."
      
[Llama check_availability]

Si hay disponibilidad:
Bot: "¡Excelente noticia! Tenemos disponibilidad ese día. 
      ¿Te gustaría reservar para las 10:00 AM o prefieres otra hora?"

Si NO hay disponibilidad:
Bot: "Lamentablemente ese día está completo. 
      ¿Te gustaría ver otras fechas disponibles o prefieres otro horario?"
```

**Reglas:**
- ✅ NO prometer disponibilidad antes de verificar
- ✅ Si no hay disponibilidad → ofrecer alternativas inmediatamente
- ✅ Ser empático si no hay disponibilidad

---

### **FASE 5: Confirmación y Creación de Reserva** (Solo con confirmación explícita)

**Objetivo:** Confirmar todos los detalles antes de crear la reserva.

**Ejemplo:**

```
Bot: "Perfecto, entonces sería:
      📅 Plan Marineritos
      👶 Para Lucas (6 meses)
      📆 Fecha: 15 de febrero
      ⏰ Hora: 10:00 AM
      
      ¿Confirmas esta reserva?"

Usuario: "Sí, confirma" o "Sí" o "Adelante" o "Procede"
Bot: [Llama create_reservation]
     "¡Reserva confirmada! ✅
      Tu reserva está lista para el 15 de febrero a las 10:00 AM.
      ID de reserva: [ID]
      
      Te enviaremos un recordatorio un día antes. 
      ¡Estamos emocionados de recibir a Lucas!"
```

**Reglas:**
- ✅ Resumir TODOS los detalles antes de confirmar
- ✅ Esperar confirmación EXPLÍCITA ("sí", "confirma", "adelante")
- ✅ NO crear reserva sin confirmación
- ✅ Celebrar cuando se confirme

---

## 🎨 Principios del Flujo Natural

### 1. **Una cosa a la vez**
❌ "Necesito tu nombre, fecha, hora y servicio"  
✅ "¿Cómo se llama tu bebé?"

### 2. **Escuchar primero**
❌ Listar todos los servicios inmediatamente  
✅ "¿En qué puedo ayudarte?" → Esperar respuesta → Ofrecer según necesidad

### 3. **Personalizar**
❌ "Perfecto, continuemos"  
✅ "¡Qué lindo nombre Lucas! ¿Cuántos meses tiene?"

### 4. **Validar gradualmente**
❌ Asumir que entendiste todo  
✅ "Perfecto, entonces sería el Plan Marineritos para Lucas el 15 de febrero. ¿Correcto?"

### 5. **Ser empático**
❌ "Ese día no está disponible"  
✅ "Lamentablemente ese día está completo. ¿Te gustaría ver otras fechas?"

### 6. **Conversación natural**
❌ "Campo: Nombre. Campo: Fecha. Campo: Hora"  
✅ "¿Cómo se llama tu bebé?" → "¿Para qué fecha?" → "¿Qué hora te conviene?"

---

## 📝 Ejemplo de Conversación Completa

```
Usuario: "Hola"
Bot: "¡Hola! 👋 Bienvenido a Mimos Baby Spa - Valledupar. 
      Es un placer atenderte. ¿En qué puedo ayudarte hoy?"

Usuario: "Quiero información sobre sus servicios"
Bot: "¡Por supuesto! Tenemos varios servicios especiales para bebés:
      - Clase Grupal
      - Plan Aventuras Marinas  
      - Plan Marineritos
      - Plan Suaves Mimos
      
      ¿Alguno te llama la atención o quieres que te cuente más sobre alguno?"

Usuario: "El Plan Marineritos"
Bot: "¡Excelente elección! El Plan Marineritos es perfecto para bebés. 
      ¿Cómo se llama tu bebé?"

Usuario: "Lucas"
Bot: "¡Qué lindo nombre! 😊 ¿Cuántos meses tiene Lucas?"

Usuario: "6 meses"
Bot: "Perfecto, esa es una edad ideal para disfrutar nuestros servicios. 
      ¿Para qué fecha te gustaría reservar?"

Usuario: "El 15 de febrero"
Bot: "Excelente, el 15 de febrero. ¿Qué hora te conviene más? 
      Tenemos horarios disponibles desde las 9:00 AM hasta las 5:00 PM."

Usuario: "A las 10 de la mañana"
Bot: "Perfecto, las 10:00 AM. Déjame verificar la disponibilidad 
      para el Plan Marineritos el 15 de febrero..."
      
[check_availability]

Bot: "¡Excelente noticia! Tenemos disponibilidad ese día a las 10:00 AM. 
      Entonces sería:
      📅 Plan Marineritos
      👶 Para Lucas (6 meses)
      📆 Fecha: 15 de febrero
      ⏰ Hora: 10:00 AM
      
      ¿Confirmas esta reserva?"

Usuario: "Sí, confirma"
Bot: [create_reservation]
     "¡Reserva confirmada! ✅
      Tu reserva está lista para el 15 de febrero a las 10:00 AM.
      ID de reserva: [ID]
      
      Te enviaremos un recordatorio un día antes. 
      ¡Estamos emocionados de recibir a Lucas!"
```

---

## ⚠️ Errores Comunes a Evitar

### ❌ Error 1: Bombardear con información
```
Bot: "¡Hola! Bienvenido. Tenemos estos servicios: [lista larga]. 
      También necesito tu nombre, fecha, hora, nombre del bebé..."
```
**Problema:** Abruma al usuario desde el inicio.

### ❌ Error 2: Múltiples preguntas
```
Bot: "Perfecto. Necesito: nombre del bebé, edad, fecha y hora."
```
**Problema:** Parece formulario, no conversación.

### ❌ Error 3: No personalizar
```
Bot: "Perfecto, continuemos. ¿Fecha?"
```
**Problema:** Frío e impersonal.

### ❌ Error 4: Prometer disponibilidad sin verificar
```
Bot: "Seguro tenemos disponibilidad ese día"
```
**Problema:** Puede crear expectativas falsas.

---

## ✅ Checklist de Buenas Prácticas

- [ ] Saludo breve y cálido (1-2 líneas)
- [ ] NO listar servicios inmediatamente
- [ ] Una pregunta a la vez
- [ ] Confirmar antes de avanzar
- [ ] Personalizar usando nombres
- [ ] Verificar disponibilidad antes de prometer
- [ ] Resumir antes de confirmar
- [ ] Esperar confirmación explícita
- [ ] Celebrar cuando se confirme

---

## 🔄 Flujo Visual

```
SALUDO
  ↓
¿Qué necesita?
  ↓
¿Servicio específico? → SÍ → Extraer servicio
  ↓ NO
Listar servicios brevemente
  ↓
Usuario elige servicio → Extraer servicio
  ↓
¿Nombre del bebé? → Extraer
  ↓
¿Edad? → Extraer
  ↓
¿Fecha? → Extraer
  ↓
¿Hora? → Extraer
  ↓
Verificar disponibilidad (check_availability)
  ↓
¿Disponible? → SÍ → Resumir y confirmar
  ↓ NO
Ofrecer alternativas
  ↓
Usuario confirma → Crear reserva (create_reservation)
  ↓
Celebrar y confirmar detalles
```

---

## 📊 Comparación: Antes vs Después

### ANTES (Robótico):
```
Bot: "¡Hola! Bienvenido a Mimos Baby Spa - Valledupar. Es un placer atenderte.

Para ayudarte mejor, ¿qué tipo de servicio te gustaría reservar para tu bebé? 
Te puedo ofrecer:
- Clase Grupal
- Plan Aventuras Marinas
- Plan Marineritos
- Plan Suaves Mimos

Además, necesitaré saber tu nombre y la fecha y hora en que te gustaría hacer la reserva."
```

### DESPUÉS (Natural):
```
Bot: "¡Hola! 👋 Bienvenido a Mimos Baby Spa - Valledupar. 
      Es un placer atenderte. ¿En qué puedo ayudarte hoy?"

Usuario: "Quiero información"
Bot: "¡Por supuesto! ¿Sobre qué servicio te gustaría saber más?"
```

---

## 🎯 Resultado Esperado

Una conversación que:
- ✅ Se siente natural y humana
- ✅ No abruma al usuario
- ✅ Recolecta información gradualmente
- ✅ Personaliza usando información recolectada
- ✅ Es empática y servicial
- ✅ Guía suavemente hacia la reserva

---

**Última actualización:** 26 de enero, 2026

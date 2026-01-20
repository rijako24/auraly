# Configuración de Reservas con Prompts desde Base de Datos

## 📋 Descripción

El sistema de reservas está completamente integrado con el bot usando prompts configurables desde la base de datos. Todo el flujo de reservas (consulta de disponibilidad, creación de reservas, confirmaciones) está controlado por prompts genéricos en `SystemConfiguration` y reglas específicas del negocio en `BusinessConfiguration`.

## 🏗️ Arquitectura de Configuración

### SystemConfiguration (Genéricos - Aplican a todos los negocios)

Estos prompts son genéricos y se configuran una vez para todos los negocios:

1. **ReservationFlowPrompt** (Key: 8)
   - Prompt para procesar el flujo completo de reservas
   - Placeholders disponibles: `{reservationRules}`, `{serviceDurationRules}`, `{context}`, `{userMessage}`, `{customerName}`, `{phoneNumber}`

2. **AvailabilityQueryPrompt** (Key: 9)
   - Prompt para responder consultas de disponibilidad
   - Placeholders disponibles: `{date}`, `{time}`, `{availabilityResult}`, `{context}`

3. **ReservationConfirmationPrompt** (Key: 10)
   - Prompt para confirmar reservas creadas exitosamente
   - Placeholders disponibles: `{customerName}`, `{date}`, `{time}`, `{serviceName}`

4. **ReservationDataExtractionPrompt** (Key: 11) ⭐ NUEVO
   - Prompt para extraer datos de reserva usando IA (reemplaza regex)
   - Placeholders disponibles: `{reservationRules}`, `{context}`, `{userMessage}`
   - Debe retornar JSON con formato específico

5. **AvailabilityDetectionPrompt** (Key: 12) ⭐ NUEVO
   - Prompt para detectar si un mensaje es consulta de disponibilidad usando IA (reemplaza keywords hardcodeadas)
   - Placeholders disponibles: `{context}`, `{messageText}`
   - Debe retornar JSON: `{"isAvailabilityQuery": true/false}`

6. **AvailabilityDataExtractionPrompt** (Key: 13) ⭐ NUEVO
   - Prompt para extraer fecha/hora de consultas de disponibilidad usando IA (reemplaza regex)
   - Placeholders disponibles: `{context}`, `{messageText}`
   - Debe retornar JSON: `{"date": "YYYY-MM-DD", "time": "HH:mm", "durationMinutes": 60}`

### BusinessConfiguration (Específicos del Negocio)

Estos se configuran por cada negocio:

1. **ReservationTemplate** (Key: 12)
   - Template para eventos de calendario
   - Placeholders: `{CustomerName}`, `{PhoneNumber}`, `{ServiceName}`, `{ReservationDate}`, `{ReservationTime}`, `{DurationMinutes}`

2. **ReservationRules** (Key: 13)
   - Reglas específicas del negocio para reservas
   - Qué datos se necesitan, cómo validar, políticas, etc.

3. **ServiceDurationRules** (Key: 14)
   - Reglas para calcular duración de servicios
   - Formato JSON: `{"Plan Premium": 90, "Plan Básico": 45, "Plan Deluxe": 120}`

## 📝 Ejemplos de Configuración

### 1. SystemConfiguration - ReservationFlowPrompt

```sql
INSERT INTO SystemConfigurations (SystemConfigurationId, Value, Description, IsActive)
VALUES (8, 
'Analiza el siguiente contexto y mensaje del usuario para determinar si tienes todos los datos necesarios para crear una reserva.

Reglas de reserva del negocio:
{reservationRules}

Reglas de duración de servicios:
{serviceDurationRules}

Contexto de la conversación:
{context}

Mensaje del usuario: {userMessage}
Cliente: {customerName}
Teléfono: {phoneNumber}

Responde SOLO con JSON válido:
{
  "hasAllData": true/false,
  "message": "mensaje para el usuario si falta información",
  "reservationData": {
    "customerName": "nombre extraído",
    "serviceName": "servicio extraído",
    "reservationDate": "YYYY-MM-DD",
    "reservationTime": "HH:mm"
  }
}

Si falta información, hasAllData debe ser false y message debe indicar qué falta.
Si tienes todos los datos, hasAllData debe ser true y reservationData debe contener los datos extraídos.',
'Prompt genérico para procesar flujo de reservas',
1);
```

### 2. SystemConfiguration - AvailabilityQueryPrompt

```sql
INSERT INTO SystemConfigurations (SystemConfigurationId, Value, Description, IsActive)
VALUES (9,
'El cliente pregunta por disponibilidad para el {date} a las {time}.

Resultado de la consulta: {availabilityResult}

Contexto adicional:
{context}

Genera una respuesta amigable y profesional indicando si el horario está disponible o no. Si está disponible, pregunta si quiere reservar.',
'Prompt genérico para responder consultas de disponibilidad',
1);
```

### 3. SystemConfiguration - ReservationConfirmationPrompt

```sql
INSERT INTO SystemConfigurations (SystemConfigurationId, Value, Description, IsActive)
VALUES (10,
'Perfecto 💙

Ya reservé la cita para {customerName} el {date} a las {time}.

Servicio: {serviceName}

Te esperamos en nuestro establecimiento.',
'Prompt genérico para confirmar reservas',
1);
```

### 4. SystemConfiguration - ReservationDataExtractionPrompt ⭐ NUEVO

```sql
INSERT INTO SystemConfigurations (SystemConfigurationId, Value, Description, IsActive)
VALUES (11,
'Eres un asistente especializado en extraer información de reservas desde mensajes de usuarios.

Reglas de reserva del negocio:
{reservationRules}

Contexto de la conversación:
{context}

Analiza el siguiente mensaje del usuario y extrae los datos necesarios para crear una reserva.

Responde SOLO con JSON válido en el siguiente formato:
{
  "hasAllData": true/false,
  "message": "mensaje para el usuario si falta información",
  "reservationData": {
    "customerName": "nombre del cliente extraído",
    "serviceName": "servicio o plan extraído",
    "reservationDate": "YYYY-MM-DD",
    "reservationTime": "HH:mm"
  }
}

Instrucciones:
- Si falta información, hasAllData debe ser false y message debe indicar qué falta de manera amigable
- Si tienes todos los datos, hasAllData debe ser true y reservationData debe contener los datos extraídos
- Las fechas deben estar en formato YYYY-MM-DD
- Las horas deben estar en formato HH:mm (24 horas)
- Si el usuario menciona "mañana", "pasado mañana", etc., calcula la fecha real
- Si el usuario menciona "3pm", "3 de la tarde", etc., convierte a formato 24 horas (15:00)',
'Prompt para extraer datos de reserva usando IA (reemplaza regex)',
1);
```

### 5. SystemConfiguration - AvailabilityDetectionPrompt ⭐ NUEVO

```sql
INSERT INTO SystemConfigurations (SystemConfigurationId, Value, Description, IsActive)
VALUES (12,
'Eres un analizador de intenciones. Determina si el siguiente mensaje del usuario es una consulta sobre disponibilidad de horarios para reservas.

Contexto de la conversación:
{context}

Mensaje del usuario: {messageText}

Responde SOLO con JSON válido:
{
  "isAvailabilityQuery": true/false
}

Considera como consulta de disponibilidad:
- Preguntas sobre horarios disponibles
- Preguntas sobre fechas libres
- Consultas sobre si hay espacio en una fecha/hora específica
- Preguntas sobre disponibilidad en general

NO consideres como consulta de disponibilidad:
- Solicitudes directas de reserva (ej: "quiero reservar")
- Preguntas sobre servicios o precios
- Saludos o conversación general',
'Prompt para detectar consultas de disponibilidad usando IA (reemplaza keywords)',
1);
```

### 6. SystemConfiguration - AvailabilityDataExtractionPrompt ⭐ NUEVO

```sql
INSERT INTO SystemConfigurations (SystemConfigurationId, Value, Description, IsActive)
VALUES (13,
'Eres un asistente especializado en extraer fechas y horas de mensajes sobre disponibilidad.

Contexto de la conversación:
{context}

Mensaje del usuario: {messageText}

Analiza el mensaje y extrae la fecha y hora mencionadas (si las hay).

Responde SOLO con JSON válido:
{
  "date": "YYYY-MM-DD" o null si no hay fecha,
  "time": "HH:mm" o null si no hay hora,
  "durationMinutes": 60 (opcional, duración en minutos si se menciona)
}

Instrucciones:
- Si el usuario dice "mañana", calcula la fecha de mañana
- Si el usuario dice "el 15 de febrero", usa esa fecha
- Si el usuario dice "a las 3pm", convierte a formato 24 horas (15:00)
- Si no hay fecha u hora mencionada, retorna null
- Las fechas deben estar en formato YYYY-MM-DD
- Las horas deben estar en formato HH:mm (24 horas)',
'Prompt para extraer fecha/hora de consultas de disponibilidad usando IA (reemplaza regex)',
1);
```

### 7. BusinessConfiguration - ReservationTemplate

```sql
INSERT INTO BusinessConfigurations (BusinessId, [Key], Value, Description, IsActive)
VALUES (
  '<GUID_DEL_NEGOCIO>',
  12,
  '[{ServiceName}] {CustomerName}

Cliente: {CustomerName}
Teléfono: {PhoneNumber}
Servicio: {ServiceName}
Fecha: {ReservationDate}
Hora: {ReservationTime}
Duración: {DurationMinutes} minutos

Reserva creada por bot IA.',
  'Template para eventos de calendario de reservas',
  1
);
```

### 5. BusinessConfiguration - ReservationRules

```sql
INSERT INTO BusinessConfigurations (BusinessId, [Key], Value, Description, IsActive)
VALUES (
  '<GUID_DEL_NEGOCIO>',
  13,
  'Para crear una reserva se necesitan los siguientes datos:
- Nombre del cliente
- Teléfono (WhatsApp)
- Servicio o plan elegido
- Fecha deseada (formato: DD/MM/YYYY)
- Hora deseada (formato: HH:mm)

Validaciones:
- La fecha debe ser futura
- La hora debe estar dentro del horario de atención (9:00 AM - 7:00 PM)
- No se pueden hacer reservas para el mismo día después de las 5:00 PM',
  'Reglas específicas del negocio para reservas',
  1
);
```

### 8. BusinessConfiguration - ServiceDurationRules

```sql
INSERT INTO BusinessConfigurations (BusinessId, [Key], Value, Description, IsActive)
VALUES (
  '<GUID_DEL_NEGOCIO>',
  14,
  '{
    "Plan Básico": 45,
    "Plan Premium": 90,
    "Plan Deluxe": 120,
    "Masaje Relajante": 60,
    "Hidroterapia": 45
  }',
  'Reglas para calcular duración de servicios en minutos',
  1
);
```

## 🔄 Flujo del Bot con Prompts (100% IA, Sin Regex)

### Consulta de Disponibilidad

1. Usuario pregunta: "¿Está disponible el 15 de febrero a las 10:00 AM?"
2. Bot obtiene `AvailabilityDetectionPrompt` de SystemConfiguration
3. Bot usa IA para detectar si es consulta de disponibilidad (reemplaza keywords hardcodeadas)
4. Si es consulta de disponibilidad:
   - Bot obtiene `AvailabilityDataExtractionPrompt` de SystemConfiguration
   - Bot usa IA para extraer fecha y hora (reemplaza regex)
   - Bot consulta `IsAvailableAsync` en Google Calendar y BD
   - Bot obtiene `AvailabilityQueryPrompt` de SystemConfiguration
   - Bot reemplaza placeholders: `{date}`, `{time}`, `{availabilityResult}`
   - Bot responde usando el prompt configurado

### Creación de Reserva

1. Usuario dice: "Quiero reservar el Plan Premium para mañana a las 3pm"
2. Bot detecta intención `ReservationRequest` (usando IA)
3. Bot obtiene `ReservationDataExtractionPrompt` de SystemConfiguration
4. Bot obtiene `ReservationRules` y `ServiceDurationRules` de BusinessConfiguration
5. Bot reemplaza placeholders en el prompt
6. Bot usa IA para extraer datos (reemplaza regex completamente)
7. Si tiene todos los datos:
   - Valida disponibilidad
   - Crea reserva en BD
   - Crea evento en calendario usando `ReservationTemplate`
   - Genera confirmación usando `ReservationConfirmationPrompt`
8. Si falta información:
   - Retorna mensaje de la IA indicando qué falta
   - Continúa conversación para recopilar datos

## 📊 Estructura de Datos

### SystemConfiguration

| SystemConfigurationId | Nombre | Descripción |
|----------------------|--------|-------------|
| 8 | ReservationFlowPrompt | Prompt para procesar flujo de reservas |
| 9 | AvailabilityQueryPrompt | Prompt para consultas de disponibilidad |
| 10 | ReservationConfirmationPrompt | Prompt para confirmar reservas |
| 11 | ReservationDataExtractionPrompt | Prompt para extraer datos de reserva usando IA ⭐ |
| 12 | AvailabilityDetectionPrompt | Prompt para detectar consultas de disponibilidad usando IA ⭐ |
| 13 | AvailabilityDataExtractionPrompt | Prompt para extraer fecha/hora usando IA ⭐ |

### BusinessConfiguration

| Key | Nombre | Descripción |
|-----|--------|-------------|
| 12 | ReservationTemplate | Template para eventos de calendario |
| 13 | ReservationRules | Reglas específicas del negocio |
| 14 | ServiceDurationRules | Reglas de duración de servicios (JSON) |

## ✅ Ventajas de esta Arquitectura

1. **100% IA**: Todo el procesamiento usa IA, sin regex ni keywords hardcodeadas
2. **Genérico**: Los prompts en SystemConfiguration aplican a todos los negocios
3. **Personalizable**: Cada negocio puede tener sus propias reglas en BusinessConfiguration
4. **Sin código hardcodeado**: Todo viene de la base de datos
5. **Fácil de modificar**: Cambiar prompts sin recompilar código
6. **Testeable**: Se pueden probar diferentes prompts fácilmente
7. **Escalable**: Fácil agregar nuevos negocios con sus propias reglas
8. **Inteligente**: La IA entiende contexto, sinónimos, variaciones de lenguaje

## 🚀 Próximos Pasos

1. **Configurar prompts en SystemConfiguration** (una vez para todos los negocios)
2. **Configurar reglas en BusinessConfiguration** (por cada negocio)
3. **Probar el flujo completo** de consulta y reserva
4. **Ajustar prompts** según resultados

## 📝 Notas Importantes

- ⚠️ **Si no hay `ReservationTemplate` configurado, el sistema NO creará reservas**
- ⚠️ **Si no hay `ReservationDataExtractionPrompt`, el sistema no podrá extraer datos de reservas**
- ⚠️ **Si no hay `AvailabilityDetectionPrompt`, el sistema usará detección básica (fallback)**
- ⚠️ **Si no hay `AvailabilityDataExtractionPrompt`, el sistema retornará datos vacíos**
- Los prompts pueden usar todos los placeholders disponibles
- Los placeholders se reemplazan automáticamente antes de enviar a la IA
- **TODOS los prompts deben retornar JSON válido cuando se especifica `jsonResponse: true`**
- La IA reemplaza completamente el uso de regex y keywords hardcodeadas

-- Script para actualizar las instrucciones de gestión de contexto en BusinessInformation
-- Ejecutar este script para mejorar las instrucciones sobre cuándo usar update_conversation_state

UPDATE BusinessConfigurations
SET Value = REPLACE(
    Value,
    N'GESTIÓN DE CONTEXTO:
Cuando durante la conversación detectes información importante del cliente,
DEBES llamar a la tool "update_conversation_state".

Información importante incluye:
- customerName
- phone
- babyAgeMonths
- service
- desiredDate
- desiredTime
- reservationConfirmed

Ejemplos de uso de update_conversation_state:
- Cuando el cliente mencione su nombre: field="customerName", value="Juan Pérez"
- Cuando el cliente mencione su teléfono: field="phone", value="+1234567890"
- Cuando el cliente mencione la edad del bebé: field="babyAgeMonths", value="6"
- Cuando el cliente elija un servicio: field="service", value="Plan Marineritos"
- Cuando el cliente mencione una fecha deseada: field="desiredDate", value="2024-01-25"
- Cuando el cliente mencione una hora deseada: field="desiredTime", value="14:30"
- Cuando el cliente confirme explícitamente una reserva: field="reservationConfirmed", value="true"',
    N'GESTIÓN DE CONTEXTO - REGLA CRÍTICA:
SIEMPRE que el cliente mencione información importante, DEBES llamar INMEDIATAMENTE a la tool "update_conversation_state" ANTES de responder. NO respondas sin guardar la información primero.

CASOS OBLIGATORIOS donde DEBES usar update_conversation_state:

1. EDAD DEL BEBÉ (MUY IMPORTANTE - SIEMPRE):
   Si el cliente dice: "mi bebé tiene X meses", "tiene X meses", "X meses", "mi bebé tiene X años", "tiene X años", "X años"
   → INMEDIATAMENTE llama: update_conversation_state con field="babyAgeMonths" y value="X" (si dice años, convierte: 1 año = 12 meses, 2 años = 24 meses)
   Ejemplos:
   - "mi bebé tiene 4 meses" → field="babyAgeMonths", value="4"
   - "tiene 6 meses" → field="babyAgeMonths", value="6"
   - "tiene 1 año" → field="babyAgeMonths", value="12"
   - "mi bebé tiene 2 años" → field="babyAgeMonths", value="24"

2. NOMBRE DEL CLIENTE:
   Si menciona su nombre → field="customerName", value="[nombre]"

3. TELÉFONO:
   Si menciona su teléfono → field="phone", value="[teléfono]"

4. SERVICIO O PLAN:
   Si elige un servicio → field="service", value="[servicio]"

5. FECHA DESEADA:
   Si menciona una fecha → field="desiredDate", value="[fecha]"

6. HORA DESEADA:
   Si menciona una hora → field="desiredTime", value="[hora]"

7. CONFIRMACIÓN DE RESERVA:
   Si confirma explícitamente → field="reservationConfirmed", value="true"

IMPORTANTE: Si el cliente dice "mi bebé tiene 4 meses" en el primer mensaje, DEBES llamar update_conversation_state PRIMERO antes de cualquier otra respuesta.'
)
WHERE [Key] = 0;

PRINT 'Instrucciones de gestión de contexto actualizadas correctamente.';
PRINT 'Ahora la IA debe usar update_conversation_state inmediatamente cuando detecte la edad del bebé.';

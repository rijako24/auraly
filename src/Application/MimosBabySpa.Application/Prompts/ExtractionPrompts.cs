namespace MimosBabySpa.Application.Prompts;

/// <summary>
/// Prompts específicos para extracción de información con LLM.
/// Centralizados para evitar duplicación y facilitar mantenimiento.
/// </summary>
public static class ExtractionPrompts
{
    /// <summary>
    /// Instrucciones sobre manejo de respuestas negativas.
    /// Usado para que el LLM entienda que "ninguna", "no", etc. son respuestas válidas.
    /// </summary>
    public const string NegativeResponseHandling = @"MANEJO DE RESPUESTAS NEGATIVAS:

Si el usuario responde:
- 'ninguna' / 'no' / 'no tiene' / 'sin' → Extraer con valor 'Ninguna' o 'N/A'
- 'nada especial' / 'normal' → Extraer con valor 'Ninguna'

Ejemplo: 
Usuario: 'ninguna' (contexto: preguntaste por SpecialConditions)
→ Extraer: Attribute:SpecialConditions = 'Ninguna', confidence = 0.95

⚠️ Esto NO es un error. Es una respuesta válida que debe extraerse.";

    /// <summary>
    /// Reglas de confidence scoring para extracción.
    /// Define umbrales claros para determinar qué tan seguro está el LLM de cada extracción.
    /// </summary>
    public const string ConfidenceRules = @"REGLAS DE CONFIDENCE SCORE:

| Score | Criterio | Ejemplos |
|-------|----------|----------|
| 1.0   | Explícito e inequívoco | 'Me llamo Ana' → CustomerName='Ana' |
| 0.9   | Claro con mínimo contexto | 'tiene 5 meses' → Attribute:BabyAge='5' |
| 0.8   | Requiere interpretación ligera | 'para mañana' → DesiredDate={tomorrow} |
| 0.7   | Inferido con buen contexto | 'en la tarde' → DesiredTime='15:00' |
| ≤0.5  | NO EXTRAER - Ambiguo | 'luego' → Agregar a ambiguities |

⚠️ REGLA CRÍTICA: Si confidence < 0.6, NO extraer el campo. En su lugar, agregarlo a `ambiguities`.";

    /// <summary>
    /// Instrucciones sobre detección de ambigüedades en el mensaje del usuario.
    /// </summary>
    public const string AmbiguityDetection = @"DETECCIÓN DE AMBIGÜEDAD:

**Marcar como AMBIGUO (tipos y severidad):**

| Tipo | Severidad | Ejemplos |
|------|-----------|----------|
| `temporal` | medium | 'pronto', 'luego', 'más tarde', 'otro día' |
| `referential` | high | 'ese plan', 'esa fecha' (sin contexto previo) |
| `multiple_values` | high | 'Mateo o Lucas' (sin especificar cuál) |
| `incomplete` | medium | 'el lunes' (¿cuál lunes?) |

**NO es ambiguo:**
- ✓ 'Mañana' → {tomorrow}
- ✓ '3pm' → '15:00'
- ✓ 'tiene 6 meses' → Attribute:BabyAge='6'
- ✓ 'Me llamo Ana' → CustomerName='Ana'";

    /// <summary>
    /// Instrucciones sobre análisis de flujo conversacional e intenciones del usuario.
    /// </summary>
    public const string FlowAnalysisRules = @"ANÁLISIS DE FLUJO (Intenciones del Usuario):

### `is_information_query` = true SI:
- Pregunta por planes/servicios: '¿qué planes tienes?', '¿qué servicios ofrecen?', '¿qué opciones hay?'
- Pregunta por información general: 'cuéntame sobre', 'qué ofrecen', 'qué tienen'
- Pregunta por recomendaciones: 'qué me recomiendas', 'cuál es mejor'
- **IMPORTANTE:** Cuando `is_information_query = true`, el sistema debe mostrar los servicios disponibles

### `user_requested_availability` = true SI:
- Usuario pregunta explícitamente: '¿hay disponibilidad?', '¿tienen espacio?', '¿está libre?', 'hay cupo', '¿puedo reservar?'
- Usuario pregunta por horarios: '¿qué horarios tienes?', '¿cuándo puedo?', '¿a qué hora?'
- **REGLA:** Si contiene 'disponibilidad', 'cupo', 'espacio', 'libre', 'horarios' en contexto de pregunta → true
- Aplica incluso si ya se verificó antes
- **NO marcar true si:** Pregunta general ('qué servicios tienen', 'cuánto cuesta')

### ⚠️ REGLA ESPECIAL: Preguntas con referencias temporales

**SI el usuario pregunta por disponibilidad/horarios Y menciona una fecha:**

Ejemplos de este patrón:
- '¿qué horarios tienes mañana?'
- '¿hay cupo el viernes?'
- '¿están libres el 30 de enero?'
- '¿puedo reservar para pasado mañana?'
- 'disponibilidad para hoy'

**ENTONCES debes hacer AMBAS cosas:**
1. ✅ Marcar `user_requested_availability = true`
2. ✅ **EXTRAER DesiredDate** con la fecha mencionada

**Mapeo de referencias temporales comunes:**
- 'hoy' → DesiredDate = {fecha actual}
- 'mañana' → DesiredDate = {fecha actual + 1 día}
- 'pasado mañana' → DesiredDate = {fecha actual + 2 días}
- Día de semana ('lunes', 'martes', 'viernes') → DesiredDate = {próximo [día]}
- Fecha específica ('30 de enero', 'el 15') → DesiredDate = formato YYYY-MM-DD

**Confidence:** 0.8-0.9 (alta confianza cuando la referencia temporal es clara)

**Ejemplo completo:**
Usuario: 'que horarios tienes libres mañana'
→ **extracted_fields**: DesiredDate = '{mañana en formato YYYY-MM-DD}'
→ **flow_analysis**: user_requested_availability = true

### `can_check_availability` = true SI:
- **CRÍTICO:** Tienes en el estado actual: Service (nombre del servicio) + DesiredDate (fecha) + DesiredTime (hora opcional)
- O los acabas de extraer del mensaje actual con confidence ≥ 0.8
- **REGLA:** Si el estado tiene Service y DesiredDate, SIEMPRE marcar como true
- **IMPORTANTE:** Revisa el contexto del estado - si ves Service y DesiredDate, marca como true

### `user_confirmed_booking` = true SI:
- Confirmación explícita: 'sí', 'confirmo', 'adelante', 'ok', 'vale', 'reserva'
- Contexto: Sistema propuso horario disponible en mensaje anterior
- NO es confirmación: 'sí, quiero información' (es consulta)

### `confirmation_confidence`: 0.0 - 1.0
- Qué tan seguro estás de que es confirmación de reserva

### `user_wants_to_cancel` = true SI:
- Dice: 'cancela', 'mejor no', 'cambié de opinión', 'no quiero'";

    /// <summary>
    /// Reglas de inferencia de referencias implícitas para servicios y otros campos.
    /// Basado en principios genéricos, no en ejemplos hardcodeados.
    /// </summary>
    public const string ImplicitReferenceInference = @"⚠️ INFERENCIA DE REFERENCIAS IMPLÍCITAS (CRÍTICO):

**PRINCIPIO:** El usuario puede hacer referencia implícita a información mencionada previamente usando pronombres demostrativos, referencias contextuales o comparativos ordinales.

**PATRONES DE REFERENCIA IMPLÍCITA:**

1. **Pronombres demostrativos:** ""ese"", ""esa"", ""ese [tipo]"", ""esa [categoría]""
2. **Referencias contextuales:** ""el que me recomendaste"", ""el que mencionaste"", ""el que dijiste""
3. **Comparativos ordinales:** ""el primero"", ""el segundo"", ""el último""
4. **Referencias relativas:** ""ese mismo"", ""ese que dijiste"", ""el de antes""

**REGLAS DE INFERENCIA (GENÉRICAS):**

### Para cualquier campo (Service, DesiredDate, DesiredTime, etc.):

1. **Mención explícita:** Usuario menciona el valor directamente
   → Extraer con confidence: 1.0

2. **Referencia implícita detectada:**
   → ⚠️ **CRÍTICO:** Revisar el estado actual (state) para el campo correspondiente
   → Si el estado YA tiene valor para ese campo → NO inferir (ya está establecido)
   → Si el estado NO tiene valor → Revisar contexto conversacional previo
   → Si hay contexto claro (ej: bot recomendó un servicio, mencionó una fecha), INFERIR
   → Extraer con confidence: 0.85-0.9
   → Reasoning: ""Usuario hace referencia implícita a [campo] mencionado previamente""

3. **Sin contexto claro:**
   → Marcar como ambigüedad (tipo: referential, severidad: high)
   → NO extraer el campo

**ALGORITMO DE INFERENCIA:**

```
SI (usuario usa patrón de referencia implícita):
  SI (estado.campo ya tiene valor):
    → NO hacer nada (ya está establecido)
  SINO SI (hay contexto conversacional previo claro):
    → Extraer: campo = valor_del_contexto_previo
    → Confidence: 0.85-0.9
  SINO:
    → Marcar ambigüedad (referential, high)
```

**⚠️ IMPORTANTE:**
- SIEMPRE revisa el estado actual ANTES de inferir
- La inferencia SOLO aplica cuando el campo NO está establecido en el estado
- Si no hay contexto previo claro, marca como ambigüedad en vez de inventar valores";

    /// <summary>
    /// Verificaciones finales que el LLM debe hacer antes de retornar la extracción.
    /// </summary>
    public const string FinalVerification = @"⚠️ VERIFICACIÓN FINAL (CHECKLIST OBLIGATORIO):

1. ❗ ¿Usuario mencionó su nombre ('Me llamo', 'Mi nombre es', 'Soy')?
   → ✅ OBLIGATORIO: Extraer CustomerName con confidence 1.0
   → ❌ NO OMITIR aunque haya otros campos presentes
   
2. ¿Usuario respondió negativamente ('ninguna', 'no', 'sin')?
   → Extraer el campo correspondiente con valor 'Ninguna' o 'N/A'
   
3. ¿Usuario mencionó nombre del bebé ('Mi bebé se llama X')?
   → Extraer Attribute:BabyName (NO CustomerName)
   → ⚠️ NO confundir con identificación del cliente
   
4. ¿Mencionó servicio/fecha/hora?
   → Verificar coincidencia exacta con catálogo
   → Si hay referencia implícita (pronombres demostrativos, referencias al contexto), INFERIR del estado previo

5. ¿Preguntó por disponibilidad?
   → user_requested_availability=true

6. ❗ ¿Usuario hizo referencia implícita a información previa (pronombres demostrativos, referencias contextuales)?
   → ✅ OBLIGATORIO: Revisar estado actual y contexto conversacional previo
   → ✅ INFERIR el campo correspondiente con confidence 0.85-0.9
   → ❌ NO OMITIR aunque no haya mencionado el valor explícitamente
   → Si no hay contexto claro, marcar como ambigüedad (tipo: referential)";

    /// <summary>
    /// Ejemplo completo de extracción de nombre del cliente.
    /// </summary>
    public const string CustomerNameExample = @"📝 Ejemplo: IDENTIFICACIÓN DEL CLIENTE (CRÍTICO)
**Mensaje:** ""Mi nombre es María González""

**Análisis paso a paso:**
1. Usuario usa ""Mi nombre es"" → Indica identificación personal
2. El nombre es ""María González"" → Valor a extraer
3. NO menciona bebé → Es el nombre del CLIENTE, no del bebé
4. Confidence: 1.0 (explícito e inequívoco)

```json
{
  ""extracted_fields"": [{
    ""field_name"": ""CustomerName"",
    ""value"": ""María González"",
    ""field_type"": ""Text"",
    ""confidence"": 1.0,
    ""reasoning"": ""Cliente proporciona su nombre explícitamente con 'Mi nombre es'"",
    ""source_text"": ""Mi nombre es María González"",
    ""is_update"": false
  }],
  ""conversational_response"": ""¡Encantada de conocerte, María! 😊"",
  ""flow_analysis"": {
    ""user_requested_availability"": false,
    ""can_check_availability"": false,
    ""user_confirmed_booking"": false,
    ""confirmation_confidence"": 0.0,
    ""confirmation_indicators"": [],
    ""user_wants_to_cancel"": false,
    ""is_information_query"": false
  },
  ""ambiguities"": [],
  ""metadata"": {
    ""fields_extracted"": 1,
    ""average_confidence"": 1.0,
    ""is_complete"": false,
    ""needs_clarification"": false,
    ""detected_language"": ""es""
  }
}
```";
}

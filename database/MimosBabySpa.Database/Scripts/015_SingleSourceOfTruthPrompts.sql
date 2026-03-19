-- =============================================================================
-- Migration 015: Single Source of Truth for Domain Data
--
-- Problem 1: Script 013 used hardcoded IDs that don't match 009's NEWID(), so it
--   created ORPHAN knowledge sources. The agent still uses the OLD ones (Baby Spa
--   Básico/Premium/Duo). Fix: UPDATE the linked KnowledgeSources in-place.
--
-- Problem 2: Prompts, extractionHint, extractionInstructions, and node instructions
--   had hardcoded service names. Fix: make them generic, reference "the catalog".
--
-- Solution: Update linked KS content + make all prompts generic.
-- =============================================================================

BEGIN TRANSACTION;

DECLARE @AgentId   UNIQUEIDENTIFIER;
DECLARE @FlowDefId UNIQUEIDENTIFIER;

SELECT TOP 1 @AgentId = a.AgentId FROM [dbo].[Agents] a WHERE a.Name = 'Mimo Bot';
SELECT TOP 1 @FlowDefId = fd.FlowDefinitionId
FROM [dbo].[FlowDefinitions] fd
WHERE fd.AgentId = @AgentId AND fd.IsActive = 1;

IF @AgentId IS NULL OR @FlowDefId IS NULL
BEGIN
    RAISERROR('Mimo Bot or active FlowDefinition not found.', 16, 1);
    ROLLBACK TRANSACTION; RETURN;
END

-- =============================================================================
-- 0. KnowledgeSources — UPDATE the ones LINKED to Mimo Bot (fix 013 orphan issue)
--    Script 013 used fixed IDs; 009 used NEWID(). The linked sources have old content.
-- =============================================================================

DECLARE @CatalogContent NVARCHAR(MAX) = N'
## PLANES BABY SPA
Sesiones de bienestar diseñadas para relajar, estimular y fortalecer el vínculo con tu bebé, en un entorno seguro y amoroso.

---

### Plan Marineritos
**Precio:** $125.000 | **Duración:** 60 minutos
**El plan más completo.** Integra tres estaciones especialmente diseñadas para el desarrollo y bienestar del bebé.

**Incluye:**
- Estimulación temprana en Baby Gym (actividades guiadas según edad y etapa de desarrollo, enfocadas en habilidades motoras, sensoriales, cognitivas y de interacción)
- Hidroterapia en tinas especiales para bebés (experiencia acuática en ambiente cálido y seguro)
- Masaje de relajación (relajación, conexión y fortalecimiento del vínculo afectivo)

**Beneficios:**
- Favorece el desarrollo motor y sensorial
- Promueve la relajación y el bienestar
- Estimula la coordinación y el movimiento libre
- Fortalece el vínculo afectivo
- Brinda una experiencia integral y enriquecedora

**Opciones de Cumplemes (celebración especial en el área de la tina):**
- Con decoración sencilla: $175.000
- Con decoración personalizada: $195.000

---

### Plan Aventuras Marinas
**Precio:** $100.000 | **Duración:** 45 minutos
Experiencia de relajación y bienestar a través de dos estaciones principales.

**Incluye:**
- Hidroterapia en tinas especiales para bebés
- Masaje de relajación

**Beneficios:**
- Promueve la relajación del bebé
- Favorece el movimiento corporal
- Ayuda a liberar tensiones
- Brinda bienestar físico y emocional
- Ofrece una experiencia especial de conexión y cuidado

**Opciones de Cumplemes:**
- Con decoración sencilla: $150.000
- Con decoración personalizada: $170.000

---

### Plan Suaves Mimos – Post Vacunas
**Precio:** $95.000 | **Duración:** 45 minutos
Diseñado para brindar alivio, relajación y bienestar al bebé después de su proceso de vacunación.
⚠️ El masaje NO toca la zona de punción.

**Incluye:**
- Hidroterapia relajante en tinas especiales para bebés
- Masaje de relajación (respetando la zona de vacunación)

**Beneficios:**
- Ayuda a promover relajación y calma post-vacunas
- Favorece el bienestar corporal del bebé
- Aporta una experiencia de cuidado amoroso posterior a la vacunación
- Acompaña a los padres en este momento con atención cálida y profesional

**Opciones de Cumplemes:**
- Con decoración sencilla: $145.000
- Con decoración personalizada: $165.000

---

## ESTIMULACIÓN TEMPRANA

### Talleres Grupales de Estimulación Temprana
Clases grupales organizadas por edades similares. Los bebés aprenden jugando, interactúan con otros niños y fortalecen su desarrollo integral.

**Tarifas:**
- 1 clase suelta: $80.000
- 1 día por semana (mensual): $260.000
- 2 días por semana (mensual): $320.000
- 3 días por semana (mensual): $380.000

**Grupos por edad:**
- Estrellitas de Mar: 2 a 4 meses
- Pulpitos: 4 a 7 meses
- Cangrejitos: 7 a 10 meses
- Tiburoncitos 1: 10 a 13 meses
- Tiburoncitos 2: 13 meses en adelante

**Beneficios:**
- Socialización temprana
- Estimulación de múltiples áreas del desarrollo
- Las familias comparten experiencias enriquecedoras
- Ambiente dinámico, amoroso y guiado por profesionales
- Fortalecen habilidades según cada etapa

---

### Estimulación Temprana Personalizada
Sesiones individuales enfocadas en las necesidades específicas de cada bebé. Incluyen sesiones en agua en tinas especiales.

**Tarifas:**
- 1 clase suelta: $95.000
- Paquete 4 clases: $310.000
- Paquete 8 clases: $410.000
- Paquete 12 clases: $490.000

**Incluye:**
- Valoración general de la etapa del desarrollo
- Actividades personalizadas según las necesidades del bebé
- Orientación para los padres (refuerzo en casa)
- Acompañamiento profesional durante toda la sesión
- Sesiones de Hidroterapia en tinas especiales para bebés

**Áreas de trabajo:** Motricidad gruesa, motricidad fina, lenguaje, área cognitiva, área sensorial, área socioemocional.

**Beneficios:**
- Atención totalmente personalizada
- Actividades adaptadas a cada necesidad
- Seguimiento más cercano del proceso
- Mayor orientación para la familia
- Acompañamiento respetuoso y especializado

---

## MATERNO SPA
Espacio especial de autocuidado, relajación y conexión para mamás gestantes.

### Materno Spa – Opción 1: Con Hidratación Facial
**Incluye:** Masaje de cuerpo completo + Hidratación facial
**Beneficios:** Favorece la relajación, disminuye tensiones musculares, brinda bienestar físico y emocional, momento especial de autocuidado.
*(Precio a consultar)*

### Materno Spa – Opción 2: Con Hidratación Corporal en Barriguita
**Incluye:** Masaje de cuerpo completo + Hidratación corporal en barriguita
**Beneficios:** Promueve relajación, ayuda al cuidado de la piel, favorece la conexión con la etapa de maternidad.
*(Precio a consultar)*

---

## DULCE ESPERA – Preparación para el parto
Sesión personalizada para acompañar a la mamá gestante con herramientas físicas y emocionales para vivir el embarazo con mayor seguridad y bienestar.

**Incluye:**
- Yoga prenatal
- Ejercicios de respiración para manejo de tensiones y dolor
- Fortalecimiento de suelo pélvico, abdomen y espalda
- Técnicas de pilates
- Preparación para gestación, parto y postparto

**Beneficios:** Mejora conciencia corporal, manejo de tensiones, fortalece músculos clave, herramientas de respiración y relajación.
*(Precio a consultar)*

---

## PROGRAMA INICIACIÓN AL JARDÍN
Diseñado para acompañar a los niños en su transición a la etapa escolar, fortaleciendo autonomía, habilidades sociales y adaptación a rutinas.

**Dirigido a:** Niños de aproximadamente 18 meses a 3 años
**Horario:** Lunes a viernes, 8:00 a.m. – 11:30 a.m.

**Tarifas:**
- Mensualidad: $380.000
- Inscripción (pago único): $100.000
- Uniforme: próximamente disponible

**La jornada incluye:** Saludo y bienvenida, juego libre, merienda, actividad central, rutinas de participación, canción de despedida.

**Beneficios:**
- Facilita la preparación para la etapa escolar
- Fortalece habilidades sociales y emocionales
- Promueve hábitos y rutinas
- Estimula el aprendizaje a través de la lúdica
- Favorece la independencia progresiva

---

## CUMPLEMES MIMOS
Celebración especial disponible como complemento para cualquier plan Baby Spa. Decoración especial en el área de la tina para hacer aún más memorable el cumplemes del bebé.
- Decoración sencilla: se suma al precio del plan elegido (ver precios por plan arriba)
- Decoración personalizada: se suma al precio del plan elegido (ver precios por plan arriba)
';

DECLARE @FaqContent NVARCHAR(MAX) = N'
**P: ¿Desde qué edad pueden venir los bebés?**
R: Recibimos bebés desde el primer mes de vida. Cada plan está adaptado a la edad y etapa de desarrollo del bebé.

**P: ¿Cuál es la diferencia entre los planes Baby Spa?**
R: El Plan Marineritos (60 min, $125.000) es el más completo: incluye estimulación en Baby Gym, hidroterapia y masaje. El Plan Aventuras Marinas (45 min, $100.000) incluye hidroterapia y masaje. El Plan Suaves Mimos Post Vacunas (45 min, $95.000) es especial para bebés recién vacunados, con hidroterapia y masaje sin tocar la zona de punción.

**P: ¿Qué es el Cumplemes y cómo funciona?**
R: Es una decoración especial en el área de la tina para celebrar el cumplemes del bebé. Aplica para cualquier plan Baby Spa. La decoración sencilla tiene un valor adicional de $50.000 y la personalizada de $70.000 sobre el plan base.

**P: ¿Cuál es la diferencia entre la estimulación grupal y la personalizada?**
R: Los talleres grupales organizan a los bebés por grupos de edad (Estrellitas de Mar, Pulpitos, Cangrejitos, Tiburoncitos) y son más económicos (desde $80.000 la clase). La estimulación personalizada es individual, con valoración y actividades diseñadas específicamente para tu bebé; incluye también hidroterapia (desde $95.000 la clase o paquetes de 4, 8 o 12 clases).

**P: ¿Qué grupo de estimulación le corresponde a mi bebé?**
R: Los grupos son: Estrellitas de Mar (2-4 meses), Pulpitos (4-7 meses), Cangrejitos (7-10 meses), Tiburoncitos 1 (10-13 meses), Tiburoncitos 2 (13 meses en adelante).

**P: ¿El Materno Spa es solo para embarazadas?**
R: Sí, está diseñado especialmente para mamás gestantes. Tenemos dos opciones: con hidratación facial o con hidratación corporal en barriguita. Consulta el precio directamente.

**P: ¿Qué es Dulce Espera?**
R: Es una sesión personalizada de preparación para el parto que incluye yoga prenatal, respiración, pilates y fortalecimiento de suelo pélvico. Ideal para mamás que quieren prepararse física y emocionalmente. Consulta disponibilidad y precio.

**P: ¿A qué edad aplica el Programa de Iniciación al Jardín?**
R: Está dirigido a niños de aproximadamente 18 meses a 3 años. El horario es lunes a viernes de 8:00 a.m. a 11:30 a.m. La mensualidad es $380.000 y la inscripción (pago único) $100.000.

**P: ¿Cómo puedo agendar una sesión?**
R: Puedes agendar directamente por este chat. Cuéntame qué plan te interesa y te ayudo a verificar disponibilidad.

**P: ¿Qué formas de pago aceptan?**
R: Aceptamos pagos por transferencia y link de pago. Una vez confirmada tu cita, te envío el link para que puedas pagar de forma segura.

**P: ¿Con cuánto tiempo de anticipación debo reservar?**
R: Recomendamos reservar con al menos 24-48 horas de anticipación para asegurar el horario que prefieres.

**P: ¿Qué pasa si necesito cancelar o reprogramar?**
R: Puedes cancelar o reprogramar sin problema. Solo avísanos con anticipación para liberar el espacio a otras familias.
';

DECLARE @ProfileContent NVARCHAR(MAX) = N'
**Nombre:** Mimos Baby Spa
**Tipo:** Centro de bienestar maternoinfantil
**Ubicación:** Cesar, Colombia
**Slogan:** Mimos: donde la infancia se vive, se siente y se fortalece.

**Misión:** Brindar experiencias de bienestar maternoinfantil con amor, calidez y profesionalismo, acompañando a bebés, niños y mamás en sus procesos de desarrollo, cuidado y conexión.

**Visión:** Ser el centro de bienestar maternoinfantil de referencia en el Cesar.

**Servicios principales:** Planes Baby Spa (Marineritos, Aventuras Marinas, Post Vacunas), Estimulación Temprana Grupal y Personalizada, Materno Spa, Dulce Espera, Programa de Iniciación al Jardín, Cumplemes.

**Diferencial:** Integración de bienestar, estimulación y experiencias memorables en una sola marca. Atención cálida, cercana y respetuosa. Enfoque integral maternoinfantil. Personalización de servicios.

**Público objetivo:** Bebés desde el primer mes de vida, niños hasta 3 años, mamás gestantes y familias.
';

-- Use Content or ContentJson depending on schema (013 renamed ContentJson → Content)
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.KnowledgeSources') AND name = N'Content')
BEGIN
    -- Schema post-013: column is Content
    UPDATE ks SET ks.[Content] = @CatalogContent, ks.[Name] = N'Catálogo de Servicios – Mimos Baby Spa', ks.[UpdatedAt] = GETUTCDATE()
    FROM [dbo].[KnowledgeSources] ks
    INNER JOIN [dbo].[AgentKnowledgeSources] aks ON ks.KnowledgeSourceId = aks.KnowledgeSourceId
    WHERE aks.AgentId = @AgentId AND ks.[Type] = 0;

    UPDATE ks SET ks.[Content] = @FaqContent, ks.[Name] = N'Preguntas Frecuentes – Mimos Baby Spa', ks.[UpdatedAt] = GETUTCDATE()
    FROM [dbo].[KnowledgeSources] ks
    INNER JOIN [dbo].[AgentKnowledgeSources] aks ON ks.KnowledgeSourceId = aks.KnowledgeSourceId
    WHERE aks.AgentId = @AgentId AND ks.[Type] = 3;

    UPDATE ks SET ks.[Content] = @ProfileContent, ks.[Name] = N'Perfil – Mimos Baby Spa', ks.[UpdatedAt] = GETUTCDATE()
    FROM [dbo].[KnowledgeSources] ks
    INNER JOIN [dbo].[AgentKnowledgeSources] aks ON ks.KnowledgeSourceId = aks.KnowledgeSourceId
    WHERE aks.AgentId = @AgentId AND ks.[Type] = 5;
END
ELSE IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.KnowledgeSources') AND name = N'ContentJson')
BEGIN
    -- Schema pre-013: column is still ContentJson
    UPDATE ks SET ks.[Content] = @CatalogContent, ks.[Name] = N'Catálogo de Servicios – Mimos Baby Spa', ks.[UpdatedAt] = GETUTCDATE()
    FROM [dbo].[KnowledgeSources] ks
    INNER JOIN [dbo].[AgentKnowledgeSources] aks ON ks.KnowledgeSourceId = aks.KnowledgeSourceId
    WHERE aks.AgentId = @AgentId AND ks.[Type] = 0;

    UPDATE ks SET ks.[Content] = @FaqContent, ks.[Name] = N'Preguntas Frecuentes – Mimos Baby Spa', ks.[UpdatedAt] = GETUTCDATE()
    FROM [dbo].[KnowledgeSources] ks
    INNER JOIN [dbo].[AgentKnowledgeSources] aks ON ks.KnowledgeSourceId = aks.KnowledgeSourceId
    WHERE aks.AgentId = @AgentId AND ks.[Type] = 3;

    UPDATE ks SET ks.[Content] = @ProfileContent, ks.[Name] = N'Perfil – Mimos Baby Spa', ks.[UpdatedAt] = GETUTCDATE()
    FROM [dbo].[KnowledgeSources] ks
    INNER JOIN [dbo].[AgentKnowledgeSources] aks ON ks.KnowledgeSourceId = aks.KnowledgeSourceId
    WHERE aks.AgentId = @AgentId AND ks.[Type] = 5;
END

-- =============================================================================
-- 1. AgentPromptSections — sales_strategy: generic, no service names
-- =============================================================================

UPDATE [dbo].[AgentPromptSections]
SET [Content] = N'ESTRATEGIA DE PRESENTACIÓN DE SERVICIOS (según qué pregunta el cliente):

1) Cliente NO pregunta por un plan o servicio específico (explora en general, dice edad del bebé, pregunta "qué tienen"):
   - Presenta las CATEGORÍAS de servicios disponibles (Baby Spa, Estimulación Temprana, Materno Spa, etc.) de forma breve.
   - Si conoces la edad del bebé, menciona qué categorías o planes se adaptan mejor (ej: 5 meses → Baby Spa, Talleres Grupales Pulpitos).
   - No entres en detalles de un solo plan. Invita a que pregunte por lo que le interese.

2) Cliente pregunta por UN plan o servicio específico (ej: "qué es el Marineritos", "beneficios del Plan Aventuras Marinas"):
   - Entra en detalle SOLO de ese plan: qué incluye, beneficios, precios exactos.
   - Si tiene extras compatibles (Cumplemes), menciónalos brevemente.
   - Usa nombres y precios EXACTOS del catálogo.

3) Cliente explora una categoría (ej: "cuéntame de los planes Baby Spa"):
   - Presenta los planes de esa categoría. El de mayor valor primero como "la experiencia más completa", luego alternativas más accesibles.
   - Para cada plan: incluye SIEMPRE los add-ons/complementos si los tiene (ej. Cumplemes en planes Baby Spa: decoración sencilla, decoración personalizada, con sus precios). Es crucial para la venta.
   - Nombres y precios exactos. No inventes.

General: Usa SIEMPRE nombres y precios exactos del catálogo. NO menciones precios de forma abrupta. Cierre: invitación suave.',
    [UpdatedAt] = GETUTCDATE()
WHERE [AgentId] = @AgentId AND [Key] = 'sales_strategy';

-- =============================================================================
-- 2. FlowDefinition JSON — variable service: generic extractionHint
-- =============================================================================

DECLARE @Json NVARCHAR(MAX);
SELECT @Json = [DefinitionJson] FROM [dbo].[FlowDefinitions] WHERE [FlowDefinitionId] = @FlowDefId;

-- Find index of "service" variable and update extractionHint
DECLARE @varIdx INT = 0;
DECLARE @found BIT = 0;

SELECT @varIdx = CAST([key] AS INT), @found = 1
FROM OPENJSON(JSON_QUERY(@Json, '$.variables'))
WHERE JSON_VALUE(value, '$.key') = 'service';

IF @found = 1
    SET @Json = JSON_MODIFY(@Json,
        '$.variables[' + CAST(@varIdx AS NVARCHAR) + '].extractionHint',
        N'Nombre EXACTO de un servicio o plan del catálogo inyectado en esta conversación. Si el usuario usa alias o nombre parcial, resolver al nombre oficial que aparece en el catálogo.');

-- =============================================================================
-- 3. FlowDefinition — extractionInstructions: generic, no service mappings
-- =============================================================================

DECLARE @ExtractionInstructions NVARCHAR(MAX) = N'Contexto temporal:
- Hoy: {{runtime.today}} ({{runtime.day_of_week}})
- Mañana: {{runtime.tomorrow}}
- Pasado mañana: {{runtime.day_after_tomorrow}}
- Hora actual: {{runtime.current_time}}

Reglas de fechas (mapeo obligatorio):
- "hoy" → {{runtime.today}}
- "mañana" → {{runtime.tomorrow}}
- "pasado mañana" → {{runtime.day_after_tomorrow}}
- Días de semana ("el viernes", "el próximo lunes") → próxima ocurrencia futura desde hoy.
- Número de día solo ("el 15", "para el 29") → ese día en el mes actual si aún no pasó, o en el mes siguiente si ya pasó.
- Si el usuario pide disponibilidad u horarios mencionando una fecha → extraer la fecha correspondiente.

Reglas de horas (mapeo obligatorio):
- Convertir siempre a HH:MM formato 24h.
- "9am" → "09:00", "2pm" → "14:00", "mediodía" → "12:00", "a las 3" → "15:00" según contexto.

Reglas de resolución contextual:
- Valor directo: el usuario proporciona el dato explícitamente ("quiero el Plan Marineritos", "a las 10", "el viernes").
- Aceptación por referencia: el asistente mostró opciones y el usuario acepta ("sí", "ese", "la primera", "esa", "está bien") → resolver al valor EXACTO del catálogo usando el historial de conversación.
- Nombre parcial o variación: resolver al nombre exacto del catálogo.
- Si hay varios candidatos o la referencia es ambigua → marcar como ambigüedad, confidence < 0.6, no extraer.

Confianza esperada:
- Dato explícito e inequívoco: 0.95
- Referencia resuelta con certeza desde historial: 0.90
- Referencia ambigua: 0.65–0.70';

SET @Json = JSON_MODIFY(@Json, '$.extractionInstructions', @ExtractionInstructions);

-- =============================================================================
-- 4. Node instructions — generic, no service names
-- =============================================================================

DECLARE @idx INT;

-- info_response
SELECT @idx = CAST([key] AS INT)
FROM OPENJSON(JSON_QUERY(@Json, '$.nodes'))
WHERE JSON_VALUE(value, '$.id') = 'info_response';

IF @idx IS NOT NULL
    SET @Json = JSON_MODIFY(@Json,
        '$.nodes[' + CAST(@idx AS NVARCHAR) + '].config.instructions',
        N'El usuario pide información específica — responde usando el catálogo y FAQ.

Si NO pregunta por un plan concreto: presenta CATEGORÍAS (Baby Spa, Estimulación Temprana, Materno Spa, etc.). Si conoces edad del bebé, indica qué se adapta. No detalles un solo plan.
Si pregunta por UN plan específico: detalla SOLO ese plan (qué incluye, beneficios, precio exacto).
Si explora una categoría ("planes Baby Spa"): presenta planes de esa categoría. Mayor valor primero. Para cada plan, incluye SIEMPRE los add-ons (Cumplemes, etc.) con precios exactos — son clave de venta. NO inventes.');

-- detect_intent — el contexto compartido sin solicitud explícita es "other", no "reservation"
SELECT @idx = CAST([key] AS INT)
FROM OPENJSON(JSON_QUERY(@Json, '$.nodes'))
WHERE JSON_VALUE(value, '$.id') = 'detect_intent';

IF @idx IS NOT NULL
    SET @Json = JSON_MODIFY(@Json,
        '$.nodes[' + CAST(@idx AS NVARCHAR) + '].config.prompt',
        N'Clasifica la intención principal del usuario en exactamente uno de estos valores: reservation | information | other

DEFINICIONES:
- reservation: El usuario quiere explícitamente reservar o agendar una cita. Puede incluir datos de reserva (servicio, fecha, hora) con clara intención de completarla.
- information: El usuario pregunta por precios, qué incluye un servicio, horarios del negocio, diferencias entre opciones o pide información específica.
- other: El usuario solo saluda, envía un mensaje genérico o comparte contexto personal (edad del bebé, vacunas, etc.) SIN hacer una solicitud de reserva ni de información. Ejemplos de "other": "Hola", "Buenos días", "Mi bebé tiene 3 meses", "Hola, mi niña tiene 4 meses y acaba de vacunarse".

CRITERIO CLAVE: Compartir datos del bebé (edad, situación) sin pedir reserva ni información = other. Solo clasifica como "reservation" cuando la intención de agendar sea explícita.');

-- collect_service — instrucciones con prioridad de saludo en primer mensaje
SELECT @idx = CAST([key] AS INT)
FROM OPENJSON(JSON_QUERY(@Json, '$.nodes'))
WHERE JSON_VALUE(value, '$.id') = 'collect_service';

IF @idx IS NOT NULL
    SET @Json = JSON_MODIFY(@Json,
        '$.nodes[' + CAST(@idx AS NVARCHAR) + '].config.instructions',
        N'El sistema indica si es el primer mensaje de la sesión en el "CONTEXTO DE SESIÓN".

▸ PRIMER MENSAJE (Primera interacción de la sesión: Sí):
  PRIORIDAD ABSOLUTA — ignora la directiva de "campos faltantes". No preguntes por el servicio ni por ningún dato todavía.
  - Saluda con calidez y preséntate (quién eres, de qué negocio).
  - Si el mensaje trae datos o contexto (edad del bebé, motivo de consulta), reconócelo en la respuesta.
  - Cierra invitando a contar qué necesitan, de forma natural.

▸ CLIENTE RECURRENTE (Cliente recurrente: Sí, sesión previa registrada):
  - NO te presentes de nuevo.
  - Saluda reconociendo que regresa.
  - Si solo saluda → pregunta en qué puedes ayudarle. No presentes catálogo sin que lo pida.
  - Si trae pregunta o datos → respóndelos después del saludo.

▸ CONVERSACIÓN EN CURSO (Primera interacción de la sesión: No):
  - Si el usuario NO pregunta por un plan específico (explora en general, dice edad del bebé) → presenta CATEGORÍAS de servicios. Si conoces la edad, indica qué se adapta (ej: 5 meses → Baby Spa, Talleres Pulpitos). No detalles un solo plan todavía.
  - Si el usuario pregunta por UN plan o servicio concreto → detalla ese plan (incluye, beneficios, precio exacto).
  - Si acepta un plan que recomendaste ("sí ese plan", "está bien") → el sistema extraerá el servicio. Procede a siguiente paso (fecha/hora).
  - NO preguntes fecha, hora ni datos personales — eso va después de elegir servicio.');

-- =============================================================================
-- 5. Persist
-- =============================================================================

UPDATE [dbo].[FlowDefinitions]
SET [DefinitionJson] = @Json, [UpdatedAt] = GETUTCDATE()
WHERE [FlowDefinitionId] = @FlowDefId;

-- =============================================================================
-- Verify
-- =============================================================================

SELECT [Key], LEFT([Content], 120) AS ContentPreview
FROM [dbo].[AgentPromptSections]
WHERE [AgentId] = @AgentId AND [Key] = 'sales_strategy';

SELECT JSON_VALUE(@Json, '$.variables[3].extractionHint') AS ServiceExtractionHint;
SELECT LEFT(JSON_VALUE(@Json, '$.extractionInstructions'), 200) AS ExtractionInstructionsPreview;

-- Confirmar que el catálogo vinculado muestra Marineritos/Aventuras Marinas (no Baby Spa Básico)
SELECT ks.Name, LEFT(ks.[Content], 150) AS CatalogPreview
FROM [dbo].[KnowledgeSources] ks
INNER JOIN [dbo].[AgentKnowledgeSources] aks ON ks.KnowledgeSourceId = aks.KnowledgeSourceId
WHERE aks.AgentId = @AgentId AND ks.[Type] = 0;

COMMIT TRANSACTION;
PRINT '=== Migration 015 completed. Single source of truth: catalog only. ===';
GO

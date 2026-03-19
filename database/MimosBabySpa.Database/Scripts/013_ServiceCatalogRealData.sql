-- =============================================================================
-- 013_ServiceCatalogRealData.sql
--
-- 1. Renames KnowledgeSources.ContentJson column to Content.
-- 2. Replaces the Mimo Bot knowledge sources with real data from official PDFs:
--      - Portafolio General de Servicios (2026)
--      - Mimos Baby Spa Planes 2026
--      - Programa Estimulación Temprana 2026
--      - Programa Iniciación al Jardín 2026
--
-- Content is stored as PLAIN TEXT ready for LLM injection.
-- The engine does not interpret the content — it injects it as-is.
-- =============================================================================

-- ── 1. Rename ContentJson → Content ──────────────────────────────────────────
IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE  object_id = OBJECT_ID(N'dbo.KnowledgeSources')
      AND  name      = N'ContentJson'
)
BEGIN
    EXEC sp_rename 'dbo.KnowledgeSources.ContentJson', 'Content', 'COLUMN';
    PRINT 'Column KnowledgeSources.ContentJson renamed to Content.';
END
ELSE
BEGIN
    PRINT 'Column Content already exists (or ContentJson not found) — skipping rename.';
END
GO

-- ── 2. Declare variables for reuse ───────────────────────────────────────────
DECLARE @BusinessId   UNIQUEIDENTIFIER = (SELECT TOP 1 BusinessId FROM dbo.Businesses ORDER BY CreatedAt);
DECLARE @CatalogId    UNIQUEIDENTIFIER = '73276FF1-E349-46B9-9691-7047BAF98E3D';
DECLARE @FaqId        UNIQUEIDENTIFIER = '399237DC-6885-4641-8394-BF7B3CBF4B69';
DECLARE @ProfileId    UNIQUEIDENTIFIER = '41F884C5-A6FB-4650-93CA-E5243BDE8225';
DECLARE @Now          DATETIME         = GETUTCDATE();

-- =============================================================================
-- ── 3. CATÁLOGO DE SERVICIOS ──────────────────────────────────────────────────
-- =============================================================================
DECLARE @Catalog NVARCHAR(MAX) = N'
## PLANES BABY SPA
Sesiones de bienestar diseñadas para relajar, estimular y fortalecer el vínculo con tu bebé, en un entorno seguro y amoroso.

---

### Plan Marineritos
**Precio:** $125.000 | **Duración:** 60 minutos
**El plan más completo.** Integra tres estaciones especialmente diseñadas para el desarrollo y bienestar del bebé.

**Incluye:**
- Estimulación temprana en Baby Gym (actividades guiadas según edad y etapa de desarrollo, enfocadas en habilidades motoras, sensoriales, cognitivas y de interacción)
- Hidroterapia en tinas especiales para bebés (experiencia acuática en ambiente cálido y seguro)
- Masaje infantil (relajación, conexión y fortalecimiento del vínculo afectivo)

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
- Masaje infantil

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
- Masaje infantil (respetando la zona de vacunación)

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

-- ── Update or insert Catálogo ─────────────────────────────────────────────────
IF EXISTS (SELECT 1 FROM dbo.KnowledgeSources WHERE KnowledgeSourceId = @CatalogId)
BEGIN
    UPDATE dbo.KnowledgeSources
    SET    Name      = N'Catálogo de Servicios – Mimos Baby Spa',
           Content   = @Catalog,
           Type      = 0, -- ServiceCatalog
           IsActive  = 1,
           UpdatedAt = @Now
    WHERE  KnowledgeSourceId = @CatalogId;
    PRINT 'Catálogo de servicios actualizado.';
END
ELSE
BEGIN
    INSERT INTO dbo.KnowledgeSources
        (KnowledgeSourceId, BusinessId, Name, Type, Content, IsActive, CreatedAt, UpdatedAt)
    VALUES
        (@CatalogId, @BusinessId, N'Catálogo de Servicios – Mimos Baby Spa', 0, @Catalog, 1, @Now, @Now);
    PRINT 'Catálogo de servicios insertado.';
END
GO

-- =============================================================================
-- ── 4. PREGUNTAS FRECUENTES ───────────────────────────────────────────────────
-- =============================================================================
DECLARE @FaqId   UNIQUEIDENTIFIER = '399237DC-6885-4641-8394-BF7B3CBF4B69';
DECLARE @Now     DATETIME         = GETUTCDATE();
DECLARE @Faq NVARCHAR(MAX) = N'
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

IF EXISTS (SELECT 1 FROM dbo.KnowledgeSources WHERE KnowledgeSourceId = @FaqId)
BEGIN
    UPDATE dbo.KnowledgeSources
    SET    Name      = N'Preguntas Frecuentes – Mimos Baby Spa',
           Content   = @Faq,
           Type      = 3, -- FAQ
           IsActive  = 1,
           UpdatedAt = @Now
    WHERE  KnowledgeSourceId = @FaqId;
    PRINT 'FAQ actualizado.';
END
ELSE
BEGIN
    DECLARE @BusinessId2 UNIQUEIDENTIFIER = (SELECT TOP 1 BusinessId FROM dbo.Businesses ORDER BY CreatedAt);
    INSERT INTO dbo.KnowledgeSources
        (KnowledgeSourceId, BusinessId, Name, Type, Content, IsActive, CreatedAt, UpdatedAt)
    VALUES
        (@FaqId, @BusinessId2, N'Preguntas Frecuentes – Mimos Baby Spa', 3, @Faq, 1, @Now, @Now);
    PRINT 'FAQ insertado.';
END
GO

-- =============================================================================
-- ── 5. PERFIL DEL NEGOCIO ─────────────────────────────────────────────────────
-- =============================================================================
DECLARE @ProfileId  UNIQUEIDENTIFIER = '41F884C5-A6FB-4650-93CA-E5243BDE8225';
DECLARE @Now2       DATETIME         = GETUTCDATE();
DECLARE @Profile NVARCHAR(MAX) = N'
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

IF EXISTS (SELECT 1 FROM dbo.KnowledgeSources WHERE KnowledgeSourceId = @ProfileId)
BEGIN
    UPDATE dbo.KnowledgeSources
    SET    Name      = N'Perfil – Mimos Baby Spa',
           Content   = @Profile,
           Type      = 5, -- BusinessProfile
           IsActive  = 1,
           UpdatedAt = @Now2
    WHERE  KnowledgeSourceId = @ProfileId;
    PRINT 'Perfil del negocio actualizado.';
END
ELSE
BEGIN
    DECLARE @BusinessId3 UNIQUEIDENTIFIER = (SELECT TOP 1 BusinessId FROM dbo.Businesses ORDER BY CreatedAt);
    INSERT INTO dbo.KnowledgeSources
        (KnowledgeSourceId, BusinessId, Name, Type, Content, IsActive, CreatedAt, UpdatedAt)
    VALUES
        (@ProfileId, @BusinessId3, N'Perfil – Mimos Baby Spa', 5, @Profile, 1, @Now2, @Now2);
    PRINT 'Perfil del negocio insertado.';
END
GO

PRINT '=== Migración 013 completada. Catálogo, FAQ y Perfil actualizados con datos reales. ===';
GO

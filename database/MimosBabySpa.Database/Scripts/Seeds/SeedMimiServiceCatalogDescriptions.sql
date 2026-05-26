-- =============================================================================
-- SeedMimiServiceCatalogDescriptions.sql
--
-- Descripciones canónicas del catálogo Mimos Baby Spa (portafolio y planes 2026).
-- Actualiza por ServiceId fijo del negocio dev 22222222-2222-2222-2222-222222222222.
-- Idempotente en cada publish.
-- =============================================================================

SET NOCOUNT ON;

DECLARE @BusinessId UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';

IF NOT EXISTS (SELECT 1 FROM dbo.Businesses WHERE BusinessId = @BusinessId)
BEGIN
    PRINT N'SeedMimiServiceCatalogDescriptions: business 22222222 not found - skipping.';
    RETURN;
END

DECLARE @DescPlanMarineritos NVARCHAR(MAX) = N'Plan integral de Baby Spa para bebés desde los primeros meses (recomendado 0 a 12 meses). Duración aproximada 60 minutos. Incluye tres estaciones: estimulación temprana en Baby Gym (actividades según edad y etapa: motricidad, sensorial, cognitivo e interacción); hidroterapia en tinas especiales para bebés (ambiente cálido y seguro: relajación, movimiento libre y estimulación corporal); masaje infantil (calma, percepción corporal y vínculo afectivo). Beneficios: desarrollo motor y sensorial, relajación, coordinación, vínculo familiar y experiencia integral de bienestar.';

DECLARE @DescPlanAventurasMarinas NVARCHAR(MAX) = N'Plan de Baby Spa para bebés desde los primeros meses (recomendado 0 a 12 meses). Duración aproximada 45 minutos. Incluye hidroterapia en tinas especiales para bebés y masaje infantil. Pensado para relajación y bienestar: favorece el movimiento corporal, ayuda a liberar tensiones y fortalece la conexión y el cuidado entre la familia y el bebé. Ideal cuando buscas una experiencia amorosa y especial centrada en el agua y el contacto.';

DECLARE @DescPlanPostVacunas NVARCHAR(MAX) = N'Plan Post Vacunas (también conocido como Suaves Mimos): diseñado para después del proceso de vacunación, cuando el bebé puede sentir incomodidad, irritabilidad o tensión. Duración aproximada 45 minutos. Incluye hidroterapia relajante en tinas con agua tibia y masaje infantil suave sin tocar la zona de punción. Beneficios: promueve relajación y calma, favorece el bienestar corporal, ayuda a acompañar a la familia en este momento sensible y puede mejorar el estado de ánimo y el descanso. Recomendado para bebés en etapa de vacunación (consultar si hay fiebre o malestar antes de asistir).';

DECLARE @DescTallerBase NVARCHAR(MAX) = N'Programa de Estimulación Temprana - Talleres grupales Mimos 2026. Clases organizadas por edades similares; el bebé se asigna al grupo según su edad: Estrellitas de Mar (2 a 4 meses), Pulpitos (4 a 7 meses), Cangrejitos (7 a 10 meses), Tiburoncitos 1 (10 a 13 meses), Tiburoncitos 2 (13 meses en adelante). Actividades lúdicas e intencionadas para fortalecer motricidad, cognición, lenguaje, área sensorial y socioemocional; favorece socialización temprana en entorno guiado, amoroso y seguro.';

DECLARE @DescTaller1Dia NVARCHAR(MAX) = @DescTallerBase + N' Modalidad: plan mensual con asistencia 1 día por semana.';

DECLARE @DescTaller2Dias NVARCHAR(MAX) = @DescTallerBase + N' Modalidad: plan mensual con asistencia 2 días por semana.';

DECLARE @DescTaller3Dias NVARCHAR(MAX) = @DescTallerBase + N' Modalidad: plan mensual con asistencia 3 días por semana.';

DECLARE @DescTallerClaseIndividual NVARCHAR(MAX) = @DescTallerBase + N' Modalidad: clase suelta (1 sesión), ideal para conocer el programa o complementar antes de elegir un plan de 1, 2 o 3 días por semana.';

DECLARE @DescClasePersonalizada NVARCHAR(MAX) = N'Estimulación temprana personalizada (sesión individual). Dirigida según la edad, etapa de desarrollo y necesidades del bebé. Incluye valoración general, actividades personalizadas, orientación a los padres para reforzar en casa, acompañamiento profesional uno a uno y sesiones de hidroterapia en tinas especiales (no incluidas en el taller grupal). Fortalece motricidad gruesa y fina, lenguaje, cognición, sensorial y socioemocional con ritmo adaptado al bebé. Modalidades portafolio 2026: clase suelta, paquetes de 4, 8 o 12 clases. Ideal cuando se requiere atención diferenciada o máximo acompañamiento familiar.';

DECLARE @DescDecoracionSencilla NVARCHAR(MAX) = N'Complemento de decoración sencilla para planes Baby Spa (cumplemes y celebraciones). Incluye ambientación festiva en el área de la tina: globos temáticos y número de la edad del bebé. Aplica sobre Plan Marineritos, Plan Aventuras Marinas o Plan Post Vacunas; el precio del plan base se cotiza aparte con este adicional.';

DECLARE @DescDecoracionBouquet NVARCHAR(MAX) = N'Complemento de decoración personalizada premium para planes Baby Spa. Bouquet floral personalizado con el nombre del bebé y número de la edad; detalle visual especial para cumplemes y fechas memorables. Compatible con los planes Baby Spa; se combina con el valor del plan elegido.';

-- ServiceId -> Description (catálogo dev Mimos)
DECLARE @Catalog TABLE (
    ServiceId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [Description] NVARCHAR(MAX) NOT NULL
);

INSERT INTO @Catalog (ServiceId, [Description]) VALUES
    ('AAAAAAAA-0001-0001-0001-AAAAAAAAAAAA', @DescPlanMarineritos),
    ('AAAAAAAA-0002-0002-0002-AAAAAAAAAAAA', @DescPlanAventurasMarinas),
    ('AAAAAAAA-0003-0003-0003-AAAAAAAAAAAA', @DescPlanPostVacunas),
    ('AAAAAAAA-0006-0006-0006-AAAAAAAAAAAA', @DescTaller1Dia),
    ('AAAAAAAA-0012-0012-0012-AAAAAAAAAAAA', @DescTaller2Dias),
    ('AAAAAAAA-0013-0013-0013-AAAAAAAAAAAA', @DescTaller3Dias),
    ('AAAAAAAA-0014-0014-0014-AAAAAAAAAAAA', @DescTallerClaseIndividual),
    ('AAAAAAAA-0007-0007-0007-AAAAAAAAAAAA', @DescClasePersonalizada),
    ('AAAAAAAA-0008-0008-0008-AAAAAAAAAAAA', @DescDecoracionSencilla),
    ('AAAAAAAA-0009-0009-0009-AAAAAAAAAAAA', @DescDecoracionBouquet);

UPDATE s
SET s.[Description] = c.[Description],
    s.UpdatedAt = SYSUTCDATETIME()
FROM dbo.Services s
INNER JOIN @Catalog c ON c.ServiceId = s.ServiceId
WHERE s.BusinessId = @BusinessId;

DECLARE @Updated INT = @@ROWCOUNT;
PRINT N'SeedMimiServiceCatalogDescriptions: ' + CAST(@Updated AS NVARCHAR(10)) + N' services updated for business ' + CAST(@BusinessId AS NVARCHAR(36));
GO

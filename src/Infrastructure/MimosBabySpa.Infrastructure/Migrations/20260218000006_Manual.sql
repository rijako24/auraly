-- ============================================================
-- Migración: 20260218000006_AddServiceTierAndSalesStrategy
-- Ejecutar en SSMS contra la base de datos de MimosBabySpa
-- ============================================================

PRINT '=== Iniciando migración 20260218000006 ===';
GO

-- ── 1. Columnas nuevas en Services ───────────────────────────

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[dbo].[Services]')
      AND name = N'GroupKey'
)
BEGIN
    ALTER TABLE [dbo].[Services]
    ADD [GroupKey] NVARCHAR(100) NULL;
    PRINT 'Columna GroupKey agregada.';
END
ELSE
    PRINT 'Columna GroupKey ya existe.';

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[dbo].[Services]')
      AND name = N'Tier'
)
BEGIN
    ALTER TABLE [dbo].[Services]
    ADD [Tier] INT NOT NULL DEFAULT 0;
    PRINT 'Columna Tier agregada.';
END
ELSE
    PRINT 'Columna Tier ya existe.';
GO

-- ── 2. Índice en (BusinessId, GroupKey) ──────────────────────

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_Services_BusinessId_GroupKey'
      AND object_id = OBJECT_ID(N'[dbo].[Services]')
)
BEGIN
    CREATE INDEX [IX_Services_BusinessId_GroupKey]
    ON [dbo].[Services] ([BusinessId], [GroupKey]);
    PRINT 'Índice IX_Services_BusinessId_GroupKey creado.';
END
ELSE
    PRINT 'Índice ya existe.';
GO

-- ── 3. Registro de la migración en __EFMigrationsHistory ─────

IF NOT EXISTS (
    SELECT 1 FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260218000006_AddServiceTierAndSalesStrategy'
)
BEGIN
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260218000006_AddServiceTierAndSalesStrategy', N'8.0.0');
    PRINT 'Migración registrada en __EFMigrationsHistory.';
END
ELSE
    PRINT 'Migración ya estaba registrada.';
GO

-- ── 4. Asignación de GroupKey y Tier ─────────────────────────

UPDATE [dbo].[Services] SET [GroupKey] = N'marineritos',  [Tier] = 0, [UpdatedAt] = GETUTCDATE()
WHERE [ServiceId] = 'AAAAAAAA-0001-0001-0001-AAAAAAAAAAAA';

UPDATE [dbo].[Services] SET [GroupKey] = N'aventuras',    [Tier] = 0, [UpdatedAt] = GETUTCDATE()
WHERE [ServiceId] = 'AAAAAAAA-0002-0002-0002-AAAAAAAAAAAA';

UPDATE [dbo].[Services] SET [GroupKey] = NULL,            [Tier] = 0, [UpdatedAt] = GETUTCDATE()
WHERE [ServiceId] = 'AAAAAAAA-0003-0003-0003-AAAAAAAAAAAA';

UPDATE [dbo].[Services] SET [GroupKey] = N'marineritos',  [Tier] = 1, [UpdatedAt] = GETUTCDATE()
WHERE [ServiceId] = 'AAAAAAAA-0004-0004-0004-AAAAAAAAAAAA';

UPDATE [dbo].[Services] SET [GroupKey] = N'aventuras',    [Tier] = 1, [UpdatedAt] = GETUTCDATE()
WHERE [ServiceId] = 'AAAAAAAA-0005-0005-0005-AAAAAAAAAAAA';

UPDATE [dbo].[Services] SET [GroupKey] = N'estimulacion', [Tier] = 0, [UpdatedAt] = GETUTCDATE()
WHERE [ServiceId] = 'AAAAAAAA-0006-0006-0006-AAAAAAAAAAAA';

UPDATE [dbo].[Services] SET [GroupKey] = N'estimulacion', [Tier] = 1, [UpdatedAt] = GETUTCDATE()
WHERE [ServiceId] = 'AAAAAAAA-0007-0007-0007-AAAAAAAAAAAA';

PRINT 'GroupKey y Tier asignados a todos los servicios.';
GO

-- ── 5. Descripciones optimizadas para venta ───────────────────

UPDATE [dbo].[Services]
SET [Description] = N'La experiencia más completa para celebrar el cumplemes. Incluye TODO el Plan Marineritos (Estimulación en Baby Gym + Hidroterapia en tinas especiales + Masaje infantil relajante) MÁS una decoración temática que convierte la sesión en una fiesta inolvidable. Opciones de decoración: Bouquet personalizado con número de la edad ($155.000) o Decoración sencilla con globos y número de la edad ($135.000). Por solo $35.000 más que el plan base, el bebé y los papás se llevan un recuerdo único e irrepetible.',
    [UpdatedAt] = GETUTCDATE()
WHERE [ServiceId] = 'AAAAAAAA-0004-0004-0004-AAAAAAAAAAAA';

UPDATE [dbo].[Services]
SET [Description] = N'La celebración de cumplemes con hidroterapia y masaje más decoración especial. Incluye TODO el Plan Aventuras Marinas (Hidroterapia relajante + Masaje suave) MÁS decoración festiva para hacer el momento único. Opciones: Bouquet personalizado con número de la edad ($135.000) o Decoración con globos y número de la edad ($115.000). Por solo $35.000 más que el plan base, conviertes una sesión de relajación en una celebración completa con fotos dignas de guardar.',
    [UpdatedAt] = GETUTCDATE()
WHERE [ServiceId] = 'AAAAAAAA-0005-0005-0005-AAAAAAAAAAAA';

UPDATE [dbo].[Services]
SET [Description] = N'La opción más completa para el desarrollo del bebé: sesión 100% personalizada según sus necesidades específicas. Incluye TODO lo del Taller Grupal PLUS atención exclusiva uno a uno con la terapeuta, ritmo adaptado al bebé y participación activa de los papás como co-terapeutas. Además incorpora estimulación acuática que el taller grupal no incluye. Precios: 1 clase $80.000 | Plan mensual 1 día/sem $270.000 | 2 días/sem $370.000 | 3 días/sem $450.000. Ideal para bebés que necesitan atención diferenciada o papás que quieren máximo impacto en cada sesión.',
    [UpdatedAt] = GETUTCDATE()
WHERE [ServiceId] = 'AAAAAAAA-0007-0007-0007-AAAAAAAAAAAA';

PRINT 'Descripciones premium actualizadas.';
GO

-- ── 6. SalesStrategy (BusinessConfiguration key=2) ───────────

IF NOT EXISTS (
    SELECT 1 FROM [dbo].[BusinessConfigurations]
    WHERE [BusinessId] = '22222222-2222-2222-2222-222222222222' AND [Key] = 2
)
BEGIN
    INSERT INTO [dbo].[BusinessConfigurations]
        ([BusinessConfigurationId], [BusinessId], [Key], [Value], [Description], [IsActive], [CreatedAt])
    VALUES
        (NEWID(),
         '22222222-2222-2222-2222-222222222222',
         2,
         N'Cuando el cliente pregunte por planes, servicios o la edad del bebé, aplica estas reglas:

1. SIEMPRE presenta primero la opción de mayor tier (marcada ⭐ en el catálogo). Esa es tu recomendación por defecto.
2. Menciona qué INCLUYE DE MÁS la opción premium respecto a la base — usa la descripción para construir argumentos concretos.
3. Enmarca la diferencia de precio como inversión: "Por solo $35.000 más obtienes decoración + fotos del recuerdo" (no digas "cuesta más").
4. Si el cliente pregunta por el plan base, informa su precio Y luego di: "¿Sabías que por X pesos más puedes agregarle decoración y hacer del cumplemes algo aún más especial?"
5. Para estimulación: la Clase Personalizada es el diferencial clave — enfatiza la atención individual y la estimulación acuática exclusiva.
6. Si el cliente menciona la edad del bebé, recomienda el plan adecuado para esa etapa Y su versión premium si existe.
7. Nunca presiones: termina con una pregunta abierta ("¿Te gustaría saber más sobre ese plan?" o "¿Reservamos una sesión?").
8. Prioridad de recomendación: Cumplemes Plan Marineritos > Cumplemes Plan Aventuras Marinas > Clase Personalizada > Plan Marineritos > Plan Aventuras > Plan Suaves Mimos > Taller Grupal.',
         N'Estrategia de recomendación y venta para el asistente virtual',
         1,
         GETUTCDATE());
    PRINT 'SalesStrategy insertada.';
END
ELSE
    PRINT 'SalesStrategy ya existía.';
GO

-- ── Verificación final ────────────────────────────────────────

PRINT '--- Servicios con GroupKey y Tier asignados: ---';
SELECT [ServiceName], [GroupKey], [Tier], [Price]
FROM [dbo].[Services]
WHERE [BusinessId] = '22222222-2222-2222-2222-222222222222'
ORDER BY [GroupKey], [Tier];

PRINT '✅ Migración 20260218000006 aplicada correctamente.';
GO

-- ============================================================
-- Migración: 20260218000008_SplitTallerGrupalByFrequency
-- Ejecutar en SSMS o sqlcmd contra la base talkioai
--
-- Split Taller Grupal de Estimulación Temprana en servicios por frecuencia:
--   - Taller Grupal - 1 día/semana  ($230.000) → Tier Base
--   - Taller Grupal - 2 días/semana ($280.000) → Tier Premium
--   - Taller Grupal - 3 días/semana ($330.000) → Tier Deluxe
--   - Taller Grupal - Clase Individual ($70.000) → standalone
--
-- Clase Personalizada pasa a standalone (sin GroupKey).
-- Descripciones incluyen grupos por edad del bebé (meses) — Opción A.
-- ============================================================

PRINT '=== Iniciando migración 20260218000008 ===';
GO

-- Descripción compartida para Taller Grupal con grupos por edad del bebé (Opción A)
-- Grupos por edad del bebé: 2-4 meses, 4-7 meses, etc.

-- ── 1. Actualizar Taller Grupal existente (0006) → 1 día/semana ─

UPDATE [dbo].[Services]
SET
    [ServiceName]     = N'Taller Grupal - 1 día/semana',
    [Description]     = N'Actividades lúdicas, sensoriales y físicas para desarrollo integral. Baby Gym, música, juegos sensoriales, cuentos. Grupos por edad del bebé: Estrellitas de Mar (2-4 meses), Pulpitos (4-7 meses), Cangrejitos (7-10 meses), Tiburoncitos 1 (10-13 meses), Tiburoncitos 2 (13+ meses). Al reservar, te asignaremos al grupo correcto según la edad del bebé.',
    [DurationMinutes] = 60,
    [Price]           = 230000,
    [GroupKey]        = N'taller_grupal',
    [Tier]            = 0,
    [UpdatedAt]       = GETUTCDATE()
WHERE [ServiceId] = 'AAAAAAAA-0006-0006-0006-AAAAAAAAAAAA';

PRINT 'Taller Grupal 1 día/semana actualizado.';
GO

-- ── 2. Clase Personalizada → standalone (quitar GroupKey) ────────

UPDATE [dbo].[Services]
SET
    [GroupKey]  = NULL,
    [Tier]      = 0,
    [UpdatedAt] = GETUTCDATE()
WHERE [ServiceId] = 'AAAAAAAA-0007-0007-0007-AAAAAAAAAAAA';

PRINT 'Clase Personalizada pasada a standalone.';
GO

-- ── 3. Insertar nuevos servicios: 2 días, 3 días, Clase Individual ─

IF NOT EXISTS (SELECT 1 FROM [dbo].[Services] WHERE [ServiceId] = 'AAAAAAAA-0012-0012-0012-AAAAAAAAAAAA')
BEGIN
    INSERT INTO [dbo].[Services]
        ([ServiceId], [BusinessId], [ServiceName], [Description], [DurationMinutes],
         [Price], [IsActive], [GroupKey], [Tier], [CreatedAt])
    VALUES
        ('AAAAAAAA-0012-0012-0012-AAAAAAAAAAAA', '22222222-2222-2222-2222-222222222222',
         N'Taller Grupal - 2 días/semana',
         N'Actividades lúdicas, sensoriales y físicas para desarrollo integral. Baby Gym, música, juegos sensoriales, cuentos. Grupos por edad del bebé: Estrellitas de Mar (2-4 meses), Pulpitos (4-7 meses), Cangrejitos (7-10 meses), Tiburoncitos 1 (10-13 meses), Tiburoncitos 2 (13+ meses). Al reservar, te asignaremos al grupo correcto según la edad del bebé.',
         60, 280000, 1, N'taller_grupal', 1, GETUTCDATE());
    PRINT 'Taller Grupal 2 días/semana insertado.';
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[Services] WHERE [ServiceId] = 'AAAAAAAA-0013-0013-0013-AAAAAAAAAAAA')
BEGIN
    INSERT INTO [dbo].[Services]
        ([ServiceId], [BusinessId], [ServiceName], [Description], [DurationMinutes],
         [Price], [IsActive], [GroupKey], [Tier], [CreatedAt])
    VALUES
        ('AAAAAAAA-0013-0013-0013-AAAAAAAAAAAA', '22222222-2222-2222-2222-222222222222',
         N'Taller Grupal - 3 días/semana',
         N'Actividades lúdicas, sensoriales y físicas para desarrollo integral. Baby Gym, música, juegos sensoriales, cuentos. Grupos por edad del bebé: Estrellitas de Mar (2-4 meses), Pulpitos (4-7 meses), Cangrejitos (7-10 meses), Tiburoncitos 1 (10-13 meses), Tiburoncitos 2 (13+ meses). Al reservar, te asignaremos al grupo correcto según la edad del bebé.',
         60, 330000, 1, N'taller_grupal', 2, GETUTCDATE());
    PRINT 'Taller Grupal 3 días/semana insertado.';
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[Services] WHERE [ServiceId] = 'AAAAAAAA-0014-0014-0014-AAAAAAAAAAAA')
BEGIN
    INSERT INTO [dbo].[Services]
        ([ServiceId], [BusinessId], [ServiceName], [Description], [DurationMinutes],
         [Price], [IsActive], [GroupKey], [Tier], [CreatedAt])
    VALUES
        ('AAAAAAAA-0014-0014-0014-AAAAAAAAAAAA', '22222222-2222-2222-2222-222222222222',
         N'Taller Grupal - Clase Individual',
         N'Actividades lúdicas, sensoriales y físicas para desarrollo integral. Baby Gym, música, juegos sensoriales, cuentos. Grupos por edad del bebé: Estrellitas de Mar (2-4 meses), Pulpitos (4-7 meses), Cangrejitos (7-10 meses), Tiburoncitos 1 (10-13 meses), Tiburoncitos 2 (13+ meses). Al reservar, te asignaremos al grupo correcto según la edad del bebé.',
         60, 70000, 1, NULL, 0, GETUTCDATE());
    PRINT 'Taller Grupal Clase Individual insertado.';
END
GO

-- ── 4. ServiceResourceUsages (Baby Gym para nuevos servicios) ─────

IF NOT EXISTS (
    SELECT 1 FROM [dbo].[ServiceResourceUsages]
    WHERE [ServiceId] = 'AAAAAAAA-0012-0012-0012-AAAAAAAAAAAA' AND [BusinessResourceId] = 'BBBBBBBB-0001-0001-0001-BBBBBBBBBBBB')
BEGIN
    INSERT INTO [dbo].[ServiceResourceUsages]
        ([ServiceResourceUsageId], [ServiceId], [BusinessResourceId], [Quantity])
    VALUES
        (NEWID(), 'AAAAAAAA-0012-0012-0012-AAAAAAAAAAAA', 'BBBBBBBB-0001-0001-0001-BBBBBBBBBBBB', 1);
    PRINT 'ServiceResourceUsage: Taller 2 días → Baby Gym.';
END

IF NOT EXISTS (
    SELECT 1 FROM [dbo].[ServiceResourceUsages]
    WHERE [ServiceId] = 'AAAAAAAA-0013-0013-0013-AAAAAAAAAAAA' AND [BusinessResourceId] = 'BBBBBBBB-0001-0001-0001-BBBBBBBBBBBB')
BEGIN
    INSERT INTO [dbo].[ServiceResourceUsages]
        ([ServiceResourceUsageId], [ServiceId], [BusinessResourceId], [Quantity])
    VALUES
        (NEWID(), 'AAAAAAAA-0013-0013-0013-AAAAAAAAAAAA', 'BBBBBBBB-0001-0001-0001-BBBBBBBBBBBB', 1);
    PRINT 'ServiceResourceUsage: Taller 3 días → Baby Gym.';
END

IF NOT EXISTS (
    SELECT 1 FROM [dbo].[ServiceResourceUsages]
    WHERE [ServiceId] = 'AAAAAAAA-0014-0014-0014-AAAAAAAAAAAA' AND [BusinessResourceId] = 'BBBBBBBB-0001-0001-0001-BBBBBBBBBBBB')
BEGIN
    INSERT INTO [dbo].[ServiceResourceUsages]
        ([ServiceResourceUsageId], [ServiceId], [BusinessResourceId], [Quantity])
    VALUES
        (NEWID(), 'AAAAAAAA-0014-0014-0014-AAAAAAAAAAAA', 'BBBBBBBB-0001-0001-0001-BBBBBBBBBBBB', 1);
    PRINT 'ServiceResourceUsage: Taller Individual → Baby Gym.';
END
GO

-- ── 5. EmployeeServices (Terapeuta Integral → todos los talleres) ─

IF NOT EXISTS (
    SELECT 1 FROM [dbo].[EmployeeServices]
    WHERE [EmployeeId] = 'CCCCCCCC-0001-0001-0001-CCCCCCCCCCCC' AND [ServiceId] = 'AAAAAAAA-0012-0012-0012-AAAAAAAAAAAA')
BEGIN
    INSERT INTO [dbo].[EmployeeServices]
        ([EmployeeServiceId], [EmployeeId], [ServiceId], [CreatedAt])
    VALUES
        (NEWID(), 'CCCCCCCC-0001-0001-0001-CCCCCCCCCCCC', 'AAAAAAAA-0012-0012-0012-AAAAAAAAAAAA', GETUTCDATE());
END

IF NOT EXISTS (
    SELECT 1 FROM [dbo].[EmployeeServices]
    WHERE [EmployeeId] = 'CCCCCCCC-0001-0001-0001-CCCCCCCCCCCC' AND [ServiceId] = 'AAAAAAAA-0013-0013-0013-AAAAAAAAAAAA')
BEGIN
    INSERT INTO [dbo].[EmployeeServices]
        ([EmployeeServiceId], [EmployeeId], [ServiceId], [CreatedAt])
    VALUES
        (NEWID(), 'CCCCCCCC-0001-0001-0001-CCCCCCCCCCCC', 'AAAAAAAA-0013-0013-0013-AAAAAAAAAAAA', GETUTCDATE());
END

IF NOT EXISTS (
    SELECT 1 FROM [dbo].[EmployeeServices]
    WHERE [EmployeeId] = 'CCCCCCCC-0001-0001-0001-CCCCCCCCCCCC' AND [ServiceId] = 'AAAAAAAA-0014-0014-0014-AAAAAAAAAAAA')
BEGIN
    INSERT INTO [dbo].[EmployeeServices]
        ([EmployeeServiceId], [EmployeeId], [ServiceId], [CreatedAt])
    VALUES
        (NEWID(), 'CCCCCCCC-0001-0001-0001-CCCCCCCCCCCC', 'AAAAAAAA-0014-0014-0014-AAAAAAAAAAAA', GETUTCDATE());
END

PRINT 'EmployeeServices para Terapeuta Integral actualizados.';
GO

-- ── 6. SalesStrategy actualizada ───────────────────────────────

UPDATE [dbo].[BusinessConfigurations]
SET [Value] = N'Cuando el cliente pregunte por planes, servicios o la edad del bebé, aplica estas reglas:

1. SIEMPRE presenta primero la opción de mayor tier (marcada ⭐ en el catálogo). Esa es tu recomendación por defecto.
2. El catálogo muestra la composición exacta de cada bundle (qué incluye). Úsala para argumentar. NUNCA inventes diferencias que no estén en la composición listada.
3. La diferencia entre variantes del mismo grupo es SOLO el componente extra (decoración, personalización, o frecuencia), NO las actividades base — esas son siempre iguales.
4. Enmarca la diferencia de precio como inversión: ''Por solo $X más obtienes [componente extra]''.
5. Para Taller Grupal: recomienda primero 3 días/semana (Deluxe), luego 2 días (Premium) o 1 día (Base). Más frecuencia = más impacto en el desarrollo del bebé.
6. Para estimulación: la Clase Personalizada agrega atención 1 a 1 y estimulación acuática — son diferencias reales, úsalas. Si preguntan por Taller Grupal, menciona también la Clase Individual ($70.000) como opción para probar una clase.
7. Si el cliente menciona la edad del bebé, indica el grupo correspondiente (Estrellitas de Mar, Pulpitos, Cangrejitos, Tiburoncitos 1 o 2) y recomienda el plan adecuado.
8. Nunca presiones: termina con una pregunta abierta.
9. Prioridad de recomendación por grupo: Cumplemes+Bouquet > Cumplemes+Sencilla > Plan base. Taller Grupal 3 días > 2 días > 1 día. Clase Personalizada y Taller Individual según necesidad.',
    [UpdatedAt] = GETUTCDATE()
WHERE [BusinessId] = '22222222-2222-2222-2222-222222222222' AND [Key] = 2;

PRINT 'SalesStrategy actualizada.';
GO

-- ── 7. Registro de migración ───────────────────────────────────

IF NOT EXISTS (
    SELECT 1 FROM [dbo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260218000008_SplitTallerGrupalByFrequency')
BEGIN
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260218000008_SplitTallerGrupalByFrequency', N'8.0.0');
    PRINT 'Migración registrada en __EFMigrationsHistory.';
END
GO

-- ── Verificación final ──────────────────────────────────────────

PRINT '--- Servicios Taller Grupal y Clase Personalizada: ---';
SELECT [ServiceName], [GroupKey], [Tier], [Price], [DurationMinutes]
FROM [dbo].[Services]
WHERE [BusinessId] = '22222222-2222-2222-2222-222222222222'
  AND ([ServiceId] LIKE 'AAAAAAAA-0006%' OR [ServiceId] LIKE 'AAAAAAAA-0007%'
       OR [ServiceId] LIKE 'AAAAAAAA-0012%' OR [ServiceId] LIKE 'AAAAAAAA-0013%' OR [ServiceId] LIKE 'AAAAAAAA-0014%')
ORDER BY [GroupKey], [Tier];

PRINT '✅ Migración 20260218000008 aplicada correctamente.';
GO

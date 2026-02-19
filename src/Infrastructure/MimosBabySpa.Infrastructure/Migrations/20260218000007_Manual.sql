-- ============================================================
-- Migración: 20260218000007_AddServiceBundleItems
-- Ejecutar en SSMS o sqlcmd contra la base talkioai
-- ============================================================

PRINT '=== Iniciando migración 20260218000007 ===';
GO

-- ── 1. Tabla ServiceBundleItems ──────────────────────────────

IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[ServiceBundleItems]') AND type = 'U')
BEGIN
    CREATE TABLE [dbo].[ServiceBundleItems] (
        [ServiceBundleItemId] UNIQUEIDENTIFIER NOT NULL,
        [BundleServiceId]     UNIQUEIDENTIFIER NOT NULL,
        [IncludedServiceId]   UNIQUEIDENTIFIER NOT NULL,
        [DisplayOrder]        INT NOT NULL DEFAULT 1,
        CONSTRAINT [PK_ServiceBundleItems] PRIMARY KEY ([ServiceBundleItemId]),
        CONSTRAINT [FK_ServiceBundleItems_BundleService]   FOREIGN KEY ([BundleServiceId])   REFERENCES [dbo].[Services]([ServiceId]) ON DELETE CASCADE,
        CONSTRAINT [FK_ServiceBundleItems_IncludedService] FOREIGN KEY ([IncludedServiceId]) REFERENCES [dbo].[Services]([ServiceId]) ON DELETE NO ACTION
    );

    CREATE UNIQUE INDEX [IX_ServiceBundleItems_BundleServiceId_IncludedServiceId]
        ON [dbo].[ServiceBundleItems] ([BundleServiceId], [IncludedServiceId]);

    PRINT 'Tabla ServiceBundleItems creada.';
END
ELSE
    PRINT 'Tabla ServiceBundleItems ya existe.';
GO

-- ── 2. Servicios de decoración ────────────────────────────────

IF NOT EXISTS (SELECT 1 FROM [dbo].[Services] WHERE [ServiceId] = 'AAAAAAAA-0008-0008-0008-AAAAAAAAAAAA')
BEGIN
    INSERT INTO [dbo].[Services]
        ([ServiceId], [BusinessId], [ServiceName], [Description], [DurationMinutes],
         [Price], [IsActive], [GroupKey], [Tier], [CreatedAt])
    VALUES
        ('AAAAAAAA-0008-0008-0008-AAAAAAAAAAAA', '22222222-2222-2222-2222-222222222222',
         N'Decoración Sencilla',
         N'Globos temáticos y número de la edad del bebé. Transforma el espacio de la sesión en un ambiente festivo.',
         0, 35000, 1, NULL, 0, GETUTCDATE());
    PRINT 'Decoración Sencilla insertada.';
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[Services] WHERE [ServiceId] = 'AAAAAAAA-0009-0009-0009-AAAAAAAAAAAA')
BEGIN
    INSERT INTO [dbo].[Services]
        ([ServiceId], [BusinessId], [ServiceName], [Description], [DurationMinutes],
         [Price], [IsActive], [GroupKey], [Tier], [CreatedAt])
    VALUES
        ('AAAAAAAA-0009-0009-0009-AAAAAAAAAAAA', '22222222-2222-2222-2222-222222222222',
         N'Decoración Bouquet Personalizado',
         N'Bouquet floral personalizado con el nombre del bebé y número de la edad. Detalles premium para una celebración inolvidable.',
         0, 55000, 1, NULL, 0, GETUTCDATE());
    PRINT 'Decoración Bouquet insertada.';
END
GO

-- ── 3. Variantes Bouquet de Cumplemes ─────────────────────────

IF NOT EXISTS (SELECT 1 FROM [dbo].[Services] WHERE [ServiceId] = 'AAAAAAAA-0010-0010-0010-AAAAAAAAAAAA')
BEGIN
    INSERT INTO [dbo].[Services]
        ([ServiceId], [BusinessId], [ServiceName], [Description], [DurationMinutes],
         [Price], [IsActive], [GroupKey], [Tier], [CreatedAt])
    VALUES
        ('AAAAAAAA-0010-0010-0010-AAAAAAAAAAAA', '22222222-2222-2222-2222-222222222222',
         N'Cumplemes – Plan Marineritos + Bouquet',
         N'Celebración de cumplemes que incluye el Plan Marineritos más Decoración Bouquet Personalizado.',
         60, 155000, 1, N'marineritos', 2, GETUTCDATE());
END

IF NOT EXISTS (SELECT 1 FROM [dbo].[Services] WHERE [ServiceId] = 'AAAAAAAA-0011-0011-0011-AAAAAAAAAAAA')
BEGIN
    INSERT INTO [dbo].[Services]
        ([ServiceId], [BusinessId], [ServiceName], [Description], [DurationMinutes],
         [Price], [IsActive], [GroupKey], [Tier], [CreatedAt])
    VALUES
        ('AAAAAAAA-0011-0011-0011-AAAAAAAAAAAA', '22222222-2222-2222-2222-222222222222',
         N'Cumplemes – Plan Aventuras Marinas + Bouquet',
         N'Celebración de cumplemes que incluye el Plan Aventuras Marinas más Decoración Bouquet Personalizado.',
         45, 135000, 1, N'aventuras', 2, GETUTCDATE());
END
GO

-- ── 4. ServiceBundleItems ─────────────────────────────────────

INSERT INTO [dbo].[ServiceBundleItems] ([ServiceBundleItemId], [BundleServiceId], [IncludedServiceId], [DisplayOrder])
SELECT NEWID(), 'AAAAAAAA-0004-0004-0004-AAAAAAAAAAAA', 'AAAAAAAA-0001-0001-0001-AAAAAAAAAAAA', 1
WHERE NOT EXISTS (SELECT 1 FROM [dbo].[ServiceBundleItems] WHERE [BundleServiceId] = 'AAAAAAAA-0004-0004-0004-AAAAAAAAAAAA' AND [IncludedServiceId] = 'AAAAAAAA-0001-0001-0001-AAAAAAAAAAAA');

INSERT INTO [dbo].[ServiceBundleItems] ([ServiceBundleItemId], [BundleServiceId], [IncludedServiceId], [DisplayOrder])
SELECT NEWID(), 'AAAAAAAA-0004-0004-0004-AAAAAAAAAAAA', 'AAAAAAAA-0008-0008-0008-AAAAAAAAAAAA', 2
WHERE NOT EXISTS (SELECT 1 FROM [dbo].[ServiceBundleItems] WHERE [BundleServiceId] = 'AAAAAAAA-0004-0004-0004-AAAAAAAAAAAA' AND [IncludedServiceId] = 'AAAAAAAA-0008-0008-0008-AAAAAAAAAAAA');

INSERT INTO [dbo].[ServiceBundleItems] ([ServiceBundleItemId], [BundleServiceId], [IncludedServiceId], [DisplayOrder])
SELECT NEWID(), 'AAAAAAAA-0010-0010-0010-AAAAAAAAAAAA', 'AAAAAAAA-0001-0001-0001-AAAAAAAAAAAA', 1
WHERE NOT EXISTS (SELECT 1 FROM [dbo].[ServiceBundleItems] WHERE [BundleServiceId] = 'AAAAAAAA-0010-0010-0010-AAAAAAAAAAAA' AND [IncludedServiceId] = 'AAAAAAAA-0001-0001-0001-AAAAAAAAAAAA');

INSERT INTO [dbo].[ServiceBundleItems] ([ServiceBundleItemId], [BundleServiceId], [IncludedServiceId], [DisplayOrder])
SELECT NEWID(), 'AAAAAAAA-0010-0010-0010-AAAAAAAAAAAA', 'AAAAAAAA-0009-0009-0009-AAAAAAAAAAAA', 2
WHERE NOT EXISTS (SELECT 1 FROM [dbo].[ServiceBundleItems] WHERE [BundleServiceId] = 'AAAAAAAA-0010-0010-0010-AAAAAAAAAAAA' AND [IncludedServiceId] = 'AAAAAAAA-0009-0009-0009-AAAAAAAAAAAA');

INSERT INTO [dbo].[ServiceBundleItems] ([ServiceBundleItemId], [BundleServiceId], [IncludedServiceId], [DisplayOrder])
SELECT NEWID(), 'AAAAAAAA-0005-0005-0005-AAAAAAAAAAAA', 'AAAAAAAA-0002-0002-0002-AAAAAAAAAAAA', 1
WHERE NOT EXISTS (SELECT 1 FROM [dbo].[ServiceBundleItems] WHERE [BundleServiceId] = 'AAAAAAAA-0005-0005-0005-AAAAAAAAAAAA' AND [IncludedServiceId] = 'AAAAAAAA-0002-0002-0002-AAAAAAAAAAAA');

INSERT INTO [dbo].[ServiceBundleItems] ([ServiceBundleItemId], [BundleServiceId], [IncludedServiceId], [DisplayOrder])
SELECT NEWID(), 'AAAAAAAA-0005-0005-0005-AAAAAAAAAAAA', 'AAAAAAAA-0008-0008-0008-AAAAAAAAAAAA', 2
WHERE NOT EXISTS (SELECT 1 FROM [dbo].[ServiceBundleItems] WHERE [BundleServiceId] = 'AAAAAAAA-0005-0005-0005-AAAAAAAAAAAA' AND [IncludedServiceId] = 'AAAAAAAA-0008-0008-0008-AAAAAAAAAAAA');

INSERT INTO [dbo].[ServiceBundleItems] ([ServiceBundleItemId], [BundleServiceId], [IncludedServiceId], [DisplayOrder])
SELECT NEWID(), 'AAAAAAAA-0011-0011-0011-AAAAAAAAAAAA', 'AAAAAAAA-0002-0002-0002-AAAAAAAAAAAA', 1
WHERE NOT EXISTS (SELECT 1 FROM [dbo].[ServiceBundleItems] WHERE [BundleServiceId] = 'AAAAAAAA-0011-0011-0011-AAAAAAAAAAAA' AND [IncludedServiceId] = 'AAAAAAAA-0002-0002-0002-AAAAAAAAAAAA');

INSERT INTO [dbo].[ServiceBundleItems] ([ServiceBundleItemId], [BundleServiceId], [IncludedServiceId], [DisplayOrder])
SELECT NEWID(), 'AAAAAAAA-0011-0011-0011-AAAAAAAAAAAA', 'AAAAAAAA-0009-0009-0009-AAAAAAAAAAAA', 2
WHERE NOT EXISTS (SELECT 1 FROM [dbo].[ServiceBundleItems] WHERE [BundleServiceId] = 'AAAAAAAA-0011-0011-0011-AAAAAAAAAAAA' AND [IncludedServiceId] = 'AAAAAAAA-0009-0009-0009-AAAAAAAAAAAA');

PRINT 'ServiceBundleItems insertados.';
GO

-- ── 5. Descripciones limpias ──────────────────────────────────

UPDATE [dbo].[Services]
SET [Description] = N'Experiencia de 3 estaciones: Estimulación temprana en Baby Gym (desarrollo motor, cognitivo y social), Hidroterapia en tinas especiales adaptadas para bebés, y Masaje infantil relajante que mejora la circulación y fortalece el vínculo padres-bebé.', [UpdatedAt] = GETUTCDATE()
WHERE [ServiceId] = 'AAAAAAAA-0001-0001-0001-AAAAAAAAAAAA';

UPDATE [dbo].[Services]
SET [Description] = N'Experiencia de 2 estaciones: Hidroterapia en tinas especiales (sesión relajante con flotación y movimiento en el agua) y Masaje infantil suave para relajar y consentir al bebé.', [UpdatedAt] = GETUTCDATE()
WHERE [ServiceId] = 'AAAAAAAA-0002-0002-0002-AAAAAAAAAAAA';

UPDATE [dbo].[Services]
SET [Description] = N'Celebración de cumplemes que incluye el Plan Marineritos más Decoración Sencilla.', [UpdatedAt] = GETUTCDATE()
WHERE [ServiceId] = 'AAAAAAAA-0004-0004-0004-AAAAAAAAAAAA';

UPDATE [dbo].[Services]
SET [Description] = N'Celebración de cumplemes que incluye el Plan Aventuras Marinas más Decoración Sencilla.', [UpdatedAt] = GETUTCDATE()
WHERE [ServiceId] = 'AAAAAAAA-0005-0005-0005-AAAAAAAAAAAA';

PRINT 'Descripciones actualizadas.';
GO

-- ── 6. SalesStrategy actualizada ──────────────────────────────

UPDATE [dbo].[BusinessConfigurations]
SET [Value] = N'Cuando el cliente pregunte por planes, servicios o la edad del bebé, aplica estas reglas:

1. SIEMPRE presenta primero la opción de mayor tier (marcada ⭐ en el catálogo). Esa es tu recomendación por defecto.
2. El catálogo muestra la composición exacta de cada bundle (qué incluye). Úsala para argumentar. NUNCA inventes diferencias que no estén en la composición listada.
3. La diferencia entre variantes del mismo grupo es SOLO el componente extra (decoración, personalización), NO las actividades base — esas son siempre iguales.
4. Enmarca la diferencia de precio como inversión: ''Por solo $X más obtienes [componente extra]''.
5. Para estimulación: la Clase Personalizada agrega atención 1 a 1 y estimulación acuática — son diferencias reales, úsalas.
6. Si el cliente menciona la edad del bebé, recomienda el plan adecuado y su versión de mayor tier.
7. Nunca presiones: termina con una pregunta abierta.
8. Prioridad de recomendación por grupo: Cumplemes+Bouquet > Cumplemes+Sencilla > Plan base.',
    [UpdatedAt] = GETUTCDATE()
WHERE [BusinessId] = '22222222-2222-2222-2222-222222222222' AND [Key] = 2;

PRINT 'SalesStrategy actualizada.';
GO

-- ── 7. Registro de migración ──────────────────────────────────

IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'20260218000007_AddServiceBundleItems')
BEGIN
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260218000007_AddServiceBundleItems', N'8.0.0');
    PRINT 'Migración registrada.';
END
GO

PRINT '✅ Migración 20260218000007 aplicada correctamente.';
GO

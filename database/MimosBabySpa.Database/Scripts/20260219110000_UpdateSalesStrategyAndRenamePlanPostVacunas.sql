-- ============================================================
-- Migración: 20260219110000_UpdateSalesStrategyAndRenamePlanPostVacunas
-- Actualiza SalesStrategy con reglas de edad y renombra plan a "Plan Post Vacunas"
-- Ejecutar en SSMS o: sqlcmd -S server -d MimosBabySpa -i "ruta\a\este\script.sql"
-- ============================================================

DECLARE @BusinessId UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';
DECLARE @SvcPostVacunas UNIQUEIDENTIFIER = 'AAAAAAAA-0003-0003-0003-AAAAAAAAAAAA';

-- 1. Actualizar SalesStrategy (key=2)
UPDATE [dbo].[BusinessConfigurations]
SET [Value] = N'Cuando el cliente pregunte por planes, servicios o la edad del bebé, aplica estas reglas:

1. SIEMPRE presenta primero la opción de mayor tier (marcada ⭐ en el catálogo). Sin excepciones.
2. Menciona qué INCLUYE DE MÁS respecto a opciones base — usa la descripción para argumentos concretos.
3. Enmarca la diferencia de precio como inversión, nunca como gasto.
4. Reglas por edad del bebé:
   - 0-2 meses: Plan Aventuras Marinas. Ofrecer Plan Post Vacunas solo si mencionan vacunación.
   - 3-6 meses: Plan Marineritos (incluye estimulación + hidro + masaje). Alternativa: Aventuras Marinas.
   - 6+ meses: Plan Marineritos (la experiencia más completa para esta etapa).
   Incluso con estas reglas, siempre presenta primero la opción de mayor tier disponible para la edad.
5. Para estimulación: Clase Personalizada es el diferencial — enfatiza atención individual y estimulación acuática.
6. Prioridad de recomendación: Cumplemes Marineritos > Cumplemes Aventuras > Clase Personalizada > Plan Marineritos > Plan Aventuras > Plan Post Vacunas > Taller Grupal.',
    [UpdatedAt] = GETUTCDATE()
WHERE [BusinessId] = @BusinessId AND [Key] = 2;

-- 2. Renombrar servicio
UPDATE [dbo].[Services]
SET [ServiceName] = N'Plan Post Vacunas',
    [UpdatedAt] = GETUTCDATE()
WHERE [ServiceId] = @SvcPostVacunas;

-- 3. Registrar en historial de migraciones (para que EF considere aplicada)
IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'20260219110000_UpdateSalesStrategyAndRenamePlanPostVacunas')
INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260219110000_UpdateSalesStrategyAndRenamePlanPostVacunas', N'8.0.0');

PRINT 'Migración 20260219110000 aplicada: SalesStrategy actualizada, Plan Post Vacunas renombrado.';
GO

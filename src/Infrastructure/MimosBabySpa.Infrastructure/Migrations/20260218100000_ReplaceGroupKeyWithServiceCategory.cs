using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <summary>
    /// Reemplaza GroupKey por ServiceCategory.
    /// - Category: Plan=0, Taller=1, Clase=2, Otro=99
    /// - Tier mantiene orden de recomendación dentro de categoría.
    /// - ServiceAddOnRule.CompatibleServiceCategory: add-ons solo para ciertas categorías.
    /// Multitenant: datos de Mimos como ejemplo; otros negocios migran con defaults.
    /// </summary>
    [Migration("20260218100000_ReplaceGroupKeyWithServiceCategory")]
    public class ReplaceGroupKeyWithServiceCategory : Migration
    {
        private const string BusinessId = "22222222-2222-2222-2222-222222222222";
        private const string SvcDecoSencilla = "AAAAAAAA-0008-0008-0008-AAAAAAAAAAAA";
        private const string SvcDecoBouquet = "AAAAAAAA-0009-0009-0009-AAAAAAAAAAAA";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            AddServiceCategoryColumn(migrationBuilder);
            AddServiceTypeColumn(migrationBuilder);
            EnsureServiceAddOnRulesTable(migrationBuilder);
            AddCompatibleServiceCategoryToRules(migrationBuilder);
            MigrateDataFromGroupKey(migrationBuilder);
            SetAddOnServiceType(migrationBuilder);
            SeedAddOnRulesForMimos(migrationBuilder);
            DropGroupKeyColumn(migrationBuilder);
        }

        private static void AddServiceCategoryColumn(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Services') AND name = 'Category')
                BEGIN
                    ALTER TABLE [dbo].[Services] ADD [Category] INT NOT NULL DEFAULT 99;
                    PRINT 'Columna Category agregada.';
                END
            ");
        }

        private static void AddServiceTypeColumn(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Services') AND name = 'ServiceType')
                BEGIN
                    ALTER TABLE [dbo].[Services] ADD [ServiceType] INT NOT NULL DEFAULT 0;
                    PRINT 'Columna ServiceType agregada.';
                END
            ");
        }

        private static void EnsureServiceAddOnRulesTable(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[ServiceAddOnRules]') AND type = 'U')
                BEGIN
                    CREATE TABLE [dbo].[ServiceAddOnRules] (
                        [ServiceAddOnRuleId]       UNIQUEIDENTIFIER NOT NULL,
                        [BusinessId]               UNIQUEIDENTIFIER NOT NULL,
                        [AddOnServiceId]           UNIQUEIDENTIFIER NOT NULL,
                        [CompatibleServiceId]     UNIQUEIDENTIFIER NULL,
                        [CompatibleServiceCategory] INT NULL,
                        [DisplayOrder]             INT NOT NULL DEFAULT 1,
                        CONSTRAINT [PK_ServiceAddOnRules] PRIMARY KEY ([ServiceAddOnRuleId]),
                        CONSTRAINT [FK_ServiceAddOnRules_Business] FOREIGN KEY ([BusinessId]) REFERENCES [dbo].[Businesses]([BusinessId]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_ServiceAddOnRules_AddOnService] FOREIGN KEY ([AddOnServiceId]) REFERENCES [dbo].[Services]([ServiceId]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_ServiceAddOnRules_CompatibleService] FOREIGN KEY ([CompatibleServiceId]) REFERENCES [dbo].[Services]([ServiceId]) ON DELETE NO ACTION
                    );
                    CREATE INDEX [IX_ServiceAddOnRules_BusinessId] ON [dbo].[ServiceAddOnRules]([BusinessId]);
                    CREATE UNIQUE INDEX [IX_ServiceAddOnRules_BusinessId_AddOnServiceId_CompatibleServiceId] ON [dbo].[ServiceAddOnRules]([BusinessId], [AddOnServiceId], [CompatibleServiceId]);
                    PRINT 'Tabla ServiceAddOnRules creada.';
                END
            ");
        }

        private static void AddCompatibleServiceCategoryToRules(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID('dbo.ServiceAddOnRules') AND type = 'U')
                   AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ServiceAddOnRules') AND name = 'CompatibleServiceCategory')
                BEGIN
                    ALTER TABLE [dbo].[ServiceAddOnRules] ADD [CompatibleServiceCategory] INT NULL;
                    PRINT 'Columna CompatibleServiceCategory agregada.';
                END
            ");
        }

        private static void SetAddOnServiceType(MigrationBuilder migrationBuilder)
        {
            // ServiceType: Standard=0, AddOn=1
            migrationBuilder.Sql($@"
                UPDATE [dbo].[Services] SET [ServiceType] = 1, [UpdatedAt] = GETUTCDATE()
                WHERE [ServiceId] IN ('{SvcDecoSencilla}', '{SvcDecoBouquet}')
                  AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Services') AND name = 'ServiceType');
            ");
        }

        private static void SeedAddOnRulesForMimos(MigrationBuilder migrationBuilder)
        {
            // Plan = 0. Insertar reglas: Decoración Sencilla y Bouquet compatibles con categoría Plan.
            migrationBuilder.Sql($@"
                IF EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID('dbo.ServiceAddOnRules') AND type = 'U')
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM [dbo].[ServiceAddOnRules] WHERE [BusinessId] = '{BusinessId}' AND [AddOnServiceId] = '{SvcDecoSencilla}' AND [CompatibleServiceId] IS NULL)
                    INSERT INTO [dbo].[ServiceAddOnRules] ([ServiceAddOnRuleId], [BusinessId], [AddOnServiceId], [CompatibleServiceId], [CompatibleServiceCategory], [DisplayOrder])
                    VALUES (NEWID(), '{BusinessId}', '{SvcDecoSencilla}', NULL, 0, 1);

                    IF NOT EXISTS (SELECT 1 FROM [dbo].[ServiceAddOnRules] WHERE [BusinessId] = '{BusinessId}' AND [AddOnServiceId] = '{SvcDecoBouquet}' AND [CompatibleServiceId] IS NULL)
                    INSERT INTO [dbo].[ServiceAddOnRules] ([ServiceAddOnRuleId], [BusinessId], [AddOnServiceId], [CompatibleServiceId], [CompatibleServiceCategory], [DisplayOrder])
                    VALUES (NEWID(), '{BusinessId}', '{SvcDecoBouquet}', NULL, 0, 2);
                END
            ");
        }

        private static void MigrateDataFromGroupKey(MigrationBuilder migrationBuilder)
        {
            // Category: Plan=0, Taller=1, Clase=2, Otro=99
            // Tier: Deluxe=2, Premium=1, Base=0

            // Mimos: Plan Marineritos=Deluxe, Aventuras=Base, Suaves Mimos=Base
            // Cumplemes (si existen) = Plan, Base
            // Taller Grupal 1/2/3 días, Clase Individual = Taller
            // Clase Personalizada = Clase

            migrationBuilder.Sql($@"
                -- Plan Marineritos: Deluxe (recomendar primero)
                UPDATE [dbo].[Services] SET [Category] = 0, [Tier] = 2, [UpdatedAt] = GETUTCDATE()
                WHERE [BusinessId] = '{BusinessId}' AND [ServiceName] = N'Plan Marineritos';

                -- Plan Aventuras: Base
                UPDATE [dbo].[Services] SET [Category] = 0, [Tier] = 0, [UpdatedAt] = GETUTCDATE()
                WHERE [BusinessId] = '{BusinessId}' AND [ServiceName] = N'Plan Aventuras Marinas';

                -- Plan Suaves Mimos: Base
                UPDATE [dbo].[Services] SET [Category] = 0, [Tier] = 0, [UpdatedAt] = GETUTCDATE()
                WHERE [BusinessId] = '{BusinessId}' AND [ServiceName] = N'Plan Suaves Mimos – Post Vacunas';

                -- Cumplemes (si existen): Plan, Base
                UPDATE [dbo].[Services] SET [Category] = 0, [Tier] = 0, [UpdatedAt] = GETUTCDATE()
                WHERE [BusinessId] = '{BusinessId}' AND [ServiceName] LIKE N'Cumplemes%';

                -- Taller Grupal - todos los de frecuencia: Taller
                UPDATE [dbo].[Services] SET [Category] = 1, [UpdatedAt] = GETUTCDATE()
                WHERE [BusinessId] = '{BusinessId}' AND ([ServiceName] LIKE N'Taller Grupal%' OR [ServiceName] = N'Taller Grupal de Estimulación Temprana');

                -- Taller: 3 días=Deluxe, 2 días=Premium, 1 día=Base, Individual=Base
                UPDATE [dbo].[Services] SET [Tier] = 2 WHERE [BusinessId] = '{BusinessId}' AND [ServiceName] = N'Taller Grupal - 3 días/semana';
                UPDATE [dbo].[Services] SET [Tier] = 1 WHERE [BusinessId] = '{BusinessId}' AND [ServiceName] = N'Taller Grupal - 2 días/semana';
                UPDATE [dbo].[Services] SET [Tier] = 0 WHERE [BusinessId] = '{BusinessId}' AND [ServiceName] = N'Taller Grupal - 1 día/semana';
                UPDATE [dbo].[Services] SET [Tier] = 0 WHERE [BusinessId] = '{BusinessId}' AND [ServiceName] = N'Taller Grupal - Clase Individual';

                -- Clase Personalizada: Clase
                UPDATE [dbo].[Services] SET [Category] = 2, [Tier] = 0, [UpdatedAt] = GETUTCDATE()
                WHERE [BusinessId] = '{BusinessId}' AND [ServiceName] = N'Clase Personalizada de Estimulación Temprana';

                -- Servicios no mapeados conservan Category=99 (Otro) por defecto de la columna.
            ");
        }

        private static void DropGroupKeyColumn(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Services_BusinessId_GroupKey' AND object_id = OBJECT_ID('dbo.Services'))
                BEGIN
                    DROP INDEX [IX_Services_BusinessId_GroupKey] ON [dbo].[Services];
                    PRINT 'Índice IX_Services_BusinessId_GroupKey eliminado.';
                END

                IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Services') AND name = 'GroupKey')
                BEGIN
                    ALTER TABLE [dbo].[Services] DROP COLUMN [GroupKey];
                    PRINT 'Columna GroupKey eliminada.';
                END

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Services_BusinessId_Category' AND object_id = OBJECT_ID('dbo.Services'))
                BEGIN
                    CREATE INDEX [IX_Services_BusinessId_Category] ON [dbo].[Services] ([BusinessId], [Category]);
                    PRINT 'Índice IX_Services_BusinessId_Category creado.';
                END
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Services') AND name = 'GroupKey')
                BEGIN
                    ALTER TABLE [dbo].[Services] ADD [GroupKey] NVARCHAR(100) NULL;
                    CREATE INDEX [IX_Services_BusinessId_GroupKey] ON [dbo].[Services] ([BusinessId], [GroupKey]);
                END

                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Services_BusinessId_Category' AND object_id = OBJECT_ID('dbo.Services'))
                    DROP INDEX [IX_Services_BusinessId_Category] ON [dbo].[Services];

                IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Services') AND name = 'Category')
                    ALTER TABLE [dbo].[Services] DROP COLUMN [Category];

                IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ServiceAddOnRules') AND name = 'CompatibleServiceCategory')
                    ALTER TABLE [dbo].[ServiceAddOnRules] DROP COLUMN [CompatibleServiceCategory];
            ");
        }
    }
}

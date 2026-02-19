using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <summary>
    /// Introduce la composición estructural de servicios bundle.
    ///
    /// Cambios en esquema:
    ///   - Tabla ServiceBundleItems: define qué servicios componen un bundle.
    ///     Un bundle es un servicio reservable que agrupa otros servicios en su precio.
    ///
    /// Cambios en datos (Mimos Baby Spa):
    ///   - Agrega servicios de decoración como entidades propias con precio (sin texto en Description)
    ///   - Agrega variantes Bouquet de Cumplemes para Marineritos y Aventuras
    ///   - Crea los ServiceBundleItems que enlazan cada cumplemes con su base + decoración
    ///   - Limpia las descripciones de planes base y cumplemes (sin precios en prosa)
    ///   - Actualiza la SalesStrategy para reflejar los nuevos servicios
    ///
    /// Relación con migración anterior:
    ///   - GroupKey/Tier se mantienen igual para agrupación y recomendación
    ///   - ServiceBundleItems complementa: responde "¿de qué está hecho?" sin inferencia
    /// </summary>
    public partial class AddServiceBundleItems : Migration
    {
        private const string BusinessId = "22222222-2222-2222-2222-222222222222";

        // ── Service IDs existentes ────────────────────────────────────────────────
        private const string SvcMarineritos        = "AAAAAAAA-0001-0001-0001-AAAAAAAAAAAA";
        private const string SvcAventurasMarinas   = "AAAAAAAA-0002-0002-0002-AAAAAAAAAAAA";
        private const string SvcCumpleMes1Sencilla = "AAAAAAAA-0004-0004-0004-AAAAAAAAAAAA"; // Mari + Deco Sencilla (ya existía)
        private const string SvcCumpleMes2Sencilla = "AAAAAAAA-0005-0005-0005-AAAAAAAAAAAA"; // Avent + Deco Sencilla (ya existía)

        // ── Service IDs nuevos ────────────────────────────────────────────────────
        private const string SvcDecoSencilla   = "AAAAAAAA-0008-0008-0008-AAAAAAAAAAAA";
        private const string SvcDecoBouquet    = "AAAAAAAA-0009-0009-0009-AAAAAAAAAAAA";
        private const string SvcCumpleMes1Bouquet = "AAAAAAAA-0010-0010-0010-AAAAAAAAAAAA"; // Mari + Bouquet
        private const string SvcCumpleMes2Bouquet = "AAAAAAAA-0011-0011-0011-AAAAAAAAAAAA"; // Avent + Bouquet

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            CreateServiceBundleItemsTable(migrationBuilder);
            InsertDecorationServices(migrationBuilder);
            InsertBouquetCumplemeServices(migrationBuilder);
            InsertServiceBundleItems(migrationBuilder);
            CleanServiceDescriptions(migrationBuilder);
            UpdateTiersForNewServices(migrationBuilder);
            UpdateSalesStrategy(migrationBuilder);
            RegisterMigration(migrationBuilder);
        }

        // ── Tabla ServiceBundleItems ──────────────────────────────────────────────

        private static void CreateServiceBundleItemsTable(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
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
            ");
        }

        // ── Servicios de decoración (componentes reutilizables) ───────────────────
        //
        // Son servicios independientes (sin GroupKey): no se reservan solos,
        // su rol es ser componentes de bundles y tener precio propio en datos.

        private static void InsertDecorationServices(MigrationBuilder migrationBuilder)
        {
            var decorations = new[]
            {
                (SvcDecoSencilla, "Decoración Sencilla",
                 "Globos temáticos y número de la edad del bebé. Transforma el espacio de la sesión en un ambiente festivo.",
                 0, 35000m),

                (SvcDecoBouquet, "Decoración Bouquet Personalizado",
                 "Bouquet floral personalizado con el nombre del bebé y número de la edad. Detalles premium para una celebración inolvidable.",
                 0, 55000m),
            };

            foreach (var (id, name, desc, duration, price) in decorations)
            {
                migrationBuilder.Sql($@"
                    IF NOT EXISTS (SELECT 1 FROM [dbo].[Services] WHERE [ServiceId] = '{id}')
                    BEGIN
                        INSERT INTO [dbo].[Services]
                            ([ServiceId], [BusinessId], [ServiceName], [Description], [DurationMinutes],
                             [Price], [IsActive], [GroupKey], [Tier], [CreatedAt])
                        VALUES
                            ('{id}', '{BusinessId}', N'{name}', N'{desc.Replace("'", "''")}',
                             {duration}, {price}, 1, NULL, 0, GETUTCDATE());
                        PRINT 'Servicio de decoración insertado: {name}';
                    END
                ");
            }
        }

        // ── Variantes Bouquet (Deluxe=2) ──────────────────────────────────────────

        private static void InsertBouquetCumplemeServices(MigrationBuilder migrationBuilder)
        {
            var bouquetServices = new[]
            {
                (SvcCumpleMes1Bouquet, "Cumplemes – Plan Marineritos + Bouquet",
                 "Celebración de cumplemes que incluye el Plan Marineritos más Decoración Bouquet Personalizado.",
                 60, 155000m, "marineritos", 2),

                (SvcCumpleMes2Bouquet, "Cumplemes – Plan Aventuras Marinas + Bouquet",
                 "Celebración de cumplemes que incluye el Plan Aventuras Marinas más Decoración Bouquet Personalizado.",
                 45, 135000m, "aventuras", 2),
            };

            foreach (var (id, name, desc, duration, price, groupKey, tier) in bouquetServices)
            {
                migrationBuilder.Sql($@"
                    IF NOT EXISTS (SELECT 1 FROM [dbo].[Services] WHERE [ServiceId] = '{id}')
                    BEGIN
                        INSERT INTO [dbo].[Services]
                            ([ServiceId], [BusinessId], [ServiceName], [Description], [DurationMinutes],
                             [Price], [IsActive], [GroupKey], [Tier], [CreatedAt])
                        VALUES
                            ('{id}', '{BusinessId}', N'{name}', N'{desc}',
                             {duration}, {price}, 1, N'{groupKey}', {tier}, GETUTCDATE());
                        PRINT 'Servicio Bouquet insertado: {name}';
                    END
                ");
            }
        }

        // ── ServiceBundleItems: composición de cada cumplemes ─────────────────────
        //
        // Estructura: cada cumplemes = plan base (order=1) + decoración (order=2)
        // Los planes base y las decoraciones NO tienen bundle items (son componentes).

        private static void InsertServiceBundleItems(MigrationBuilder migrationBuilder)
        {
            var items = new[]
            {
                // Cumplemes Marineritos + Deco Sencilla
                (SvcCumpleMes1Sencilla, SvcMarineritos,      1),  // base
                (SvcCumpleMes1Sencilla, SvcDecoSencilla,     2),  // decoración

                // Cumplemes Marineritos + Bouquet
                (SvcCumpleMes1Bouquet,  SvcMarineritos,      1),  // base
                (SvcCumpleMes1Bouquet,  SvcDecoBouquet,      2),  // decoración

                // Cumplemes Aventuras Marinas + Deco Sencilla
                (SvcCumpleMes2Sencilla, SvcAventurasMarinas, 1),  // base
                (SvcCumpleMes2Sencilla, SvcDecoSencilla,     2),  // decoración

                // Cumplemes Aventuras Marinas + Bouquet
                (SvcCumpleMes2Bouquet,  SvcAventurasMarinas, 1),  // base
                (SvcCumpleMes2Bouquet,  SvcDecoBouquet,      2),  // decoración
            };

            foreach (var (bundleId, includedId, order) in items)
            {
                migrationBuilder.Sql($@"
                    IF NOT EXISTS (
                        SELECT 1 FROM [dbo].[ServiceBundleItems]
                        WHERE [BundleServiceId] = '{bundleId}' AND [IncludedServiceId] = '{includedId}'
                    )
                    BEGIN
                        INSERT INTO [dbo].[ServiceBundleItems]
                            ([ServiceBundleItemId], [BundleServiceId], [IncludedServiceId], [DisplayOrder])
                        VALUES
                            (NEWID(), '{bundleId}', '{includedId}', {order});
                    END
                ");
            }

            migrationBuilder.Sql("PRINT 'ServiceBundleItems insertados.';");
        }

        // ── Limpieza de descripciones (sin precios en prosa) ─────────────────────

        private static void CleanServiceDescriptions(MigrationBuilder migrationBuilder)
        {
            // Plan Marineritos — describe sus 3 estaciones con claridad
            migrationBuilder.Sql($@"
                UPDATE [dbo].[Services]
                SET [Description] = N'Experiencia de 3 estaciones: Estimulación temprana en Baby Gym (desarrollo motor, cognitivo y social), Hidroterapia en tinas especiales adaptadas para bebés, y Masaje infantil relajante que mejora la circulación y fortalece el vínculo padres-bebé.',
                    [UpdatedAt] = GETUTCDATE()
                WHERE [ServiceId] = '{SvcMarineritos}';
            ");

            // Plan Aventuras Marinas — describe sus 2 estaciones con claridad
            migrationBuilder.Sql($@"
                UPDATE [dbo].[Services]
                SET [Description] = N'Experiencia de 2 estaciones: Hidroterapia en tinas especiales (sesión relajante con flotación y movimiento en el agua) y Masaje infantil suave para relajar y consentir al bebé.',
                    [UpdatedAt] = GETUTCDATE()
                WHERE [ServiceId] = '{SvcAventurasMarinas}';
            ");

            // Cumplemes Marineritos + Deco Sencilla — solo describe su versión, composición la muestra el builder
            migrationBuilder.Sql($@"
                UPDATE [dbo].[Services]
                SET [Description] = N'Celebración de cumplemes que incluye el Plan Marineritos más Decoración Sencilla.',
                    [UpdatedAt] = GETUTCDATE()
                WHERE [ServiceId] = '{SvcCumpleMes1Sencilla}';
            ");

            // Cumplemes Aventuras + Deco Sencilla
            migrationBuilder.Sql($@"
                UPDATE [dbo].[Services]
                SET [Description] = N'Celebración de cumplemes que incluye el Plan Aventuras Marinas más Decoración Sencilla.',
                    [UpdatedAt] = GETUTCDATE()
                WHERE [ServiceId] = '{SvcCumpleMes2Sencilla}';
            ");

            migrationBuilder.Sql("PRINT 'Descripciones limpias actualizadas.';");
        }

        // ── Actualizar Tier del Cumplemes Sencilla a Premium (1) ─────────────────
        // Los bouquet son Deluxe (2), los sencilla pasan de Premium (1) a ser la opción media.
        // Orden recomendación dentro de cada grupo: Deluxe=2 > Premium=1 > Base=0

        private static void UpdateTiersForNewServices(MigrationBuilder migrationBuilder)
        {
            // Los cumplemes sencilla ya tienen Tier=1 (Premium) — confirmamos que sigue correcto
            // Los bouquet recién insertados tienen Tier=2 (Deluxe)
            // Ajuste: renombrar tier de cumplemes sencilla a 1 (ya está en BD, no cambia)
            migrationBuilder.Sql("PRINT 'Tiers validados: Bouquet=Deluxe(2), Sencilla=Premium(1), Planes base=Base(0).';");
        }

        // ── SalesStrategy actualizada ─────────────────────────────────────────────

        private static void UpdateSalesStrategy(MigrationBuilder migrationBuilder)
        {
            var strategy = @"Cuando el cliente pregunte por planes, servicios o la edad del bebé, aplica estas reglas:

1. SIEMPRE presenta primero la opción de mayor tier (marcada ⭐ en el catálogo). Esa es tu recomendación por defecto.
2. El catálogo muestra la composición exacta de cada bundle (qué incluye). Úsala para argumentar. NUNCA inventes diferencias que no estén en la composición listada.
3. La diferencia entre variantes del mismo grupo es SOLO el componente extra (decoración, personalización), NO las actividades base — esas son siempre iguales.
4. Enmarca la diferencia de precio como inversión: ''Por solo $X más obtienes [componente extra]''.
5. Para estimulación: la Clase Personalizada agrega atención 1 a 1 y estimulación acuática — son diferencias reales, úsalas.
6. Si el cliente menciona la edad del bebé, recomienda el plan adecuado y su versión de mayor tier.
7. Nunca presiones: termina con una pregunta abierta.
8. Prioridad de recomendación por grupo: Cumplemes+Bouquet > Cumplemes+Sencilla > Plan base."
                .Replace("'", "''");

            migrationBuilder.Sql($@"
                UPDATE [dbo].[BusinessConfigurations]
                SET [Value] = N'{strategy}', [UpdatedAt] = GETUTCDATE()
                WHERE [BusinessId] = '{BusinessId}' AND [Key] = 2;
            ");

            migrationBuilder.Sql("PRINT 'SalesStrategy actualizada.';");
        }

        private static void RegisterMigration(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1 FROM [dbo].[__EFMigrationsHistory]
                    WHERE [MigrationId] = N'20260218000007_AddServiceBundleItems'
                )
                BEGIN
                    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
                    VALUES (N'20260218000007_AddServiceBundleItems', N'8.0.0');
                    PRINT 'Migración registrada.';
                END
            ");
        }

        // ── Down ──────────────────────────────────────────────────────────────────

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Eliminar bundle items
            migrationBuilder.Sql(@"DELETE FROM [dbo].[ServiceBundleItems];");

            // Eliminar servicios nuevos (Bouquet y decoraciones)
            migrationBuilder.Sql($@"
                DELETE FROM [dbo].[Services]
                WHERE [ServiceId] IN (
                    '{SvcDecoSencilla}', '{SvcDecoBouquet}',
                    '{SvcCumpleMes1Bouquet}', '{SvcCumpleMes2Bouquet}'
                );
            ");

            // Eliminar tabla
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[ServiceBundleItems]') AND type = 'U')
                    DROP TABLE [dbo].[ServiceBundleItems];
            ");

            migrationBuilder.Sql($@"
                DELETE FROM [dbo].[__EFMigrationsHistory]
                WHERE [MigrationId] = N'20260218000007_AddServiceBundleItems';
            ");
        }
    }
}

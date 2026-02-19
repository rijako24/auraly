using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <summary>
    /// - Elimina servicios: 0005 (Cumplemes Aventuras+Sencilla), 0010 (Marineritos+Bouquet), 0011 (Aventuras+Bouquet).
    /// - Categoriza y asigna ServiceType a todos los servicios.
    /// - Elimina CompatibleServiceCategory de ServiceAddOnRules (la compatibilidad viene de Service.Category del add-on).
    /// </summary>
    [Migration("20260218100002_CleanupServicesAndDropCompatibleServiceCategory")]
    public class CleanupServicesAndDropCompatibleServiceCategory : Migration
    {
        private const string BusinessId = "22222222-2222-2222-2222-222222222222";
        private const string PlanMarineritos = "AAAAAAAA-0001-0001-0001-AAAAAAAAAAAA";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            DeleteServices(migrationBuilder);
            CategorizeAndSetServiceTypes(migrationBuilder);
            DropCompatibleServiceCategory(migrationBuilder);
        }

        private static void DeleteServices(MigrationBuilder migrationBuilder)
        {
            var toDelete = new[] { "AAAAAAAA-0005-0005-0005-AAAAAAAAAAAA", "AAAAAAAA-0010-0010-0010-AAAAAAAAAAAA", "AAAAAAAA-0011-0011-0011-AAAAAAAAAAAA" };
            foreach (var id in toDelete)
            {
                migrationBuilder.Sql($@"
                    UPDATE [dbo].[Reservations] SET [ServiceId] = '{PlanMarineritos}', [UpdatedAt] = GETUTCDATE() WHERE [ServiceId] = '{id}';
                    DELETE FROM [dbo].[EmployeeServices] WHERE [ServiceId] = '{id}';
                    DELETE FROM [dbo].[ServiceBundleItems] WHERE [BundleServiceId] = '{id}';
                    DELETE FROM [dbo].[ServiceResourceUsages] WHERE [ServiceId] = '{id}';
                    DELETE FROM [dbo].[Services] WHERE [ServiceId] = '{id}';
                ");
            }
        }

        private static void CategorizeAndSetServiceTypes(MigrationBuilder migrationBuilder)
        {
            // Category: Plan=0, Taller=1, Clase=2, Otro=99
            // ServiceType: Standard=0, AddOn=1

            migrationBuilder.Sql($@"
                -- Planes
                UPDATE [dbo].[Services] SET [Category] = 0, [ServiceType] = 0, [UpdatedAt] = GETUTCDATE()
                WHERE [BusinessId] = '{BusinessId}' AND [ServiceName] IN (N'Plan Marineritos', N'Plan Aventuras Marinas', N'Plan Suaves Mimos – Post Vacunas');

                -- Talleres
                UPDATE [dbo].[Services] SET [Category] = 1, [ServiceType] = 0, [UpdatedAt] = GETUTCDATE()
                WHERE [BusinessId] = '{BusinessId}' AND ([ServiceName] LIKE N'Taller Grupal%' OR [ServiceName] = N'Taller Grupal de Estimulación Temprana');

                -- Clase
                UPDATE [dbo].[Services] SET [Category] = 2, [ServiceType] = 0, [UpdatedAt] = GETUTCDATE()
                WHERE [BusinessId] = '{BusinessId}' AND [ServiceName] = N'Clase Personalizada de Estimulación Temprana';

                -- Add-ons (Decoración): Category=Plan indica compatibilidad con Planes
                UPDATE [dbo].[Services] SET [Category] = 0, [ServiceType] = 1, [UpdatedAt] = GETUTCDATE()
                WHERE [BusinessId] = '{BusinessId}' AND [ServiceName] IN (N'Decoración Sencilla', N'Decoración Bouquet Personalizado');
            ");
        }

        private static void DropCompatibleServiceCategory(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.ServiceAddOnRules') AND name = 'CompatibleServiceCategory')
                BEGIN
                    ALTER TABLE [dbo].[ServiceAddOnRules] DROP COLUMN [CompatibleServiceCategory];
                END
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No reversible: datos eliminados y columna obsoleta.
        }
    }
}

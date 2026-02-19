using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <summary>
    /// Elimina el servicio "Cumplemes – Plan Marineritos + Sencilla".
    /// Reservas existentes se migran a Plan Marineritos.
    /// </summary>
    [Migration("20260218100001_RemoveCumplemesPlanMarineritosSencilla")]
    public class RemoveCumplemesPlanMarineritosSencilla : Migration
    {
        private const string ServiceToRemove = "AAAAAAAA-0004-0004-0004-AAAAAAAAAAAA";
        private const string FallbackService = "AAAAAAAA-0001-0001-0001-AAAAAAAAAAAA"; // Plan Marineritos

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Reservas: migrar a Plan Marineritos
            migrationBuilder.Sql($@"
                UPDATE [dbo].[Reservations]
                SET [ServiceId] = '{FallbackService}', [UpdatedAt] = GETUTCDATE()
                WHERE [ServiceId] = '{ServiceToRemove}';
            ");

            // 2. EmployeeServices
            migrationBuilder.Sql($@"
                DELETE FROM [dbo].[EmployeeServices]
                WHERE [ServiceId] = '{ServiceToRemove}';
            ");

            // 3. ServiceBundleItems (BundleServiceId)
            migrationBuilder.Sql($@"
                DELETE FROM [dbo].[ServiceBundleItems]
                WHERE [BundleServiceId] = '{ServiceToRemove}';
            ");

            // 4. ServiceResourceUsages
            migrationBuilder.Sql($@"
                DELETE FROM [dbo].[ServiceResourceUsages]
                WHERE [ServiceId] = '{ServiceToRemove}';
            ");

            // 5. Servicio
            migrationBuilder.Sql($@"
                DELETE FROM [dbo].[Services]
                WHERE [ServiceId] = '{ServiceToRemove}';
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No se revierte: el servicio fue eliminado por decisión de negocio.
            // Restaurar requeriría datos de seed completos desde 0007/0006.
        }
    }
}

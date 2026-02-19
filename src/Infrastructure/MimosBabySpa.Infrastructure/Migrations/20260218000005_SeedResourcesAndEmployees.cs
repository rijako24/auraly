using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <summary>
    /// Pobla los recursos físicos del negocio (BusinessResources) y define qué recursos
    /// consume cada servicio (ServiceResourceUsages). Además crea dos empleadas reales:
    ///   - Terapeuta Integral: habilitada para todos los servicios del catálogo.
    ///   - Terapeuta de Planes: habilitada únicamente para los planes (sin talleres ni clases).
    /// </summary>
    public partial class SeedResourcesAndEmployees : Migration
    {
        private const string BusinessId = "22222222-2222-2222-2222-222222222222";

        // ── BusinessResource IDs ──────────────────────────────────────────────────
        private const string BabyGymId        = "BBBBBBBB-0001-0001-0001-BBBBBBBBBBBB";
        private const string TinasId          = "BBBBBBBB-0002-0002-0002-BBBBBBBBBBBB";
        private const string MasajeadoresId   = "BBBBBBBB-0003-0003-0003-BBBBBBBBBBBB";

        // ── Service IDs (ya sembrados en SeedMimosBusinessData) ──────────────────
        private const string SvcMarineritos        = "AAAAAAAA-0001-0001-0001-AAAAAAAAAAAA";
        private const string SvcAventurasMarinas   = "AAAAAAAA-0002-0002-0002-AAAAAAAAAAAA";
        private const string SvcSuavesMimos        = "AAAAAAAA-0003-0003-0003-AAAAAAAAAAAA";
        private const string SvcCumpleMes1         = "AAAAAAAA-0004-0004-0004-AAAAAAAAAAAA";
        private const string SvcCumpleMes2         = "AAAAAAAA-0005-0005-0005-AAAAAAAAAAAA";
        private const string SvcTallerGrupal       = "AAAAAAAA-0006-0006-0006-AAAAAAAAAAAA";
        private const string SvcClasePersonalizada = "AAAAAAAA-0007-0007-0007-AAAAAAAAAAAA";

        // ── Employee IDs ──────────────────────────────────────────────────────────
        private const string EmpIntegralId = "CCCCCCCC-0001-0001-0001-CCCCCCCCCCCC";
        private const string EmpPlanesId   = "CCCCCCCC-0002-0002-0002-CCCCCCCCCCCC";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            SeedBusinessResources(migrationBuilder);
            SeedServiceResourceUsages(migrationBuilder);
            SeedEmployees(migrationBuilder);
            SeedEmployeeServices(migrationBuilder);
        }

        // ── BusinessResources ────────────────────────────────────────────────────

        private static void SeedBusinessResources(MigrationBuilder migrationBuilder)
        {
            var resources = new[]
            {
                (BabyGymId,      "Baby Gym",      1),
                (TinasId,        "Tinas",         2),
                (MasajeadoresId, "Masajeadores",  2),
            };

            foreach (var (id, name, qty) in resources)
            {
                migrationBuilder.Sql($@"
                    IF NOT EXISTS (
                        SELECT 1 FROM [dbo].[BusinessResources]
                        WHERE [BusinessId] = '{BusinessId}' AND [ResourceName] = N'{name}'
                    )
                    BEGIN
                        INSERT INTO [dbo].[BusinessResources]
                            ([BusinessResourceId], [BusinessId], [ResourceName], [Quantity], [CreatedAt])
                        VALUES
                            ('{id}', '{BusinessId}', N'{name}', {qty}, GETUTCDATE());
                    END
                ");
            }
        }

        // ── ServiceResourceUsages ─────────────────────────────────────────────────
        //
        // Plan Marineritos            → Baby Gym (1) + Tina (1) + Masajeador (1)
        // Cumplemes Plan Marineritos  → Baby Gym (1) + Tina (1) + Masajeador (1)
        // Plan Aventuras Marinas      → Tina (1) + Masajeador (1)
        // Cumplemes Plan Aventuras    → Tina (1) + Masajeador (1)
        // Plan Suaves Mimos           → Tina (1) + Masajeador (1)
        // Taller Grupal               → Baby Gym (1)
        // Clase Personalizada         → Baby Gym (1)

        private static void SeedServiceResourceUsages(MigrationBuilder migrationBuilder)
        {
            var usages = new[]
            {
                // Plan Marineritos
                (SvcMarineritos, BabyGymId,      1),
                (SvcMarineritos, TinasId,         1),
                (SvcMarineritos, MasajeadoresId,  1),

                // Cumplemes – Plan Marineritos + Decoración
                (SvcCumpleMes1,  BabyGymId,      1),
                (SvcCumpleMes1,  TinasId,         1),
                (SvcCumpleMes1,  MasajeadoresId,  1),

                // Plan Aventuras Marinas
                (SvcAventurasMarinas, TinasId,        1),
                (SvcAventurasMarinas, MasajeadoresId, 1),

                // Cumplemes – Plan Aventuras Marinas + Decoración
                (SvcCumpleMes2, TinasId,        1),
                (SvcCumpleMes2, MasajeadoresId, 1),

                // Plan Suaves Mimos – Post Vacunas
                (SvcSuavesMimos, TinasId,        1),
                (SvcSuavesMimos, MasajeadoresId, 1),

                // Taller Grupal de Estimulación Temprana
                (SvcTallerGrupal, BabyGymId, 1),

                // Clase Personalizada de Estimulación Temprana
                (SvcClasePersonalizada, BabyGymId, 1),
            };

            foreach (var (serviceId, resourceId, qty) in usages)
            {
                migrationBuilder.Sql($@"
                    IF NOT EXISTS (
                        SELECT 1 FROM [dbo].[ServiceResourceUsages]
                        WHERE [ServiceId] = '{serviceId}' AND [BusinessResourceId] = '{resourceId}'
                    )
                    BEGIN
                        INSERT INTO [dbo].[ServiceResourceUsages]
                            ([ServiceResourceUsageId], [ServiceId], [BusinessResourceId], [Quantity])
                        VALUES
                            (NEWID(), '{serviceId}', '{resourceId}', {qty});
                    END
                ");
            }
        }

        // ── Employees ────────────────────────────────────────────────────────────

        private static void SeedEmployees(MigrationBuilder migrationBuilder)
        {
            var employees = new[]
            {
                (EmpIntegralId, "Terapeuta Integral"),
                (EmpPlanesId,   "Terapeuta de Planes"),
            };

            foreach (var (id, name) in employees)
            {
                migrationBuilder.Sql($@"
                    IF NOT EXISTS (
                        SELECT 1 FROM [dbo].[Employees]
                        WHERE [BusinessId] = '{BusinessId}' AND [Name] = N'{name}'
                    )
                    BEGIN
                        INSERT INTO [dbo].[Employees]
                            ([EmployeeId], [BusinessId], [Name], [IsActive], [CreatedAt])
                        VALUES
                            ('{id}', '{BusinessId}', N'{name}', 1, GETUTCDATE());
                    END
                ");
            }
        }

        // ── EmployeeServices ─────────────────────────────────────────────────────
        //
        // Terapeuta Integral → todos los servicios (7)
        // Terapeuta de Planes → solo planes (sin Taller Grupal ni Clase Personalizada)

        private static void SeedEmployeeServices(MigrationBuilder migrationBuilder)
        {
            var allServices = new[]
            {
                SvcMarineritos,
                SvcAventurasMarinas,
                SvcSuavesMimos,
                SvcCumpleMes1,
                SvcCumpleMes2,
                SvcTallerGrupal,
                SvcClasePersonalizada,
            };

            var planServices = new[]
            {
                SvcMarineritos,
                SvcAventurasMarinas,
                SvcSuavesMimos,
                SvcCumpleMes1,
                SvcCumpleMes2,
            };

            InsertEmployeeServices(migrationBuilder, EmpIntegralId, allServices);
            InsertEmployeeServices(migrationBuilder, EmpPlanesId, planServices);
        }

        private static void InsertEmployeeServices(
            MigrationBuilder migrationBuilder,
            string employeeId,
            string[] serviceIds)
        {
            foreach (var serviceId in serviceIds)
            {
                migrationBuilder.Sql($@"
                    IF NOT EXISTS (
                        SELECT 1 FROM [dbo].[EmployeeServices]
                        WHERE [EmployeeId] = '{employeeId}' AND [ServiceId] = '{serviceId}'
                    )
                    BEGIN
                        INSERT INTO [dbo].[EmployeeServices]
                            ([EmployeeServiceId], [EmployeeId], [ServiceId], [CreatedAt])
                        VALUES
                            (NEWID(), '{employeeId}', '{serviceId}', GETUTCDATE());
                    END
                ");
            }
        }

        // ── Down ─────────────────────────────────────────────────────────────────

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Eliminar EmployeeServices de los empleados creados en esta migración
            migrationBuilder.Sql($@"
                DELETE FROM [dbo].[EmployeeServices]
                WHERE [EmployeeId] IN ('{EmpIntegralId}', '{EmpPlanesId}');
            ");

            // Eliminar los empleados creados
            migrationBuilder.Sql($@"
                DELETE FROM [dbo].[Employees]
                WHERE [EmployeeId] IN ('{EmpIntegralId}', '{EmpPlanesId}');
            ");

            // Eliminar ServiceResourceUsages asociados a los recursos de esta migración
            migrationBuilder.Sql($@"
                DELETE FROM [dbo].[ServiceResourceUsages]
                WHERE [BusinessResourceId] IN ('{BabyGymId}', '{TinasId}', '{MasajeadoresId}');
            ");

            // Eliminar los recursos del negocio
            migrationBuilder.Sql($@"
                DELETE FROM [dbo].[BusinessResources]
                WHERE [BusinessResourceId] IN ('{BabyGymId}', '{TinasId}', '{MasajeadoresId}');
            ");
        }
    }
}

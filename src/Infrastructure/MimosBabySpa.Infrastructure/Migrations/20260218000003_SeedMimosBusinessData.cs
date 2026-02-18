using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <summary>
    /// Poblar datos de negocio para Mimos Baby Spa - Valledupar.
    /// Migra la información que antes vivía en BusinessConfiguration (GeneralInformation)
    /// a la estructura normalizada: Business (details) + Services (catálogo).
    /// </summary>
    public partial class SeedMimosBusinessData : Migration
    {
        private const string BusinessId = "22222222-2222-2222-2222-222222222222";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            UpdateBusinessDetails(migrationBuilder);
            InsertServices(migrationBuilder);
        }

        private static void UpdateBusinessDetails(MigrationBuilder migrationBuilder)
        {
            var operatingHoursJson = @"{
  ""monday"":    [{""Open"":""08:00"",""Close"":""12:00""},{""Open"":""14:00"",""Close"":""18:00""}],
  ""tuesday"":   [{""Open"":""08:00"",""Close"":""12:00""},{""Open"":""14:00"",""Close"":""18:00""}],
  ""wednesday"": [{""Open"":""08:00"",""Close"":""12:00""},{""Open"":""14:00"",""Close"":""18:00""}],
  ""thursday"":  [{""Open"":""08:00"",""Close"":""12:00""},{""Open"":""14:00"",""Close"":""18:00""}],
  ""friday"":    [{""Open"":""08:00"",""Close"":""12:00""},{""Open"":""14:00"",""Close"":""18:00""}],
  ""saturday"":  [{""Open"":""08:00"",""Close"":""13:00""}],
  ""sunday"":    []
}".Replace("'", "''");

            var paymentMethodsJson = @"[
  {""Name"":""Efectivo"",""Icon"":""💵""},
  {""Name"":""Nequi"",""Icon"":""📱""},
  {""Name"":""Daviplata"",""Icon"":""📱""},
  {""Name"":""Transferencia bancaria"",""Icon"":""🏦""}
]".Replace("'", "''");

            migrationBuilder.Sql($@"
                UPDATE [dbo].[Businesses]
                SET
                    [Description]        = N'Centro especializado en el bienestar y desarrollo integral de bebés. Hidroterapia en tinas especiales, masaje infantil, estimulación temprana, cumplemes y talleres grupales.',
                    [Address]            = N'Cra 13 #9C-19, Barrio San Joaquín, Valledupar – Cesar, Colombia',
                    [Phone]              = N'+573194823017',
                    [OperatingHoursJson] = N'{operatingHoursJson}',
                    [PaymentMethodsJson] = N'{paymentMethodsJson}',
                    [UpdatedAt]          = GETUTCDATE()
                WHERE [BusinessId] = '{BusinessId}';
            ");
        }

        private static void InsertServices(MigrationBuilder migrationBuilder)
        {
            var now = "GETUTCDATE()";

            var services = new[]
            {
                new ServiceSeed(
                    "AAAAAAAA-0001-0001-0001-AAAAAAAAAAAA",
                    "Plan Marineritos",
                    "Experiencia completa de 3 estaciones: Estimulación temprana en Baby Gym (desarrollo motor, cognitivo y social), Hidroterapia en tinas especiales adaptadas para bebés, y Masaje infantil relajante que mejora la circulación y fortalece el vínculo padres-bebé.",
                    60, 0),

                new ServiceSeed(
                    "AAAAAAAA-0002-0002-0002-AAAAAAAAAAAA",
                    "Plan Aventuras Marinas",
                    "Incluye 2 estaciones: Hidroterapia en tinas especiales (sesión relajante con flotación y movimiento en el agua) y Masaje infantil suave para relajar y consentir al bebé.",
                    45, 0),

                new ServiceSeed(
                    "AAAAAAAA-0003-0003-0003-AAAAAAAAAAAA",
                    "Plan Suaves Mimos – Post Vacunas",
                    "Diseñado para después de la vacunación. Hidroterapia en tinas con agua tibia para relajar músculos y calmar molestias, más Masaje infantil suave (sin tocar zona de punción). Reduce molestias e inflamación, mejora el estado de ánimo y promueve sueño reparador.",
                    45, 0),

                new ServiceSeed(
                    "AAAAAAAA-0004-0004-0004-AAAAAAAAAAAA",
                    "Cumplemes – Plan Marineritos + Decoración",
                    "Celebración de cumplemes con Plan Marineritos completo (Estimulación + Hidroterapia + Masaje) más decoración. Opciones: Bouquet personalizado + número de la edad ($155.000) o Decoración sencilla con globos + número de la edad ($135.000).",
                    60, 135000),

                new ServiceSeed(
                    "AAAAAAAA-0005-0005-0005-AAAAAAAAAAAA",
                    "Cumplemes – Plan Aventuras Marinas + Decoración",
                    "Celebración de cumplemes con Plan Aventuras Marinas (Hidroterapia + Masaje) más decoración. Opciones: Bouquet personalizado + número de la edad ($135.000) o Decoración sencilla con globos + número de la edad ($115.000).",
                    45, 115000),

                new ServiceSeed(
                    "AAAAAAAA-0006-0006-0006-AAAAAAAAAAAA",
                    "Taller Grupal de Estimulación Temprana",
                    "Actividades lúdicas, sensoriales y físicas para desarrollo integral. Baby Gym, música, juegos sensoriales, cuentos. Grupos por edad: Estrellitas de Mar (2-4m), Pulpitos (4-7m), Cangrejitos (7-10m), Tiburoncitos 1 (10-13m), Tiburoncitos 2 (13m+). Precios: Clase individual $70.000 | Plan mensual 1 día/sem $230.000 | 2 días/sem $280.000 | 3 días/sem $330.000.",
                    60, 70000),

                new ServiceSeed(
                    "AAAAAAAA-0007-0007-0007-AAAAAAAAAAAA",
                    "Clase Personalizada de Estimulación Temprana",
                    "Sesión individual adaptada a las necesidades del bebé. Desarrollo cognitivo, motor, emocional y social con participación activa de los padres. Incluye estimulación acuática. Precios: 1 clase $80.000 | Plan mensual 1 día/sem $270.000 | 2 días/sem $370.000 | 3 días/sem $450.000.",
                    60, 80000),
            };

            foreach (var svc in services)
            {
                migrationBuilder.Sql($@"
                    IF NOT EXISTS (SELECT 1 FROM [dbo].[Services] WHERE [BusinessId] = '{BusinessId}' AND [ServiceName] = N'{svc.Name.Replace("'", "''")}')
                    BEGIN
                        INSERT INTO [dbo].[Services] ([ServiceId], [BusinessId], [ServiceName], [Description], [DurationMinutes], [Price], [IsActive], [CreatedAt])
                        VALUES ('{svc.Id}', '{BusinessId}', N'{svc.Name.Replace("'", "''")}', N'{svc.Description.Replace("'", "''")}', {svc.DurationMinutes}, {svc.Price}, 1, {now});
                    END
                ");
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
                DELETE FROM [dbo].[Services] WHERE [BusinessId] = '{BusinessId}';

                UPDATE [dbo].[Businesses]
                SET [Description] = '', [Address] = '', [Phone] = '',
                    [OperatingHoursJson] = '{{}}', [PaymentMethodsJson] = '[]',
                    [UpdatedAt] = GETUTCDATE()
                WHERE [BusinessId] = '{BusinessId}';
            ");
        }

        private record ServiceSeed(string Id, string Name, string Description, int DurationMinutes, decimal Price);
    }
}

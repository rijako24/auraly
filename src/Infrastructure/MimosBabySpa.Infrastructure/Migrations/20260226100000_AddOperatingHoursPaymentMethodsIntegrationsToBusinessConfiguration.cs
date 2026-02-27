using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <summary>
    /// Inserta BusinessConfiguration para las nuevas claves:
    /// Key=4 (OperatingHours), Key=5 (PaymentMethods), Key=6 (Integrations).
    /// </summary>
    [Migration("20260226100000_AddOperatingHoursPaymentMethodsIntegrationsToBusinessConfiguration")]
    public partial class AddOperatingHoursPaymentMethodsIntegrationsToBusinessConfiguration : Migration
    {
        private const string OperatingHoursValue = @"{
  ""monday"":    [{""open"":""08:00"",""close"":""12:00""},{""open"":""14:00"",""close"":""18:00""}],
  ""tuesday"":   [{""open"":""08:00"",""close"":""12:00""},{""open"":""14:00"",""close"":""18:00""}],
  ""wednesday"": [{""open"":""08:00"",""close"":""12:00""},{""open"":""14:00"",""close"":""18:00""}],
  ""thursday"":  [{""open"":""08:00"",""close"":""12:00""},{""open"":""14:00"",""close"":""18:00""}],
  ""friday"":    [{""open"":""08:00"",""close"":""12:00""},{""open"":""14:00"",""close"":""18:00""}],
  ""saturday"":  [{""open"":""08:00"",""close"":""13:00""}],
  ""sunday"":    []
}";

        private const string PaymentMethodsValue = @"[
  {""name"":""Efectivo"",""icon"":""💵""},
  {""name"":""Nequi"",""icon"":""📱""},
  {""name"":""Daviplata"",""icon"":""📱""},
  {""name"":""Transferencia bancaria"",""icon"":""🏦""}
]";

        private const string IntegrationsValue = @"{""GoogleCalendar"":{""Enabled"":false,""CalendarId"":""primary""}}";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var oh = OperatingHoursValue.Replace("'", "''");
            var pm = PaymentMethodsValue.Replace("'", "''");
            var ig = IntegrationsValue.Replace("'", "''");

            // Key=4 (OperatingHours)
            migrationBuilder.Sql($@"
                MERGE [dbo].[BusinessConfigurations] AS target
                USING (SELECT [BusinessId] FROM [dbo].[Businesses]) AS source
                ON target.[BusinessId] = source.[BusinessId] AND target.[Key] = 4
                WHEN NOT MATCHED BY TARGET THEN
                    INSERT (BusinessConfigurationId, BusinessId, [Key], [Value], [Description], IsActive, CreatedAt)
                    VALUES (NEWID(), source.[BusinessId], 4, N'{oh}',
                        N'Horarios de operación por día de la semana', 1, GETUTCDATE());
                WHEN MATCHED THEN
                    UPDATE SET [UpdatedAt] = GETUTCDATE();
            ");

            // Key=5 (PaymentMethods)
            migrationBuilder.Sql($@"
                MERGE [dbo].[BusinessConfigurations] AS target
                USING (SELECT [BusinessId] FROM [dbo].[Businesses]) AS source
                ON target.[BusinessId] = source.[BusinessId] AND target.[Key] = 5
                WHEN NOT MATCHED BY TARGET THEN
                    INSERT (BusinessConfigurationId, BusinessId, [Key], [Value], [Description], IsActive, CreatedAt)
                    VALUES (NEWID(), source.[BusinessId], 5, N'{pm}',
                        N'Métodos de pago aceptados', 1, GETUTCDATE());
                WHEN MATCHED THEN
                    UPDATE SET [UpdatedAt] = GETUTCDATE();
            ");

            // Key=6 (Integrations)
            migrationBuilder.Sql($@"
                MERGE [dbo].[BusinessConfigurations] AS target
                USING (SELECT [BusinessId] FROM [dbo].[Businesses]) AS source
                ON target.[BusinessId] = source.[BusinessId] AND target.[Key] = 6
                WHEN NOT MATCHED BY TARGET THEN
                    INSERT (BusinessConfigurationId, BusinessId, [Key], [Value], [Description], IsActive, CreatedAt)
                    VALUES (NEWID(), source.[BusinessId], 6, N'{ig}',
                        N'Integraciones externas (Google Calendar, etc.)', 1, GETUTCDATE());
                WHEN MATCHED THEN
                    UPDATE SET [UpdatedAt] = GETUTCDATE();
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM [dbo].[BusinessConfigurations]
                WHERE [Key] IN (4, 5, 6);
            ");
        }
    }
}

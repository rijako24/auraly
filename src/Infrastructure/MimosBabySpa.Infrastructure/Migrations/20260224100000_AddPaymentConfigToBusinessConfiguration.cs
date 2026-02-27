using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <summary>
    /// Inserta o actualiza BusinessConfiguration Key=3 (PaymentConfig) para configurar
    /// la generación del link de pago con Wompi.
    /// </summary>
    [Migration("20260224100000_AddPaymentConfigToBusinessConfiguration")]
    public partial class AddPaymentConfigToBusinessConfiguration : Migration
    {
        private const string BusinessId = "22222222-2222-2222-2222-222222222222";

        /// <summary>
        /// JSON de PaymentConfiguration: RequiresAnticipo, AnticipoPorcentaje, Provider,
        /// LinkExpirationMinutes, Currency.
        /// </summary>
        private const string PaymentConfigJson = @"{""RequiresAnticipo"":true,""AnticipoPorcentaje"":0.5,""Provider"":""Wompi"",""LinkExpirationMinutes"":120,""Currency"":""COP""}";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var value = PaymentConfigJson.Replace("'", "''");
            var configId = "33333333-3333-3333-3333-333333333333";

            migrationBuilder.Sql($@"
                MERGE [dbo].[BusinessConfigurations] AS target
                USING (SELECT '{BusinessId}' AS BusinessId, 3 AS [Key]) AS source
                ON target.[BusinessId] = source.BusinessId AND target.[Key] = source.[Key]
                WHEN MATCHED THEN
                    UPDATE SET
                        [Value] = N'{value}',
                        [Description] = N'Configuración de pago: anticipo, proveedor Wompi, expiración del link',
                        [UpdatedAt] = GETUTCDATE()
                WHEN NOT MATCHED THEN
                    INSERT (BusinessConfigurationId, BusinessId, [Key], [Value], [Description], IsActive, CreatedAt)
                    VALUES ('{configId}', '{BusinessId}', 3, N'{value}',
                        N'Configuración de pago: anticipo, proveedor Wompi, expiración del link',
                        1, GETUTCDATE());
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
                DELETE FROM [dbo].[BusinessConfigurations]
                WHERE [BusinessId] = '{BusinessId}' AND [Key] = 3;
            ");
        }
    }
}

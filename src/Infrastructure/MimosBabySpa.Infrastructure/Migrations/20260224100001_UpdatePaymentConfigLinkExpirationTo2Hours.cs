using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <summary>
    /// Actualiza PaymentConfig (Key=3) para establecer LinkExpirationMinutes en 120 (2 horas).
    /// </summary>
    [Migration("20260224100001_UpdatePaymentConfigLinkExpirationTo2Hours")]
    public partial class UpdatePaymentConfigLinkExpirationTo2Hours : Migration
    {
        private const string BusinessId = "22222222-2222-2222-2222-222222222222";

        private const string PaymentConfigJson = @"{""RequiresAnticipo"":true,""AnticipoPorcentaje"":0.5,""Provider"":""Wompi"",""LinkExpirationMinutes"":120,""Currency"":""COP""}";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var value = PaymentConfigJson.Replace("'", "''");

            migrationBuilder.Sql($@"
                UPDATE [dbo].[BusinessConfigurations]
                SET [Value] = N'{value}',
                    [UpdatedAt] = GETUTCDATE()
                WHERE [BusinessId] = '{BusinessId}' AND [Key] = 3;
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            var value60 = @"{""RequiresAnticipo"":true,""AnticipoPorcentaje"":0.5,""Provider"":""Wompi"",""LinkExpirationMinutes"":60,""Currency"":""COP""}".Replace("'", "''");

            migrationBuilder.Sql($@"
                UPDATE [dbo].[BusinessConfigurations]
                SET [Value] = N'{value60}',
                    [UpdatedAt] = GETUTCDATE()
                WHERE [BusinessId] = '{BusinessId}' AND [Key] = 3;
            ");
        }
    }
}

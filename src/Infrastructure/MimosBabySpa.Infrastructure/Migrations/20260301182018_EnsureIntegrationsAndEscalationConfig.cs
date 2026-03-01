using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <summary>
    /// 1. Asegura AddHumanEscalationConfiguration: SystemConfiguration 2 y BusinessConfiguration Key=7.
    /// 2. Actualiza Integrations (Key=6) al formato completo (GoogleCalendar + Wompi) extendiendo el valor existente.
    /// </summary>
    public partial class EnsureIntegrationsAndEscalationConfig : Migration
    {
        private const string MimosBusinessId = "22222222-2222-2222-2222-222222222222";
        private const int HumanEscalationErrorThresholdKey = 2;
        private const int EscalationContactsKey = 7;
        private const string WompiDefaults = @"{""privateKey"":"""",""publicKey"":"""",""eventsSecret"":"""",""integritySecret"":"""",""useSandbox"":true,""baseUrl"":null,""sandboxBaseUrl"":""https://sandbox.wompi.co/v1"",""productionBaseUrl"":""https://production.wompi.co/v1"",""requestTimeoutSeconds"":30,""checkoutBaseUrl"":""https://checkout.wompi.co/l/""}";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ServiceAddOnRules_BusinessId_AddOnServiceId_CompatibleServiceId",
                table: "ServiceAddOnRules");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceBundleItems_IncludedServiceId",
                table: "ServiceBundleItems",
                column: "IncludedServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceAddOnRules_BusinessId_AddOnServiceId_CompatibleServiceId",
                table: "ServiceAddOnRules",
                columns: new[] { "BusinessId", "AddOnServiceId", "CompatibleServiceId" },
                unique: true,
                filter: "[CompatibleServiceId] IS NOT NULL");

            // 1. AddHumanEscalationConfiguration: SystemConfiguration y EscalationContacts
            migrationBuilder.Sql($@"
                IF NOT EXISTS (SELECT 1 FROM [dbo].[SystemConfigurations] WHERE [SystemConfigurationId] = {HumanEscalationErrorThresholdKey})
                BEGIN
                    INSERT INTO [dbo].[SystemConfigurations] ([SystemConfigurationId], [Value], [Description], [CreatedAt], [IsActive])
                    VALUES ({HumanEscalationErrorThresholdKey}, N'2', N'Errores consecutivos del orquestador para escalar a humano', GETUTCDATE(), 1);
                END
            ");
            migrationBuilder.Sql($@"
                IF NOT EXISTS (SELECT 1 FROM [dbo].[BusinessConfigurations] WHERE [BusinessId] = '{MimosBusinessId}' AND [Key] = {EscalationContactsKey})
                BEGIN
                    INSERT INTO [dbo].[BusinessConfigurations] ([BusinessConfigurationId], [BusinessId], [Key], [Value], [Description], [IsActive], [CreatedAt])
                    VALUES (NEWID(), '{MimosBusinessId}', {EscalationContactsKey}, N'{{""WhatsAppNumbers"":[]}}', N'Contactos WhatsApp para escalado a humano', 1, GETUTCDATE());
                END
            ");

            // 2. Integrations (Key=6): agregar Wompi si falta; preservar GoogleCalendar existente
            var wompiJson = WompiDefaults.Replace("'", "''");
            migrationBuilder.Sql($@"
                UPDATE [dbo].[BusinessConfigurations]
                SET [Value] = JSON_MODIFY([Value], '$.wompi', JSON_QUERY(N'{wompiJson}')),
                    [UpdatedAt] = GETUTCDATE()
                WHERE [Key] = 6
                  AND (JSON_VALUE([Value], '$.wompi') IS NULL AND JSON_VALUE([Value], '$.Wompi') IS NULL);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ServiceBundleItems_IncludedServiceId",
                table: "ServiceBundleItems");

            migrationBuilder.DropIndex(
                name: "IX_ServiceAddOnRules_BusinessId_AddOnServiceId_CompatibleServiceId",
                table: "ServiceAddOnRules");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceAddOnRules_BusinessId_AddOnServiceId_CompatibleServiceId",
                table: "ServiceAddOnRules",
                columns: new[] { "BusinessId", "AddOnServiceId", "CompatibleServiceId" },
                unique: true);

            // Down no revierte los datos: SystemConfiguration 2 y EscalationContacts quedan
        }
    }
}

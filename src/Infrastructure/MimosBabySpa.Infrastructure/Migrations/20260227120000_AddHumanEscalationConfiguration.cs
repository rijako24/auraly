using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <summary>
    /// Agrega configuración de escalado a humano:
    /// - SystemConfiguration: threshold de errores consecutivos (valor 2).
    /// - BusinessConfiguration EscalationContacts: contactos WhatsApp por negocio.
    /// </summary>
    public partial class AddHumanEscalationConfiguration : Migration
    {
        private const string MimosBusinessId = "22222222-2222-2222-2222-222222222222";
        private const int HumanEscalationErrorThresholdKey = 2; // SystemConfigurationKey
        private const int EscalationContactsKey = 7; // BusinessConfigurationKey

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SystemConfiguration: threshold = 2 errores para escalar
            migrationBuilder.Sql($@"
                IF NOT EXISTS (SELECT 1 FROM [dbo].[SystemConfigurations] WHERE [SystemConfigurationId] = {HumanEscalationErrorThresholdKey})
                BEGIN
                    INSERT INTO [dbo].[SystemConfigurations] ([SystemConfigurationId], [Value], [Description], [CreatedAt], [IsActive])
                    VALUES ({HumanEscalationErrorThresholdKey}, N'2', N'Errores consecutivos del orquestador para escalar a humano', GETUTCDATE(), 1);
                END
            ");

            // BusinessConfiguration: EscalationContacts placeholder (admins configuran sus números)
            migrationBuilder.Sql($@"
                IF NOT EXISTS (SELECT 1 FROM [dbo].[BusinessConfigurations] WHERE [BusinessId] = '{MimosBusinessId}' AND [Key] = {EscalationContactsKey})
                BEGIN
                    INSERT INTO [dbo].[BusinessConfigurations] ([BusinessConfigurationId], [BusinessId], [Key], [Value], [IsActive], [CreatedAt])
                    VALUES (NEWID(), '{MimosBusinessId}', {EscalationContactsKey}, N'{{""WhatsAppNumbers"":[]}}', 1, GETUTCDATE());
                END
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"
                DELETE FROM [dbo].[SystemConfigurations] WHERE [SystemConfigurationId] = {HumanEscalationErrorThresholdKey};
                DELETE FROM [dbo].[BusinessConfigurations] WHERE [BusinessId] = '{MimosBusinessId}' AND [Key] = {EscalationContactsKey};
            ");
        }
    }
}

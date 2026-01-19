using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MoveIntentDefinitionsToBusinessConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var businessId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var now = DateTime.UtcNow;
            var configId12 = Guid.Parse("88888888-8888-8888-8888-888888888888"); // IntentDefinitions
            
            var intentDefinitionsValue = @"- Greeting: Saludo inicial
- AskAge: Pregunta por edad del bebé
- AskInfo: Pregunta sobre el spa (horarios, ubicación, seguridad)
- AskPrice: Pregunta sobre planes o precios
- Objecion: Dudas o miedos
- ReservationRequest: Quiere reservar
- TalkToHuman: Pide hablar con humano
- FollowUp: Continuación de conversación";
            
            // Insertar IntentDefinitions en BusinessConfiguration solo si no existe
            migrationBuilder.Sql($@"
                IF NOT EXISTS (SELECT 1 FROM BusinessConfigurations WHERE BusinessId = '{businessId}' AND [Key] = 12)
                BEGIN
                    INSERT INTO BusinessConfigurations (BusinessConfigurationId, BusinessId, [Key], Value, Description, IsActive, CreatedAt)
                    VALUES ('{configId12}', '{businessId}', 12, N'{intentDefinitionsValue.Replace("'", "''")}', N'Definiciones de intenciones disponibles para este negocio', 1, '{now:yyyy-MM-dd HH:mm:ss}');
                END
            ");
            
            // Eliminar IntentClassificationPrompt de SystemConfiguration (ID 7) ya que ahora todo está unificado en ContextExtractionPrompt
            migrationBuilder.Sql($@"
                DELETE FROM SystemConfigurations WHERE SystemConfigurationId = 7;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}

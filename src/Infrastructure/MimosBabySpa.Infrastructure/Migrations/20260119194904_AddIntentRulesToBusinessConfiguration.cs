using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIntentRulesToBusinessConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var businessId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var now = DateTime.UtcNow;
            var configId11 = Guid.Parse("77777777-7777-7777-7777-777777777777"); // IntentRules
            
            var intentRulesValue = @"{
  ""Greeting"": ""Saluda cálidamente de forma natural. Menciona que eres de Mimos Baby Spa. Varía: a veces pregunta cómo puedes ayudar, a veces simplemente saluda y espera a que te cuenten qué necesitan."",
  ""AskAge"": ""Pregunta la edad del bebé de forma natural y amable. Cuando el cliente te dé la edad (acepta meses o años): Reconoce la edad con entusiasmo genuino y natural. Puedes preguntar si ya conoce nuestros planes, pero hazlo de forma conversacional, no siempre. Si NO los conoce: EXPLICA los planes que se adaptan a la edad del bebé de forma NARRATIVA y CONVERSACIONAL, integrando la información en párrafos naturales. NO uses listas ni viñetas estructuradas. Si YA los conoce: pregunta si está interesado en alguno específico o simplemente ofrece ayuda de forma natural. Sé CONVERSACIONAL y NATURAL, como si estuvieras platicando con una amiga. NO suenes como un robot leyendo un script. NO siempre cierres con pregunta - varía tus respuestas"",
  ""AskInfo"": ""Proporciona información sobre horarios, ubicación y seguridad de forma conversacional y natural. Tranquiliza al cliente. NO uses listas estructuradas, integra la información en texto fluido."",
  ""AskPrice"": ""Explica los planes de forma CONVERSACIONAL y NARRATIVA, integrando la información de forma natural. Menciona los precios de forma fluida dentro del texto. NO uses listas estructuradas. Varía: a veces pregunta si desea reservar, a veces solo ofrece ayuda, a veces hace un comentario amable."",
  ""Objecion"": ""Valida emocionalmente la preocupación de forma genuina y empática. Responde con comprensión y datos tranquilizadores integrados de forma natural. Sugiere el plan adecuado de forma conversacional."",
  ""ReservationRequest"": ""Confirma el plan elegido y proporciona instrucciones para reservar de forma natural y conversacional. Sé entusiasta pero genuina, no robótica."",
  ""TalkToHuman"": ""Transfiere a humano sin fricción. Agradece por contactar de forma genuina y menciona que un asesor se comunicará pronto."",
  ""FollowUp"": ""Mantén continuidad usando el contexto del cliente de forma natural. Refiere a información previa si es relevante, pero hazlo de forma conversacional, no como un resumen estructurado.""
}";
            
            // Insertar IntentRules solo si no existe
            migrationBuilder.Sql($@"
                IF NOT EXISTS (SELECT 1 FROM BusinessConfigurations WHERE BusinessId = '{businessId}' AND [Key] = 11)
                BEGIN
                    INSERT INTO BusinessConfigurations (BusinessConfigurationId, BusinessId, [Key], Value, Description, IsActive, CreatedAt)
                    VALUES ('{configId11}', '{businessId}', 11, N'{intentRulesValue.Replace("'", "''")}', N'Reglas específicas para cada intención del asistente (formato JSON)', 1, '{now:yyyy-MM-dd HH:mm:ss}');
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}

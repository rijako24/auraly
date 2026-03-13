using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    [Migration("20260313120000_UpdatePersonalityToLunaAndToneRefactor")]
    /// <summary>
    /// Actualiza la personalidad de Mimos a Luna con tono más cálido y tierno.
    /// Refactor: todo el tono viene de BusinessConfiguration; SystemConfiguration.ToneAndStyle es fallback.
    /// </summary>
    public partial class UpdatePersonalityToLunaAndToneRefactor : Migration
    {
        private const string PersonalityValue = @"Eres Luna, una asesora comercial experta y muy humana de Mimos Baby Spa, especializada en servicios de spa para bebés. Hablas de forma natural, cálida, tierna y conversacional, como una amiga cercana que ama el cuidado de bebés y transmite calidez en cada mensaje.

TU ESTILO:
- Usa emoticonos de forma natural y relacionada con lo que dices (👶 bebé, 💙 cariño/apoyo, ✨ magia/experiencia, 🙏 gracias, 😊 calidez, 🛁 spa/agua, 🌊 hidroterapia, 💆 bienestar, 🎉 celebración)
- Sé especialmente tierna cuando hables del bebé, de las mamás o de momentos especiales
- Transmite emoción genuina y cariño sin exagerar";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var businessId = "22222222-2222-2222-2222-222222222222";
            var escapedValue = PersonalityValue.Replace("'", "''");

            migrationBuilder.Sql($@"
                UPDATE BusinessConfigurations
                SET [Value] = N'{escapedValue}'
                WHERE BusinessId = '{businessId}' AND [Key] = 0;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            var businessId = "22222222-2222-2222-2222-222222222222";
            var oldValue = "Eres María, una asesora comercial experta y muy humana de Mimos Baby Spa, especializada en servicios de spa para bebés. Hablas de forma natural, cálida y conversacional, como si fueras una amiga que conoce mucho sobre el cuidado de bebés.";
            var escapedValue = oldValue.Replace("'", "''");

            migrationBuilder.Sql($@"
                UPDATE BusinessConfigurations
                SET [Value] = N'{escapedValue}'
                WHERE BusinessId = '{businessId}' AND [Key] = 0;
            ");
        }
    }
}

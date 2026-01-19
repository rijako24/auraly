using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateIntentClassificationPromptTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Actualizar IntentClassificationPrompt en SystemConfiguration para usar template con placeholders
            var templateValue = @"Clasifica el siguiente mensaje en UNA de estas intenciones:

{intentDefinitions}

Mensaje: ""{messageText}""

Responde SOLO con el nombre de la intención, sin explicaciones.";
            
            migrationBuilder.Sql($@"
                UPDATE SystemConfigurations 
                SET Value = N'{templateValue.Replace("'", "''")}'
                WHERE SystemConfigurationId = 7;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}

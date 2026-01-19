using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveIntentClassificationPrompt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

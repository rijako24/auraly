using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveImportantContextFieldsData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Eliminar BusinessConfigurations con Key = 1 (ImportantContextFields)
            // Ahora solo existe BusinessInformation (Key: 0) con todo el contenido
            migrationBuilder.Sql(@"
                DELETE FROM BusinessConfigurations 
                WHERE [Key] = 1;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}

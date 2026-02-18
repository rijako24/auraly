using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <summary>
    /// Elimina la columna Intent de Messages.
    /// Intent nunca se usó en lógica de negocio — siempre se escribió como "FollowUp" hardcodeado
    /// y ningún query filtraba por él. La clasificación de intent fue reemplazada por
    /// ExtractionIntentions en el orquestador híbrido.
    /// </summary>
    public partial class RemoveMessageIntent : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Intent",
                table: "Messages");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Intent",
                table: "Messages",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "FollowUp");
        }
    }
}

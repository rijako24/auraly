using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveKeyFromConversationContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ConversationContexts_ConversationId_Key",
                table: "ConversationContexts");

            migrationBuilder.DropColumn(
                name: "Key",
                table: "ConversationContexts");

            migrationBuilder.RenameColumn(
                name: "Value",
                table: "ConversationContexts",
                newName: "Context");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Context",
                table: "ConversationContexts",
                newName: "Value");

            migrationBuilder.AddColumn<string>(
                name: "Key",
                table: "ConversationContexts",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationContexts_ConversationId_Key",
                table: "ConversationContexts",
                columns: new[] { "ConversationId", "Key" },
                unique: true);
        }
    }
}

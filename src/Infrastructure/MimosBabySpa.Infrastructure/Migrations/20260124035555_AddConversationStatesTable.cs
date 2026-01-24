using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationStatesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConversationStates",
                columns: table => new
                {
                    ConversationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StateJson = table.Column<string>(type: "NVARCHAR(MAX)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationStates", x => x.ConversationId);
                    table.ForeignKey(
                        name: "FK_ConversationStates_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "BusinessId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConversationStates_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "ConversationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationStates_BusinessId",
                table: "ConversationStates",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationStates_ConversationId",
                table: "ConversationStates",
                column: "ConversationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConversationStates");
        }
    }
}

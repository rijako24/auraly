using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConversationContexts",
                columns: table => new
                {
                    ConversationContextId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "NVARCHAR(MAX)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationContexts", x => x.ConversationContextId);
                    table.ForeignKey(
                        name: "FK_ConversationContexts_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "ConversationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationContexts_ConversationId",
                table: "ConversationContexts",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationContexts_ConversationId_Key",
                table: "ConversationContexts",
                columns: new[] { "ConversationId", "Key" },
                unique: true);

            // Migrar datos existentes de Conversation a ConversationContext
            migrationBuilder.Sql(@"
                INSERT INTO ConversationContexts (ConversationContextId, ConversationId, [Key], Value, CreatedAt)
                SELECT 
                    NEWID() as ConversationContextId,
                    ConversationId,
                    'CustomerName' as [Key],
                    CustomerName as Value,
                    GETUTCDATE() as CreatedAt
                FROM Conversations
                WHERE CustomerName IS NOT NULL AND CustomerName != '';

                INSERT INTO ConversationContexts (ConversationContextId, ConversationId, [Key], Value, CreatedAt)
                SELECT 
                    NEWID() as ConversationContextId,
                    ConversationId,
                    'BabyAge' as [Key],
                    CAST(BabyAge AS NVARCHAR(MAX)) + ' meses' as Value,
                    GETUTCDATE() as CreatedAt
                FROM Conversations
                WHERE BabyAge IS NOT NULL;

                INSERT INTO ConversationContexts (ConversationContextId, ConversationId, [Key], Value, CreatedAt)
                SELECT 
                    NEWID() as ConversationContextId,
                    ConversationId,
                    'RecommendedPlan' as [Key],
                    RecommendedPlan as Value,
                    GETUTCDATE() as CreatedAt
                FROM Conversations
                WHERE RecommendedPlan IS NOT NULL AND RecommendedPlan != '';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConversationContexts");
        }
    }
}

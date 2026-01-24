using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveNotesAddConversationIdToReservation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Reservations");

            migrationBuilder.AddColumn<Guid>(
                name: "ConversationId",
                table: "Reservations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_ConversationId",
                table: "Reservations",
                column: "ConversationId");

            migrationBuilder.AddForeignKey(
                name: "FK_Reservations_Conversations_ConversationId",
                table: "Reservations",
                column: "ConversationId",
                principalTable: "Conversations",
                principalColumn: "ConversationId",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Reservations_Conversations_ConversationId",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_ConversationId",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "ConversationId",
                table: "Reservations");

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Reservations",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);
        }
    }
}

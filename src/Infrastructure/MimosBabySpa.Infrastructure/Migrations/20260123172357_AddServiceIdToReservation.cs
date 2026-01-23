using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceIdToReservation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Paso 1: Agregar ServiceId como nullable primero
            migrationBuilder.AddColumn<Guid>(
                name: "ServiceId",
                table: "Reservations",
                type: "uniqueidentifier",
                nullable: true);

            // Paso 2: Actualizar ServiceId basándose en ServiceName para reservas existentes
            // Primero intentar match exacto
            migrationBuilder.Sql(@"
                UPDATE r
                SET r.ServiceId = s.ServiceId
                FROM Reservations r
                INNER JOIN Services s ON s.BusinessId = r.BusinessId 
                    AND s.ServiceName = r.ServiceName 
                    AND s.IsActive = 1
                WHERE r.ServiceId IS NULL;
            ");

            // Luego intentar match removiendo prefijo "Plan " si existe
            migrationBuilder.Sql(@"
                UPDATE r
                SET r.ServiceId = s.ServiceId
                FROM Reservations r
                INNER JOIN Services s ON s.BusinessId = r.BusinessId 
                    AND s.ServiceName = REPLACE(r.ServiceName, 'Plan ', '')
                    AND s.IsActive = 1
                WHERE r.ServiceId IS NULL;
            ");

            // Paso 3: Eliminar reservas huérfanas que no tienen servicio válido (opcional, comentado por seguridad)
            // migrationBuilder.Sql(@"
            //     DELETE FROM Reservations 
            //     WHERE ServiceId IS NULL;
            // ");

            // Paso 4: Hacer ServiceId NOT NULL
            migrationBuilder.AlterColumn<Guid>(
                name: "ServiceId",
                table: "Reservations",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Paso 5: Hacer ServiceName nullable
            migrationBuilder.AlterColumn<string>(
                name: "ServiceName",
                table: "Reservations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);

            // Paso 6: Crear índice y foreign key
            migrationBuilder.CreateIndex(
                name: "IX_Reservations_ServiceId",
                table: "Reservations",
                column: "ServiceId");

            migrationBuilder.AddForeignKey(
                name: "FK_Reservations_Services_ServiceId",
                table: "Reservations",
                column: "ServiceId",
                principalTable: "Services",
                principalColumn: "ServiceId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Reservations_Services_ServiceId",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_ServiceId",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "ServiceId",
                table: "Reservations");

            migrationBuilder.AlterColumn<string>(
                name: "ServiceName",
                table: "Reservations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);
        }
    }
}

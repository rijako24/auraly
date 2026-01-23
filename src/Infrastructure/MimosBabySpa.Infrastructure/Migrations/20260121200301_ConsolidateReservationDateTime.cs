using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ConsolidateReservationDateTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Eliminar índice antiguo
            migrationBuilder.DropIndex(
                name: "IX_Reservations_BusinessId_ReservationDate_ReservationTime",
                table: "Reservations");

            // Agregar nueva columna ReservationDateTime
            migrationBuilder.AddColumn<DateTime>(
                name: "ReservationDateTime",
                table: "Reservations",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Migrar datos: combinar ReservationDate + ReservationTime en ReservationDateTime
            migrationBuilder.Sql(@"
                UPDATE Reservations 
                SET ReservationDateTime = DATEADD(MINUTE, 
                    DATEDIFF(MINUTE, CAST('00:00:00' AS TIME), ReservationTime), 
                    CAST(ReservationDate AS DATETIME))
            ");

            // Eliminar columnas antiguas
            migrationBuilder.DropColumn(
                name: "ReservationTime",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "ReservationDate",
                table: "Reservations");

            // Crear nuevo índice
            migrationBuilder.CreateIndex(
                name: "IX_Reservations_BusinessId_ReservationDateTime",
                table: "Reservations",
                columns: new[] { "BusinessId", "ReservationDateTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Eliminar índice nuevo
            migrationBuilder.DropIndex(
                name: "IX_Reservations_BusinessId_ReservationDateTime",
                table: "Reservations");

            // Agregar columnas antiguas
            migrationBuilder.AddColumn<DateTime>(
                name: "ReservationDate",
                table: "Reservations",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<TimeSpan>(
                name: "ReservationTime",
                table: "Reservations",
                type: "time",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 0, 0, 0));

            // Migrar datos de vuelta: separar ReservationDateTime en ReservationDate y ReservationTime
            migrationBuilder.Sql(@"
                UPDATE Reservations 
                SET ReservationDate = CAST(ReservationDateTime AS DATE),
                    ReservationTime = CAST(ReservationDateTime AS TIME)
            ");

            // Eliminar columna nueva
            migrationBuilder.DropColumn(
                name: "ReservationDateTime",
                table: "Reservations");

            // Crear índice antiguo
            migrationBuilder.CreateIndex(
                name: "IX_Reservations_BusinessId_ReservationDate_ReservationTime",
                table: "Reservations",
                columns: new[] { "BusinessId", "ReservationDate", "ReservationTime" });
        }
    }
}

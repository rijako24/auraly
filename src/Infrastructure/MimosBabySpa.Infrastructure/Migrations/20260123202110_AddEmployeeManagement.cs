using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Paso 1: Crear tablas Employees y EmployeeServices primero
            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.EmployeeId);
                    table.ForeignKey(
                        name: "FK_Employees_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "BusinessId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeServices",
                columns: table => new
                {
                    EmployeeServiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeServices", x => x.EmployeeServiceId);
                    table.ForeignKey(
                        name: "FK_EmployeeServices_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "EmployeeId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeServices_Services_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "Services",
                        principalColumn: "ServiceId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Employees_BusinessId",
                table: "Employees",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_BusinessId_Name",
                table: "Employees",
                columns: new[] { "BusinessId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeServices_EmployeeId",
                table: "EmployeeServices",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeServices_EmployeeId_ServiceId",
                table: "EmployeeServices",
                columns: new[] { "EmployeeId", "ServiceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeServices_ServiceId",
                table: "EmployeeServices",
                column: "ServiceId");

            // Paso 2: Agregar EmployeeId como nullable primero
            migrationBuilder.AddColumn<Guid>(
                name: "EmployeeId",
                table: "Reservations",
                type: "uniqueidentifier",
                nullable: true);

            // Paso 3: Crear un empleado genérico para asignar a reservas existentes
            // Este empleado puede dar todos los servicios (se configurará después)
            migrationBuilder.Sql(@"
                DECLARE @DefaultEmployeeId UNIQUEIDENTIFIER = NEWID();
                DECLARE @BusinessId UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';
                
                -- Crear empleado genérico
                INSERT INTO [dbo].[Employees] ([EmployeeId], [BusinessId], [Name], [IsActive], [CreatedAt])
                VALUES (@DefaultEmployeeId, @BusinessId, 'Empleado Genérico', 1, GETUTCDATE());
                
                -- Asignar este empleado a todas las reservas existentes
                UPDATE [dbo].[Reservations]
                SET [EmployeeId] = @DefaultEmployeeId
                WHERE [EmployeeId] IS NULL;
                
                -- Asignar este empleado a todos los servicios existentes
                INSERT INTO [dbo].[EmployeeServices] ([EmployeeServiceId], [EmployeeId], [ServiceId], [CreatedAt])
                SELECT NEWID(), @DefaultEmployeeId, [ServiceId], GETUTCDATE()
                FROM [dbo].[Services]
                WHERE [BusinessId] = @BusinessId AND [IsActive] = 1;
            ");

            // Paso 4: Hacer EmployeeId NOT NULL después de poblarlo
            migrationBuilder.AlterColumn<Guid>(
                name: "EmployeeId",
                table: "Reservations",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            // Paso 5: Crear índices y foreign key para Reservations
            migrationBuilder.CreateIndex(
                name: "IX_Reservations_EmployeeId",
                table: "Reservations",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_EmployeeId_ReservationDateTime",
                table: "Reservations",
                columns: new[] { "EmployeeId", "ReservationDateTime" });

            migrationBuilder.AddForeignKey(
                name: "FK_Reservations_Employees_EmployeeId",
                table: "Reservations",
                column: "EmployeeId",
                principalTable: "Employees",
                principalColumn: "EmployeeId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Reservations_Employees_EmployeeId",
                table: "Reservations");

            migrationBuilder.DropTable(
                name: "EmployeeServices");

            migrationBuilder.DropTable(
                name: "Employees");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_EmployeeId",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_EmployeeId_ReservationDateTime",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "EmployeeId",
                table: "Reservations");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <summary>
    /// Elimina las tablas ServiceCoexistenceRules y ReservationMetadata.
    /// Ambas estaban sin uso activo en la lógica de negocio actual.
    /// </summary>
    public partial class DropServiceCoexistenceRulesAndReservationMetadata : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ServiceCoexistenceRules");
            migrationBuilder.DropTable(name: "ReservationMetadata");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReservationMetadata",
                columns: table => new
                {
                    ReservationMetadataId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Field = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservationMetadata", x => x.ReservationMetadataId);
                    table.ForeignKey(
                        name: "FK_ReservationMetadata_Reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "Reservations",
                        principalColumn: "ReservationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReservationMetadata_ReservationId",
                table: "ReservationMetadata",
                column: "ReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_ReservationMetadata_ReservationId_Field",
                table: "ReservationMetadata",
                columns: new[] { "ReservationId", "Field" });

            migrationBuilder.CreateTable(
                name: "ServiceCoexistenceRules",
                columns: table => new
                {
                    ServiceCoexistenceRuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceId1 = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceId2 = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CanCoexist = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceCoexistenceRules", x => x.ServiceCoexistenceRuleId);
                    table.ForeignKey(
                        name: "FK_ServiceCoexistenceRules_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "BusinessId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ServiceCoexistenceRules_Services_ServiceId1",
                        column: x => x.ServiceId1,
                        principalTable: "Services",
                        principalColumn: "ServiceId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ServiceCoexistenceRules_Services_ServiceId2",
                        column: x => x.ServiceId2,
                        principalTable: "Services",
                        principalColumn: "ServiceId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceCoexistenceRules_BusinessId",
                table: "ServiceCoexistenceRules",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceCoexistenceRules_ServiceId1",
                table: "ServiceCoexistenceRules",
                column: "ServiceId1");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceCoexistenceRules_ServiceId2",
                table: "ServiceCoexistenceRules",
                column: "ServiceId2");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceCoexistenceRules_BusinessId_ServiceId1_ServiceId2",
                table: "ServiceCoexistenceRules",
                columns: new[] { "BusinessId", "ServiceId1", "ServiceId2" },
                unique: true);
        }
    }
}

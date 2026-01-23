using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddResourceManagementAndConversationState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "State",
                table: "Conversations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "BusinessResources",
                columns: table => new
                {
                    BusinessResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResourceName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessResources", x => x.BusinessResourceId);
                    table.ForeignKey(
                        name: "FK_BusinessResources_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "BusinessId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Services",
                columns: table => new
                {
                    ServiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Services", x => x.ServiceId);
                    table.ForeignKey(
                        name: "FK_Services_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "BusinessId",
                        onDelete: ReferentialAction.Restrict);
                });

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

            migrationBuilder.CreateTable(
                name: "ServiceResourceUsages",
                columns: table => new
                {
                    ServiceResourceUsageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceResourceUsages", x => x.ServiceResourceUsageId);
                    table.ForeignKey(
                        name: "FK_ServiceResourceUsages_BusinessResources_BusinessResourceId",
                        column: x => x.BusinessResourceId,
                        principalTable: "BusinessResources",
                        principalColumn: "BusinessResourceId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ServiceResourceUsages_Services_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "Services",
                        principalColumn: "ServiceId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_State",
                table: "Conversations",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessResources_BusinessId",
                table: "BusinessResources",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessResources_BusinessId_ResourceName",
                table: "BusinessResources",
                columns: new[] { "BusinessId", "ResourceName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceCoexistenceRules_BusinessId",
                table: "ServiceCoexistenceRules",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceCoexistenceRules_BusinessId_ServiceId1_ServiceId2",
                table: "ServiceCoexistenceRules",
                columns: new[] { "BusinessId", "ServiceId1", "ServiceId2" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceCoexistenceRules_ServiceId1",
                table: "ServiceCoexistenceRules",
                column: "ServiceId1");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceCoexistenceRules_ServiceId2",
                table: "ServiceCoexistenceRules",
                column: "ServiceId2");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceResourceUsages_BusinessResourceId",
                table: "ServiceResourceUsages",
                column: "BusinessResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceResourceUsages_ServiceId_BusinessResourceId",
                table: "ServiceResourceUsages",
                columns: new[] { "ServiceId", "BusinessResourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Services_BusinessId",
                table: "Services",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_Services_BusinessId_ServiceName",
                table: "Services",
                columns: new[] { "BusinessId", "ServiceName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServiceCoexistenceRules");

            migrationBuilder.DropTable(
                name: "ServiceResourceUsages");

            migrationBuilder.DropTable(
                name: "BusinessResources");

            migrationBuilder.DropTable(
                name: "Services");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_State",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "State",
                table: "Conversations");
        }
    }
}

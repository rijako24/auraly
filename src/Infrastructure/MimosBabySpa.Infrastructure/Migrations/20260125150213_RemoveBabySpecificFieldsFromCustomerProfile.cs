using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveBabySpecificFieldsFromCustomerProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BabyAgeMonths",
                table: "CustomerProfiles");

            migrationBuilder.DropColumn(
                name: "BabyConditions",
                table: "CustomerProfiles");

            migrationBuilder.DropColumn(
                name: "BabyName",
                table: "CustomerProfiles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BabyAgeMonths",
                table: "CustomerProfiles",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BabyConditions",
                table: "CustomerProfiles",
                type: "NVARCHAR(MAX)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BabyName",
                table: "CustomerProfiles",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }
    }
}

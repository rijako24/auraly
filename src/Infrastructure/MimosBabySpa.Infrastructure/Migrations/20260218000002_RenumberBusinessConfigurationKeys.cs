using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <summary>
    /// Renumera las claves de BusinessConfigurations para que coincidan con el enum BusinessConfigurationKey:
    ///   Personality          = 0  (ya correcto en DB, sin cambios)
    ///   EntityExtractionConfig = 1  (estaba como 2 en DB)
    /// </summary>
    public partial class RenumberBusinessConfigurationKeys : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE [dbo].[BusinessConfigurations] SET [Key] = 1 WHERE [Key] = 2;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE [dbo].[BusinessConfigurations] SET [Key] = 2 WHERE [Key] = 1;");
        }
    }
}

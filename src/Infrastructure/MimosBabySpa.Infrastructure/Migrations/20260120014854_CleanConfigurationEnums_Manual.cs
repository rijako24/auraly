using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CleanConfigurationEnums_Manual : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Eliminar configuraciones obsoletas de SystemConfiguration
            migrationBuilder.Sql(@"
                DELETE FROM SystemConfigurations 
                WHERE SystemConfigurationId IN (2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13);
            ");

            // Eliminar configuraciones obsoletas de BusinessConfiguration
            // Ahora solo existe BusinessInformation (Key: 0)
            // Eliminar todas las demás keys que ya no se usan (incluyendo Key: 1)
            migrationBuilder.Sql(@"
                DELETE FROM BusinessConfigurations 
                WHERE [Key] != 0;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}

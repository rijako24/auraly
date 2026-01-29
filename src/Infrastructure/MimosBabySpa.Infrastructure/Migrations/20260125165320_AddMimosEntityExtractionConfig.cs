using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMimosEntityExtractionConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Configuración de extracción de entidades para Mimos Baby Spa
            // Campos esenciales: Nombre del padre, Edad del bebé, Permiso de redes sociales, Preocupaciones
            
            var configJson = @"{
  ""entityType"": ""dependent"",
  ""relevantFields"": [
    ""parent_name"",
    ""baby_age_months"",
    ""allows_social_media"",
    ""concerns""
  ],
  ""fieldDescriptions"": {
    ""parent_name"": ""Nombre del padre o madre del bebé"",
    ""baby_age_months"": ""Edad del bebé en meses"",
    ""allows_social_media"": ""Si el cliente permite el uso de fotos/videos en redes sociales (true/false)"",
    ""concerns"": ""Preocupaciones o problemas específicos del bebé mencionados por el cliente""
  },
  ""keywords"": {
    ""concerns"": [
      ""cólico"",
      ""cólicos"",
      ""llanto"",
      ""sueño"",
      ""reflujo"",
      ""estrés"",
      ""tensión"",
      ""problema"",
      ""preocupación"",
      ""molestia""
    ],
    ""social_media"": [
      ""redes sociales"",
      ""fotos"",
      ""videos"",
      ""publicar"",
      ""compartir"",
      ""instagram"",
      ""facebook""
    ]
  },
  ""isActive"": true
}";

            // Insertar configuración para todos los negocios existentes
            migrationBuilder.Sql($@"
                INSERT INTO BusinessConfigurations (
                    BusinessConfigurationId,
                    BusinessId,
                    [Key],
                    Value,
                    Description,
                    IsActive,
                    CreatedAt,
                    UpdatedAt
                )
                SELECT 
                    NEWID(),
                    BusinessId,
                    2, -- EntityExtractionConfig
                    '{configJson.Replace("'", "''")}',
                    'Configuración de extracción de entidades del cliente - Campos esenciales de Mimos',
                    1,
                    GETUTCDATE(),
                    NULL
                FROM Businesses
                WHERE NOT EXISTS (
                    SELECT 1 
                    FROM BusinessConfigurations 
                    WHERE BusinessConfigurations.BusinessId = Businesses.BusinessId 
                    AND BusinessConfigurations.[Key] = 2
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Eliminar configuración de extracción de entidades
            migrationBuilder.Sql(@"
                DELETE FROM BusinessConfigurations
                WHERE [Key] = 2; -- EntityExtractionConfig
            ");
        }
    }
}

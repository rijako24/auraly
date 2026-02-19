using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <summary>
    /// Agrega el atributo SelectedAddOns al EntityExtractionConfig de Mimos Baby Spa.
    /// Permite que el LLM extraiga los add-ons elegidos por el cliente (Fotografía, Decoración, etc.)
    /// y los persista en state.Attributes["SelectedAddOns"] para su uso en CreateReservationToolHandler.
    /// </summary>
    [Migration("20260218000009_AddSelectedAddOnsToEntityExtractionConfig")]
    public class AddSelectedAddOnsToEntityExtractionConfig : Migration
    {
        private const string BusinessId = "22222222-2222-2222-2222-222222222222";

        // BusinessConfigurationKey.EntityExtractionConfig = 1
        private const int EntityExtractionConfigKey = 1;

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var configJson = @"{
  ""BabyAge"": {
    ""Name"": ""BabyAge"",
    ""DisplayName"": ""Edad del bebé"",
    ""Description"": ""Edad del bebé en meses"",
    ""Type"": ""Number"",
    ""IsRequired"": false,
    ""ValidationPattern"": ""^\\d{1,3}$"",
    ""Metadata"": {
      ""min"": ""0"",
      ""max"": ""120""
    }
  },
  ""BabyName"": {
    ""Name"": ""BabyName"",
    ""DisplayName"": ""Nombre del bebé"",
    ""Description"": ""Nombre del bebé"",
    ""Type"": ""Text"",
    ""IsRequired"": false,
    ""Metadata"": {
      ""minLength"": ""2"",
      ""maxLength"": ""50""
    }
  },
  ""SpecialConditions"": {
    ""Name"": ""SpecialConditions"",
    ""DisplayName"": ""Condiciones especiales"",
    ""Description"": ""Condiciones médicas o especiales del bebé"",
    ""Type"": ""Text"",
    ""IsRequired"": false,
    ""Metadata"": {
      ""maxLength"": ""500""
    }
  },
  ""SelectedAddOns"": {
    ""Name"": ""SelectedAddOns"",
    ""DisplayName"": ""Add-ons seleccionados"",
    ""Description"": ""Lista de add-ons que el cliente eligió. Nombres exactos del catálogo separados por coma (ej: Fotografía Sencilla, Decoración Premium). Solo incluir si el cliente aceptó add-ons."",
    ""Type"": ""Text"",
    ""IsRequired"": false,
    ""Metadata"": {
      ""maxLength"": ""500""
    }
  }
}";

            var escapedJson = configJson.Replace("'", "''");

            migrationBuilder.Sql($@"
                UPDATE [dbo].[BusinessConfigurations]
                SET [Value] = N'{escapedJson}',
                    [UpdatedAt] = GETUTCDATE()
                WHERE [BusinessId] = '{BusinessId}'
                  AND [Key] = {EntityExtractionConfigKey};
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restaurar config sin SelectedAddOns (estado anterior)
            var configJson = @"{
  ""BabyAge"": {
    ""Name"": ""BabyAge"",
    ""DisplayName"": ""Edad del bebé"",
    ""Description"": ""Edad del bebé en meses"",
    ""Type"": ""Number"",
    ""IsRequired"": false,
    ""ValidationPattern"": ""^\\d{1,3}$"",
    ""Metadata"": {
      ""min"": ""0"",
      ""max"": ""120""
    }
  },
  ""BabyName"": {
    ""Name"": ""BabyName"",
    ""DisplayName"": ""Nombre del bebé"",
    ""Description"": ""Nombre del bebé"",
    ""Type"": ""Text"",
    ""IsRequired"": false,
    ""Metadata"": {
      ""minLength"": ""2"",
      ""maxLength"": ""50""
    }
  },
  ""SpecialConditions"": {
    ""Name"": ""SpecialConditions"",
    ""DisplayName"": ""Condiciones especiales"",
    ""Description"": ""Condiciones médicas o especiales del bebé"",
    ""Type"": ""Text"",
    ""IsRequired"": false,
    ""Metadata"": {
      ""maxLength"": ""500""
    }
  }
}";

            var escapedJson = configJson.Replace("'", "''");

            migrationBuilder.Sql($@"
                UPDATE [dbo].[BusinessConfigurations]
                SET [Value] = N'{escapedJson}',
                    [UpdatedAt] = GETUTCDATE()
                WHERE [BusinessId] = '{BusinessId}'
                  AND [Key] = {EntityExtractionConfigKey};
            ");
        }
    }
}

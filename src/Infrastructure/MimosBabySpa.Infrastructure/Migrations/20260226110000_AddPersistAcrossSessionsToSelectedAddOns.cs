using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <summary>
    /// Marca SelectedAddOns con PersistAcrossSessions=false para que se limpie al iniciar nueva sesión.
    /// Evita que add-ons de sesiones anteriores contaminen precios y resúmenes de nuevos ciclos.
    /// Multi-tenant: cada negocio define sus atributos transaccionales en su EntityExtractionConfig.
    /// </summary>
    public partial class AddPersistAcrossSessionsToSelectedAddOns : Migration
    {
        private const string BusinessId = "22222222-2222-2222-2222-222222222222";
        private const int EntityExtractionConfigKey = 1; // BusinessConfigurationKey.EntityExtractionConfig

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
    ""PersistAcrossSessions"": false,
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
    }
}

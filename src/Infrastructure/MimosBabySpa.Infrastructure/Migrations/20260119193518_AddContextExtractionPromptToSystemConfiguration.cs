using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddContextExtractionPromptToSystemConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var now = DateTime.UtcNow;
            
            var contextExtractionPromptValue = @"Analiza el siguiente mensaje del cliente y realiza DOS tareas:

TAREA 1 - CLASIFICAR INTENCIÓN:
{intentPrompt}

TAREA 2 - EXTRAER CONTEXTO:
Del mismo mensaje, extrae SOLO la información IMPORTANTE que debe guardarse para futuras conversaciones según las siguientes instrucciones:

{contextData}

Información del negocio (para contexto):
{generalInfo}

Reglas de planes (si aplica):
{planRules}

INSTRUCCIONES PARA EXTRAER CONTEXTO:
1. Analiza el mensaje y extrae información relevante según las instrucciones proporcionadas
2. Si el mensaje menciona edad (meses o años), extrae la edad y conviértela a meses si es necesario
3. Si hay reglas de planes y se menciona edad, determina el plan recomendado según las reglas
4. Solo extrae información que sea RELEVANTE y ÚTIL para futuras conversaciones
5. NO extraigas información temporal o que no aporte valor
6. Formatea cada dato extraído como una oración natural completa
7. El contexto debe ser SOLO una lista de strings, cada string es una oración completa con información relevante

Responde SOLO en formato JSON con esta estructura:
{{
  ""intent"": ""NombreDeLaIntencion"",
  ""context"": [
    ""El bebé tiene 24 meses"",
    ""El plan recomendado es Plan Marineritos"",
    ""Quiere venir el sábado""
  ]
}}

IMPORTANTE: El campo ""context"" es SOLO una lista de strings (array de strings). Cada elemento del array es una oración completa con información relevante extraída del mensaje.

Si no hay información relevante para el contexto, el array ""context"" debe estar vacío: []

Ejemplo si el mensaje dice ""mi bebé tiene 2 años"":
{{
  ""intent"": ""AskAge"",
  ""context"": [
    ""El bebé tiene 24 meses"",
    ""El plan recomendado es Plan Marineritos""
  ]
}}";
            
            // Insertar ContextExtractionPrompt solo si no existe
            migrationBuilder.Sql($@"
                IF NOT EXISTS (SELECT 1 FROM SystemConfigurations WHERE SystemConfigurationId = 8)
                BEGIN
                    INSERT INTO SystemConfigurations (SystemConfigurationId, Value, Description, CreatedAt, IsActive)
                    VALUES (8, N'{contextExtractionPromptValue.Replace("'", "''")}', N'Prompt unificado para clasificación de intención y extracción de contexto', '{now:yyyy-MM-dd HH:mm:ss}', 1);
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RestoreContextExtractionPrompt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var now = DateTime.UtcNow;
            var contextExtractionPromptValue = @"Analiza el siguiente mensaje del cliente y realiza DOS tareas:

TAREA 1 - CLASIFICAR INTENCIÓN:
Clasifica el siguiente mensaje en UNA de estas intenciones:

{intentDefinitions}

Mensaje: ""{messageText}""

Responde SOLO con el nombre de la intención, sin explicaciones.

TAREA 2 - EXTRAER CONTEXTO:
Del mismo mensaje, extrae SOLO la información IMPORTANTE que debe guardarse para futuras conversaciones según las siguientes instrucciones:

{contextData}

Información del negocio (para contexto):
{generalInfo}

Reglas de planes (si aplica):
{planRules}

INSTRUCCIONES PARA EXTRAER CONTEXTO:
1. Analiza el mensaje cuidadosamente y extrae únicamente la información relevante según las instrucciones proporcionadas
2. Si el mensaje menciona edad (meses o años), extrae la edad y conviértela a meses si es necesario para mantener consistencia
3. Si existen reglas de planes y se menciona edad, determina el plan recomendado aplicando las reglas correspondientes
4. Extrae SOLO información que sea RELEVANTE y ÚTIL para futuras conversaciones, evitando datos temporales o irrelevantes
5. NO extraigas información temporal, superficial o que no aporte valor al contexto de la conversación
6. Formatea cada dato extraído como una oración natural y completa, fácil de entender
7. El contexto debe ser SOLO una lista de strings, donde cada string es una oración completa con información relevante extraída del mensaje

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

            migrationBuilder.Sql($@"
                IF NOT EXISTS (SELECT 1 FROM SystemConfigurations WHERE SystemConfigurationId = 7)
                BEGIN
                    INSERT INTO SystemConfigurations (SystemConfigurationId, Value, Description, IsActive, CreatedAt)
                    VALUES (7, N'{contextExtractionPromptValue.Replace("'", "''")}', N'Prompt unificado para clasificación de intención y extracción de contexto', 1, '{now:yyyy-MM-dd HH:mm:ss}');
                END
                ELSE
                BEGIN
                    UPDATE SystemConfigurations 
                    SET Value = N'{contextExtractionPromptValue.Replace("'", "''")}',
                        Description = N'Prompt unificado para clasificación de intención y extracción de contexto',
                        IsActive = 1
                    WHERE SystemConfigurationId = 7;
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MimosBabySpa.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReservationPromptsAndAvailabilityRequestIntent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var now = DateTime.UtcNow;
            
            // Prompt para extraer datos de reserva usando IA (Key: 11)
            var reservationDataExtractionPrompt = @"Eres un asistente especializado en extraer información de reservas desde mensajes de usuarios.

Reglas de reserva del negocio:
{reservationRules}

Contexto de la conversación:
{context}

Analiza el siguiente mensaje del usuario y extrae los datos necesarios para crear una reserva.

Responde SOLO con JSON válido en el siguiente formato:
{
  ""hasAllData"": true/false,
  ""message"": ""mensaje para el usuario si falta información"",
  ""reservationData"": {
    ""customerName"": ""nombre del cliente extraído"",
    ""serviceName"": ""servicio o plan extraído"",
    ""reservationDate"": ""YYYY-MM-DD"",
    ""reservationTime"": ""HH:mm"",
    ""durationMinutes"": 60
  }
}

Instrucciones:
- Si falta información, hasAllData debe ser false y message debe indicar qué falta de manera amigable
- Si tienes todos los datos, hasAllData debe ser true y reservationData debe contener los datos extraídos
- Las fechas deben estar en formato YYYY-MM-DD
- Las horas deben estar en formato HH:mm (24 horas)
- Si el usuario menciona ""mañana"", ""pasado mañana"", etc., calcula la fecha real
- Si el usuario menciona ""3pm"", ""3 de la tarde"", etc., convierte a formato 24 horas (15:00)
- La duración (durationMinutes) debe venir directamente del servicio/plan, NO la calcules. Si el servicio tiene una duración establecida, úsala. Si no está disponible, marca hasAllData como false.";

            // Prompt para detectar consultas de disponibilidad usando IA (Key: 12)
            var availabilityDetectionPrompt = @"Eres un analizador de intenciones. Determina si el siguiente mensaje del usuario es una consulta sobre disponibilidad de horarios para reservas.

Contexto de la conversación:
{context}

Mensaje del usuario: {messageText}

Responde SOLO con JSON válido:
{
  ""isAvailabilityQuery"": true/false
}

Considera como consulta de disponibilidad:
- Preguntas sobre horarios disponibles
- Preguntas sobre fechas libres
- Consultas sobre si hay espacio en una fecha/hora específica
- Preguntas sobre disponibilidad en general

NO consideres como consulta de disponibilidad:
- Solicitudes directas de reserva (ej: ""quiero reservar"")
- Preguntas sobre servicios o precios
- Saludos o conversación general";

            // Prompt para extraer fecha/hora de consultas de disponibilidad usando IA (Key: 13)
            var availabilityDataExtractionPrompt = @"Eres un asistente especializado en extraer fechas y horas de mensajes sobre disponibilidad.

Contexto de la conversación:
{context}

Mensaje del usuario: {messageText}

Analiza el mensaje y extrae la fecha y hora mencionadas (si las hay).

Responde SOLO con JSON válido:
{
  ""date"": ""YYYY-MM-DD"" o null si no hay fecha,
  ""time"": ""HH:mm"" o null si no hay hora,
  ""durationMinutes"": 60 (opcional, duración en minutos si se menciona)
}

Instrucciones:
- Si el usuario dice ""mañana"", calcula la fecha de mañana
- Si el usuario dice ""el 15 de febrero"", usa esa fecha
- Si el usuario dice ""a las 3pm"", convierte a formato 24 horas (15:00)
- Si no hay fecha u hora mencionada, retorna null
- Las fechas deben estar en formato YYYY-MM-DD
- Las horas deben estar en formato HH:mm (24 horas)";

            // Insertar ReservationDataExtractionPrompt (Key: 11)
            migrationBuilder.Sql($@"
                IF NOT EXISTS (SELECT 1 FROM SystemConfigurations WHERE SystemConfigurationId = 11)
                BEGIN
                    INSERT INTO SystemConfigurations (SystemConfigurationId, Value, Description, IsActive, CreatedAt)
                    VALUES (11, N'{reservationDataExtractionPrompt.Replace("'", "''")}', N'Prompt para extraer datos de reserva usando IA (reemplaza regex)', 1, '{now:yyyy-MM-dd HH:mm:ss}');
                END
                ELSE
                BEGIN
                    UPDATE SystemConfigurations 
                    SET Value = N'{reservationDataExtractionPrompt.Replace("'", "''")}',
                        Description = N'Prompt para extraer datos de reserva usando IA (reemplaza regex)',
                        IsActive = 1
                    WHERE SystemConfigurationId = 11;
                END
            ");

            // Insertar AvailabilityDetectionPrompt (Key: 12)
            migrationBuilder.Sql($@"
                IF NOT EXISTS (SELECT 1 FROM SystemConfigurations WHERE SystemConfigurationId = 12)
                BEGIN
                    INSERT INTO SystemConfigurations (SystemConfigurationId, Value, Description, IsActive, CreatedAt)
                    VALUES (12, N'{availabilityDetectionPrompt.Replace("'", "''")}', N'Prompt para detectar consultas de disponibilidad usando IA (reemplaza keywords)', 1, '{now:yyyy-MM-dd HH:mm:ss}');
                END
                ELSE
                BEGIN
                    UPDATE SystemConfigurations 
                    SET Value = N'{availabilityDetectionPrompt.Replace("'", "''")}',
                        Description = N'Prompt para detectar consultas de disponibilidad usando IA (reemplaza keywords)',
                        IsActive = 1
                    WHERE SystemConfigurationId = 12;
                END
            ");

            // Insertar AvailabilityDataExtractionPrompt (Key: 13)
            migrationBuilder.Sql($@"
                IF NOT EXISTS (SELECT 1 FROM SystemConfigurations WHERE SystemConfigurationId = 13)
                BEGIN
                    INSERT INTO SystemConfigurations (SystemConfigurationId, Value, Description, IsActive, CreatedAt)
                    VALUES (13, N'{availabilityDataExtractionPrompt.Replace("'", "''")}', N'Prompt para extraer fecha/hora de consultas de disponibilidad usando IA (reemplaza regex)', 1, '{now:yyyy-MM-dd HH:mm:ss}');
                END
                ELSE
                BEGIN
                    UPDATE SystemConfigurations 
                    SET Value = N'{availabilityDataExtractionPrompt.Replace("'", "''")}',
                        Description = N'Prompt para extraer fecha/hora de consultas de disponibilidad usando IA (reemplaza regex)',
                        IsActive = 1
                    WHERE SystemConfigurationId = 13;
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Eliminar los prompts agregados
            migrationBuilder.Sql(@"
                DELETE FROM SystemConfigurations WHERE SystemConfigurationId IN (11, 12, 13);
            ");
        }
    }
}

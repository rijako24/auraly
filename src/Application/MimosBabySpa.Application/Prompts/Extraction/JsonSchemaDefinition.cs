namespace MimosBabySpa.Application.Prompts.Extraction;

/// <summary>
/// Definición del JSON schema esperado en la respuesta del LLM.
/// </summary>
public static class JsonSchemaDefinition
{
    /// <summary>
    /// Schema completo de la respuesta esperada.
    /// </summary>
    public const string Schema = @"## JSON SCHEMA DE SALIDA:

```json
{
  ""extracted_fields"": [
    {
      ""field_name"": ""string (ej: Service, Attribute:BabyAge, DesiredDate, Attribute:BabyName)"",
      ""value"": ""string (valor extraído)"",
      ""field_type"": ""string (Text|Number|Date|Time|Email|Phone|Service)"",
      ""confidence"": 0.0-1.0,
      ""reasoning"": ""string (explicación breve de por qué extrajiste este valor)"",
      ""source_text"": ""string (fragmento exacto del mensaje del usuario)"",
      ""is_update"": false
    }
  ],
  ""conversational_response"": ""string (respuesta BREVE y natural al usuario - máximo 2-3 líneas)"",
  ""flow_analysis"": {
    ""user_requested_availability"": false,
    ""can_check_availability"": false,
    ""user_confirmed_booking"": false,
    ""confirmation_confidence"": 0.0,
    ""confirmation_indicators"": [],
    ""user_wants_to_cancel"": false,
    ""is_information_query"": false
  },
  ""ambiguities"": [
    {
      ""field_name"": ""string"",
      ""ambiguity_type"": ""temporal|referential|multiple_values|incomplete"",
      ""possible_values"": [""valor1"", ""valor2""],
      ""ambiguous_text"": ""string (texto ambiguo del usuario)"",
      ""clarification_question"": ""string (pregunta para aclarar)"",
      ""severity"": ""low|medium|high""
    }
  ],
  ""metadata"": {
    ""fields_extracted"": 0,
    ""average_confidence"": 0.0,
    ""is_complete"": false,
    ""needs_clarification"": false,
    ""detected_language"": ""es""
  }
}
```";
}

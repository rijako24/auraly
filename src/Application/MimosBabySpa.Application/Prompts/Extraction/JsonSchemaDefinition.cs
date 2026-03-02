namespace MimosBabySpa.Application.Prompts.Extraction;

/// <summary>
/// Definición del JSON schema esperado en la respuesta del LLM de extracción.
///
/// Diseño minimalista: solo los campos que el sistema realmente necesita.
/// El backend infiere field_type, calcula metadata, y genera la respuesta conversacional.
/// Menos tokens → más velocidad y menos ruido en la respuesta del LLM.
/// </summary>
public static class JsonSchemaDefinition
{
    /// <summary>
    /// Schema compacto. ~60 tokens vs ~200 del anterior.
    /// </summary>
    public const string Schema = @"## OUTPUT JSON (solo este formato, sin texto adicional):
{
  ""extracted_fields"": [
    { ""field_name"": ""string"", ""value"": ""string"", ""confidence"": 0.0 }
  ],
  ""intentions"": {
    ""user_requested_availability"": false,
    ""user_confirmed_booking"": false,
    ""is_information_query"": false,
    ""user_wants_to_cancel"": false,
    ""user_requests_new_payment_link"": false,
    ""user_says_already_paid"": false,
    ""user_wants_human_assistance"": false
  },
  ""ambiguities"": [
    { ""field_name"": ""string"", ""type"": ""temporal|referential|multiple_values|incomplete"", ""text"": ""string"" }
  ]
}";
}

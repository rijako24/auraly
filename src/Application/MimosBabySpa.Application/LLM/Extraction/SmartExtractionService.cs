using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Constants;
using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.LLM.Extraction;

/// <summary>
/// Pipeline de extracción con 3 niveles de resiliencia:
///   1. LLM (JSON Mode, temperatura 0.1)
///   2. Fallback determinístico (regex + patrones configurados por negocio)
///   3. Emergency (sin datos, solo mensaje de error)
///
/// IMPORTANTE:
/// - El LLM NO genera respuesta conversacional — la genera FASE 5 del orquestador.
/// - Multitenant: usa LoadedBusinessContext (precargado, sin queries adicionales).
/// - ExtractionOutput es el contrato hacia el orquestador: no hay mapeo externo.
/// </summary>
public class SmartExtractionService : ISmartExtractionService
{
    private readonly ILLMAdapter _llmAdapter;
    private readonly JsonSchemaPromptBuilder _promptBuilder;
    private readonly IExtractionValidator _validator;
    private readonly IFallbackExtractor _fallbackExtractor;
    private readonly ILogger<SmartExtractionService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new System.Text.Json.Serialization.JsonStringEnumConverter(
                JsonNamingPolicy.CamelCase, allowIntegerValues: false)
        }
    };

    public SmartExtractionService(
        ILLMAdapter llmAdapter,
        JsonSchemaPromptBuilder promptBuilder,
        IExtractionValidator validator,
        IFallbackExtractor fallbackExtractor,
        ILogger<SmartExtractionService> logger)
    {
        _llmAdapter       = llmAdapter;
        _promptBuilder    = promptBuilder;
        _validator        = validator;
        _fallbackExtractor = fallbackExtractor;
        _logger           = logger;
    }

    // ─────────────────────────────────────────────────────────────────
    // Punto de entrada
    // ─────────────────────────────────────────────────────────────────

    public async Task<ExtractionOutput> ExtractWithValidationAsync(
        string userMessage,
        ConversationState currentState,
        LoadedBusinessContext businessContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var llmResult = await ExtractWithLLMAsync(userMessage, currentState, businessContext, cancellationToken);
            var validation = await _validator.ValidateExtractionAsync(llmResult, userMessage, currentState);

            if (validation.IsValid && validation.Confidence >= ExtractionConstants.MinValidationConfidence)
            {
                _logger.LogInformation(
                    "✅ Extracción LLM: {Count} campos, avg confidence={Conf:F2}",
                    llmResult.ExtractedFields.Count, validation.Confidence);

                return ToOutput(llmResult, ExtractionMethod.LLM, success: true);
            }

            _logger.LogWarning(
                "⚠️ Extracción LLM inválida (confidence={Conf:F2}, issues={Issues}). Usando fallback.",
                validation.Confidence, string.Join("; ", validation.Issues));

            return await FallbackAsync(userMessage, currentState, businessContext, llmResult, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error en extracción — emergency fallback");
            return Emergency();
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Nivel 1: LLM
    // ─────────────────────────────────────────────────────────────────

    private async Task<StructuredExtractionResponse> ExtractWithLLMAsync(
        string userMessage,
        ConversationState currentState,
        LoadedBusinessContext businessContext,
        CancellationToken cancellationToken)
    {
        var prompt = await _promptBuilder.BuildExtractionPromptAsync(
            businessContext, userMessage, currentState, cancellationToken);

        var request = new LLMRequest
        {
            Messages = new List<LLMMessage>
            {
                new() { Role = LLMRole.System, Content = prompt },
                // El mensaje del usuario va como rol "user" — no en el system prompt
                new() { Role = LLMRole.User, Content = userMessage }
            },
            Temperature = 0.1f,  // Máximo determinismo
            MaxTokens   = 600    // Schema compacto → menos tokens de salida necesarios
        };

        var response = await _llmAdapter.SendWithJsonModeAsync(request, cancellationToken);

        if (!response.Success)
            throw new InvalidOperationException($"LLM falló: {response.ErrorMessage}");

        try
        {
            return DeserializeResponse(response.Content);
        }
        catch (JsonException)
        {
            var partial = TryParsePartialResponse(response.Content);
            if (partial != null)
            {
                _logger.LogWarning("JSON inválido — extracción parcial ({Count} campos)", partial.ExtractedFields.Count);
                return partial;
            }
            throw;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Nivel 2: Fallback determinístico
    // ─────────────────────────────────────────────────────────────────

    private async Task<ExtractionOutput> FallbackAsync(
        string userMessage,
        ConversationState currentState,
        LoadedBusinessContext businessContext,
        StructuredExtractionResponse? llmAttempt,
        CancellationToken cancellationToken)
    {
        var result = await _fallbackExtractor.ExtractAsync(
            userMessage, currentState, businessContext, llmAttempt, cancellationToken);

        _logger.LogInformation(
            "🔄 Fallback: {Count} campos extraídos",
            result.ExtractedFields.Count);

        return ToOutput(result, ExtractionMethod.Fallback, success: true);
    }

    // ─────────────────────────────────────────────────────────────────
    // Nivel 3: Emergency
    // ─────────────────────────────────────────────────────────────────

    private static ExtractionOutput Emergency()
    {
        return new ExtractionOutput
        {
            ExtractedFields              = new List<ExtractedField>(),
            Intentions                   = new ExtractionIntentions(),
            Ambiguities                  = new List<CompactAmbiguity>(),
            ConversationalResponseSuggestion = LocalizationConstants.ErrorMessages.TechnicalDifficulty,
            Method                       = ExtractionMethod.Emergency,
            WasSuccessful                = false
        };
    }

    // ─────────────────────────────────────────────────────────────────
    // Mapeo StructuredExtractionResponse → ExtractionOutput
    // ─────────────────────────────────────────────────────────────────

    private static ExtractionOutput ToOutput(
        StructuredExtractionResponse r,
        ExtractionMethod method,
        bool success)
    {
        // Inferir FieldType si no vino del LLM (schema nuevo no lo incluye)
        foreach (var field in r.ExtractedFields)
        {
            if (field.FieldType == default)
                field.FieldType = InferFieldType(field.FieldName, field.Value);
        }

        // Preferir "intentions" (schema nuevo) sobre "flow_analysis" (legacy)
        var intentions = r.Intentions ?? MergeFromLegacy(r.FlowAnalysisLegacy);

        return new ExtractionOutput
        {
            ExtractedFields  = r.ExtractedFields,
            Intentions       = intentions,
            Ambiguities      = r.Ambiguities,
            Method           = method,
            WasSuccessful    = success
        };
    }

    private static ExtractionIntentions MergeFromLegacy(FlowAnalysis? legacy) =>
        legacy == null
            ? new ExtractionIntentions()
            : new ExtractionIntentions
            {
                UserRequestedAvailability = legacy.UserRequestedAvailability,
                UserConfirmedBooking      = legacy.UserConfirmedBooking,
                IsInformationQuery        = legacy.IsInformationQuery,
                UserWantsToCancel         = legacy.UserWantsToCancel
            };

    // ─────────────────────────────────────────────────────────────────
    // Deserialización
    // ─────────────────────────────────────────────────────────────────

    private StructuredExtractionResponse DeserializeResponse(string jsonContent)
    {
        try
        {
            var structured = JsonSerializer.Deserialize<StructuredExtractionResponse>(jsonContent, JsonOptions);
            if (structured == null)
                throw new InvalidOperationException("LLM retornó JSON null");
            return structured;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Error deserializando JSON: {Json}", jsonContent);
            throw;
        }
    }

    /// <summary>
    /// Intenta extraer algo útil de un JSON parcialmente roto (resiliencia mínima).
    /// </summary>
    private StructuredExtractionResponse? TryParsePartialResponse(string jsonContent)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;

            var response = new StructuredExtractionResponse();

            // Extraer campos si existen
            if (root.TryGetProperty("extracted_fields", out var fields) && fields.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in fields.EnumerateArray())
                {
                    var name  = el.TryGetProperty("field_name", out var fn) ? fn.GetString() : null;
                    var value = el.TryGetProperty("value", out var v) ? v.GetString() : null;
                    var conf  = el.TryGetProperty("confidence", out var c) && c.TryGetDouble(out var d) ? d : 0.6;

                    if (!string.IsNullOrEmpty(name) && value != null)
                        response.ExtractedFields.Add(new ExtractedField
                        {
                            FieldName  = name!,
                            Value      = value,
                            Confidence = conf
                        });
                }
            }

            // Extraer intenciones si existen (soporta "intentions" y "flow_analysis")
            var intentProp = root.TryGetProperty("intentions", out var intentEl) ? intentEl
                           : root.TryGetProperty("flow_analysis", out var faEl)  ? faEl
                           : (JsonElement?)null;

            if (intentProp.HasValue)
            {
                var ip = intentProp.Value;
                response.Intentions = new ExtractionIntentions
                {
                    UserRequestedAvailability = GetBool(ip, "user_requested_availability"),
                    UserConfirmedBooking      = GetBool(ip, "user_confirmed_booking"),
                    IsInformationQuery        = GetBool(ip, "is_information_query"),
                    UserWantsToCancel         = GetBool(ip, "user_wants_to_cancel")
                };
            }

            return response.ExtractedFields.Any() || response.Intentions.UserConfirmedBooking
                ? response
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool GetBool(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.True;

    // ─────────────────────────────────────────────────────────────────
    // InferFieldType — inferencia local de tipo (no depende del LLM)
    // ─────────────────────────────────────────────────────────────────

    private static FieldType InferFieldType(string fieldName, string value)
    {
        var lower = fieldName.ToLowerInvariant();

        if (lower.Contains("date") || lower.Contains("fecha"))   return FieldType.Date;
        if (lower.Contains("time") || lower.Contains("hora"))    return FieldType.Time;
        if (lower.Contains("email") || lower.Contains("correo")) return FieldType.Email;
        if (lower.Contains("phone") || lower.Contains("telefono") || lower.Contains("celular")) return FieldType.Phone;
        if (lower.Contains("service") || lower.Contains("servicio")) return FieldType.Service;
        if (lower.Contains("age") || lower.Contains("edad") || lower.Contains("meses")) return FieldType.Number;

        // Por valor
        if (System.Text.RegularExpressions.Regex.IsMatch(value, @"^\d{4}-\d{2}-\d{2}$")) return FieldType.Date;
        if (System.Text.RegularExpressions.Regex.IsMatch(value, @"^\d{2}:\d{2}$"))        return FieldType.Time;
        if (System.Text.RegularExpressions.Regex.IsMatch(value, @"^\d+$"))                 return FieldType.Number;

        return FieldType.Text;
    }
}

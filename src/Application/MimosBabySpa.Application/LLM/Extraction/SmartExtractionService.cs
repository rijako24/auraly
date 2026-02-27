using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Constants;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.LLM.Extraction;

/// <summary>
/// Pipeline de extracción con 3 niveles de resiliencia:
///   1. LLM (JSON Mode, temperatura 0.1) con historial conversacional para contexto
///   2. Fallback determinístico (regex + patrones configurados por negocio)
///   3. Emergency (sin datos, solo mensaje de error)
///
/// IMPORTANTE:
/// - El historial reciente se pasa como mensajes user/assistant — el LLM interpreta
///   respuestas como "2" en contexto de "¿Para cuántos bebés?"
/// - Multitenant: usa LoadedBusinessContext (precargado, sin queries adicionales).
/// - ExtractionOutput es el contrato hacia el orquestador: no hay mapeo externo.
/// </summary>
public class SmartExtractionService : ISmartExtractionService
{
    private const int MaxHistoryMessagesForExtraction = 6;

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
        _llmAdapter        = llmAdapter;
        _promptBuilder     = promptBuilder;
        _validator         = validator;
        _fallbackExtractor = fallbackExtractor;
        _logger            = logger;
    }

    // ─────────────────────────────────────────────────────────────────
    // Punto de entrada
    // ─────────────────────────────────────────────────────────────────

    public async Task<ExtractionOutput> ExtractWithValidationAsync(
        string userMessage,
        ConversationState currentState,
        LoadedBusinessContext businessContext,
        IReadOnlyList<Message> recentHistory,
        CancellationToken cancellationToken)
    {
        try
        {
            var llmResult = await ExtractWithLLMAsync(
                userMessage, currentState, businessContext, recentHistory, cancellationToken);

            // Normalización de frontera LLM: "N/A" significa "campo sin valor", no error.
            // Evita que el validator rechace extracciones válidas (ej. DesiredTime cuando el usuario solo preguntó por fecha).
            llmResult.ExtractedFields.RemoveAll(f =>
                string.Equals(f.Value?.Trim(), "N/A", StringComparison.OrdinalIgnoreCase));

            var validation = await _validator.ValidateExtractionAsync(llmResult, userMessage, currentState);

            if (validation.IsValid && validation.Confidence >= ExtractionConstants.MinValidationConfidence)
            {
                _logger.LogInformation(
                    "✅ Extracción: {Count} campos, avg confidence={Conf:F2}",
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
        IReadOnlyList<Message> recentHistory,
        CancellationToken cancellationToken)
    {
        var prompt = await _promptBuilder.BuildExtractionPromptAsync(
            businessContext, userMessage, currentState, cancellationToken);

        var messages = new List<LLMMessage>
        {
            new() { Role = LLMRole.System, Content = prompt }
        };

        var historySlice = recentHistory
            .OrderBy(m => m.Timestamp)
            .TakeLast(MaxHistoryMessagesForExtraction)
            .ToList();

        foreach (var msg in historySlice)
        {
            var role = msg.Sender.Equals("User", StringComparison.OrdinalIgnoreCase)
                ? LLMRole.User
                : LLMRole.Assistant;
            messages.Add(new() { Role = role, Content = msg.MessageText });
        }

        const string msgDelimiter = "---MENSAJE A ANALIZAR---";
        messages.Add(new() { Role = LLMRole.User, Content = $"{msgDelimiter}\n{userMessage}\n{msgDelimiter}" });

        var request = new LLMRequest
        {
            Messages    = messages,
            Temperature = 0.1f,
            MaxTokens   = 600
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
        // Separar campos de datos de intenciones que el LLM pudo haber duplicado
        // en extracted_fields. El criterio de filtrado viene de ExtractionIntentions.JsonPropertyNames
        // (fuente de verdad única — derivada por reflexión del propio tipo).
        var dataFields = r.ExtractedFields
            .Where(f => !ExtractionIntentions.JsonPropertyNames.Contains(f.FieldName))
            .ToList();

        // Inferir FieldType si no vino del LLM (schema nuevo no lo incluye)
        foreach (var field in dataFields)
        {
            if (field.FieldType == default)
                field.FieldType = InferFieldType(field.FieldName, field.Value);
        }

        // Preferir "intentions" (schema nuevo) sobre "flow_analysis" (legacy)
        var intentions = r.Intentions ?? MergeFromLegacy(r.FlowAnalysisLegacy);

        return new ExtractionOutput
        {
            ExtractedFields  = dataFields,
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
                UserRequestedAvailability     = legacy.UserRequestedAvailability,
                UserConfirmedBooking          = legacy.UserConfirmedBooking,
                IsInformationQuery            = legacy.IsInformationQuery,
                UserWantsToCancel             = legacy.UserWantsToCancel,
                UserRequestsNewPaymentLink    = legacy.UserRequestsNewPaymentLink,
                UserSaysAlreadyPaid           = legacy.UserSaysAlreadyPaid
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
                    var value = el.TryGetProperty("value", out var v) ? FlexibleStringJsonConverter.FromJsonElement(v) : null;
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
                    UserRequestedAvailability   = GetBool(ip, "user_requested_availability"),
                    UserConfirmedBooking       = GetBool(ip, "user_confirmed_booking"),
                    IsInformationQuery         = GetBool(ip, "is_information_query"),
                    UserWantsToCancel          = GetBool(ip, "user_wants_to_cancel"),
                    UserRequestsNewPaymentLink  = GetBool(ip, "user_requests_new_payment_link"),
                    UserSaysAlreadyPaid         = GetBool(ip, "user_says_already_paid")
                };
            }

            return response.ExtractedFields.Any()
                || response.Intentions.UserConfirmedBooking
                || response.Intentions.UserRequestsNewPaymentLink
                || response.Intentions.UserSaysAlreadyPaid
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

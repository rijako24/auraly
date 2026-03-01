using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.LLM.Extraction;

/// <summary>
/// Pipeline de extracción con 2 resultados: LLM (éxito) o Degraded (fallo).
///
/// Diseño para ventas críticas:
/// - Solo aplicamos datos extraídos cuando el LLM responde Y la validación aprueba.
/// - Cualquier fallo → turno degradado, sin mutar estado con datos no confiables.
/// - Solo intenciones críticas (UserWantsHumanAssistance, UserWantsToCancel) se preservan via regex determinístico.
/// </summary>
public class SmartExtractionService : ISmartExtractionService
{
    private const int MaxHistoryMessagesForExtraction = 6;

    private readonly ILLMAdapter _llmAdapter;
    private readonly JsonSchemaPromptBuilder _promptBuilder;
    private readonly IExtractionValidator _validator;
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
        ILogger<SmartExtractionService> logger)
    {
        _llmAdapter = llmAdapter;
        _promptBuilder = promptBuilder;
        _validator = validator;
        _logger = logger;
    }

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

            llmResult.ExtractedFields.RemoveAll(f =>
                string.Equals(f.Value?.Trim(), "N/A", StringComparison.OrdinalIgnoreCase));

            var validation = await _validator.ValidateExtractionAsync(llmResult, userMessage, currentState);

            if (validation.IsValid && validation.Confidence >= ExtractionConstants.MinValidationConfidence)
            {
                _logger.LogInformation(
                    "Extracción: {Count} campos, confidence={Conf:F2}",
                    llmResult.ExtractedFields.Count, validation.Confidence);

                return ToOutput(llmResult, ExtractionMethod.LLM, success: true);
            }

            _logger.LogWarning(
                "Extracción LLM inválida (confidence={Conf:F2}). Turno degradado.",
                validation.Confidence);

            return Degraded(userMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en extracción — turno degradado");
            return Degraded(userMessage);
        }
    }

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
            Messages = messages,
            Temperature = 0.1f,
            MaxTokens = 600
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
                _logger.LogWarning("JSON inválido — intentando validar extracción parcial ({Count} campos)", partial.ExtractedFields.Count);
                return partial;
            }
            throw;
        }
    }

    /// <summary>
    /// Solo intenciones inequívocas via regex. No extrae campos — no confiables sin LLM.
    /// </summary>
    private static ExtractionOutput Degraded(string userMessage)
    {
        var intentions = DetectCriticalIntentions(userMessage);

        return new ExtractionOutput
        {
            ExtractedFields = new(),
            Intentions = intentions,
            Ambiguities = new(),
            Method = ExtractionMethod.Degraded,
            WasSuccessful = false
        };
    }

    /// <summary>
    /// Regex determinístico para intenciones que no admiten ambigüedad.
    /// UserConfirmedBooking NO se incluye — "sí, pero..." puede crear reservas incorrectas.
    /// </summary>
    private static ExtractionIntentions DetectCriticalIntentions(string userMessage)
    {
        var lower = userMessage.ToLowerInvariant();
        return new ExtractionIntentions
        {
            UserWantsHumanAssistance = Regex.IsMatch(lower,
                @"\b(hablar con (un |una )?(humano|persona|agente|asesor)|pasar(me)? con (alguien|una persona|un agente)|necesito (un |una )?(humano|persona|agente)|quiero (un |una )?(humano|persona|agente))\b"),
            UserWantsToCancel = Regex.IsMatch(lower,
                @"\b(cancel|mejor no|cambié de opinión|no quiero)\b")
        };
    }

    private static ExtractionOutput ToOutput(
        StructuredExtractionResponse r,
        ExtractionMethod method,
        bool success)
    {
        var dataFields = r.ExtractedFields
            .Where(f => !ExtractionIntentions.JsonPropertyNames.Contains(f.FieldName))
            .ToList();

        foreach (var field in dataFields)
        {
            if (field.FieldType == default)
                field.FieldType = InferFieldType(field.FieldName, field.Value);
        }

        var intentions = r.Intentions ?? MergeFromLegacy(r.FlowAnalysisLegacy);

        return new ExtractionOutput
        {
            ExtractedFields = dataFields,
            Intentions = intentions,
            Ambiguities = r.Ambiguities,
            Method = method,
            WasSuccessful = success
        };
    }

    private static ExtractionIntentions MergeFromLegacy(FlowAnalysis? legacy) =>
        legacy == null
            ? new ExtractionIntentions()
            : new ExtractionIntentions
            {
                UserRequestedAvailability = legacy.UserRequestedAvailability,
                UserConfirmedBooking = legacy.UserConfirmedBooking,
                IsInformationQuery = legacy.IsInformationQuery,
                UserWantsToCancel = legacy.UserWantsToCancel,
                UserRequestsNewPaymentLink = legacy.UserRequestsNewPaymentLink,
                UserSaysAlreadyPaid = legacy.UserSaysAlreadyPaid
            };

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

    private StructuredExtractionResponse? TryParsePartialResponse(string jsonContent)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;

            var response = new StructuredExtractionResponse();

            if (root.TryGetProperty("extracted_fields", out var fields) && fields.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in fields.EnumerateArray())
                {
                    var name = el.TryGetProperty("field_name", out var fn) ? fn.GetString() : null;
                    var value = el.TryGetProperty("value", out var v) ? FlexibleStringJsonConverter.FromJsonElement(v) : null;
                    var conf = el.TryGetProperty("confidence", out var c) && c.TryGetDouble(out var d) ? d : 0.6;

                    if (!string.IsNullOrEmpty(name) && value != null)
                        response.ExtractedFields.Add(new ExtractedField
                        {
                            FieldName = name!,
                            Value = value,
                            Confidence = conf
                        });
                }
            }

            var intentProp = root.TryGetProperty("intentions", out var intentEl) ? intentEl
                : root.TryGetProperty("flow_analysis", out var faEl) ? faEl
                : (JsonElement?)null;

            if (intentProp.HasValue)
            {
                var ip = intentProp.Value;
                response.Intentions = new ExtractionIntentions
                {
                    UserRequestedAvailability = GetBool(ip, "user_requested_availability"),
                    UserConfirmedBooking = GetBool(ip, "user_confirmed_booking"),
                    IsInformationQuery = GetBool(ip, "is_information_query"),
                    UserWantsToCancel = GetBool(ip, "user_wants_to_cancel"),
                    UserRequestsNewPaymentLink = GetBool(ip, "user_requests_new_payment_link"),
                    UserSaysAlreadyPaid = GetBool(ip, "user_says_already_paid"),
                    UserWantsHumanAssistance = GetBool(ip, "user_wants_human_assistance")
                };
            }

            return response.ExtractedFields.Any()
                || response.Intentions.UserConfirmedBooking
                || response.Intentions.UserRequestsNewPaymentLink
                || response.Intentions.UserSaysAlreadyPaid
                || response.Intentions.UserWantsHumanAssistance
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

    private static FieldType InferFieldType(string fieldName, string value)
    {
        var lower = fieldName.ToLowerInvariant();

        if (lower.Contains("date") || lower.Contains("fecha")) return FieldType.Date;
        if (lower.Contains("time") || lower.Contains("hora")) return FieldType.Time;
        if (lower.Contains("email") || lower.Contains("correo")) return FieldType.Email;
        if (lower.Contains("phone") || lower.Contains("telefono") || lower.Contains("celular")) return FieldType.Phone;
        if (lower.Contains("service") || lower.Contains("servicio")) return FieldType.Service;
        if (lower.Contains("age") || lower.Contains("edad") || lower.Contains("meses")) return FieldType.Number;

        if (Regex.IsMatch(value, @"^\d{4}-\d{2}-\d{2}$")) return FieldType.Date;
        if (Regex.IsMatch(value, @"^\d{2}:\d{2}$")) return FieldType.Time;
        if (Regex.IsMatch(value, @"^\d+$")) return FieldType.Number;

        return FieldType.Text;
    }
}

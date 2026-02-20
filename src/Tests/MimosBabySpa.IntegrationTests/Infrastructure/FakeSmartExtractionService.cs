using System.Text.Json;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.LLM;
using MimosBabySpa.Application.LLM.Extraction;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.IntegrationTests.Infrastructure;

/// <summary>
/// Fake ISmartExtractionService: parses the JSON returned by FakeLLMAdapter.SendWithJsonModeAsync
/// and converts it to ExtractionOutput. Handles both "field" and "field_name" keys for flexibility.
/// </summary>
public class FakeSmartExtractionService : ISmartExtractionService
{
    private readonly ILLMAdapter _llmAdapter;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public FakeSmartExtractionService(ILLMAdapter llmAdapter)
    {
        _llmAdapter = llmAdapter;
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
            var request = new LLMRequest
            {
                Messages    = [new LLMMessage { Role = LLMRole.User, Content = userMessage }],
                Temperature = 0.1f,
                MaxTokens   = 600
            };

            var response = await _llmAdapter.SendWithJsonModeAsync(request, cancellationToken);
            return ParseJson(response.Content);
        }
        catch
        {
            return Empty();
        }
    }

    private static ExtractionOutput ParseJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var fields = new List<ExtractedField>();

            if (root.TryGetProperty("extracted_fields", out var arr) &&
                arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in arr.EnumerateArray())
                {
                    // Support both "field_name" and "field" (scenario compact notation)
                    var name  = el.TryGetProperty("field_name", out var fn) ? fn.GetString()
                              : el.TryGetProperty("field", out var f)       ? f.GetString()
                              : null;
                    var value = el.TryGetProperty("value", out var v)       ? v.GetString() : null;
                    var conf  = el.TryGetProperty("confidence", out var c) && c.TryGetDouble(out var d) ? d : 0.9;

                    if (name != null && value != null)
                        fields.Add(new ExtractedField { FieldName = name, Value = value, Confidence = conf });
                }
            }

            var intentions = new ExtractionIntentions();
            if (root.TryGetProperty("intentions", out var ip))
            {
                intentions.UserRequestedAvailability = GetBool(ip, "user_requested_availability");
                intentions.UserConfirmedBooking      = GetBool(ip, "user_confirmed_booking");
                intentions.IsInformationQuery        = GetBool(ip, "is_information_query");
                intentions.UserWantsToCancel         = GetBool(ip, "user_wants_to_cancel");
            }

            return new ExtractionOutput
            {
                ExtractedFields = fields,
                Intentions      = intentions,
                Ambiguities     = [],
                Method          = ExtractionMethod.LLM,
                WasSuccessful   = true
            };
        }
        catch
        {
            return Empty();
        }
    }

    private static bool GetBool(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.True;

    private static ExtractionOutput Empty() => new()
    {
        ExtractedFields = [],
        Intentions      = new ExtractionIntentions(),
        Ambiguities     = [],
        Method          = ExtractionMethod.Emergency,
        WasSuccessful   = false
    };
}

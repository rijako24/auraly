using System.Text.Json.Serialization;

namespace MimosBabySpa.Application.LLM.Extraction;

/// <summary>
/// Análisis del flujo de conversación
/// </summary>
public class FlowAnalysis
{
    [JsonPropertyName("user_requested_availability")]
    public bool UserRequestedAvailability { get; set; }

    [JsonPropertyName("can_check_availability")]
    public bool CanCheckAvailability { get; set; }

    [JsonPropertyName("user_confirmed_booking")]
    public bool UserConfirmedBooking { get; set; }

    [JsonPropertyName("confirmation_confidence")]
    public double ConfirmationConfidence { get; set; }

    [JsonPropertyName("confirmation_indicators")]
    public List<string> ConfirmationIndicators { get; set; } = new();

    [JsonPropertyName("user_wants_to_cancel")]
    public bool UserWantsToCancel { get; set; }

    [JsonPropertyName("is_information_query")]
    public bool IsInformationQuery { get; set; }
}

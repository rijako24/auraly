using System.Text.Json.Serialization;

namespace MimosBabySpa.Application.LLM.Extraction;

/// <summary>
/// Intenciones detectadas del texto del usuario por el LLM.
///
/// El LLM SOLO detecta intenciones expresadas en el texto.
/// CanCheckAvailability y CanCreateReservation los decide el FlowEngine, no el LLM.
///
/// Usado como campo "intentions" en el schema de extracción y como salida
/// del pipeline hacia el orquestador.
/// </summary>
public class ExtractionIntentions
{
    [JsonPropertyName("user_requested_availability")]
    public bool UserRequestedAvailability { get; set; }

    [JsonPropertyName("user_confirmed_booking")]
    public bool UserConfirmedBooking { get; set; }

    [JsonPropertyName("is_information_query")]
    public bool IsInformationQuery { get; set; }

    [JsonPropertyName("user_wants_to_cancel")]
    public bool UserWantsToCancel { get; set; }
}

using System.Reflection;
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
///
/// JsonPropertyNames expone los nombres JSON de todas las propiedades de esta clase,
/// calculados una sola vez por reflexión al cargar el tipo. Cualquier componente que
/// necesite identificar si un nombre de campo pertenece a este contrato debe usar
/// ese set — nunca duplicar los strings.
/// </summary>
public class ExtractionIntentions
{
    /// <summary>
    /// Nombres JSON de todas las intenciones, derivados de los atributos [JsonPropertyName]
    /// de esta clase. Computado una vez (estático) y expuesto para que los consumidores
    /// puedan filtrar sin duplicar strings.
    /// </summary>
    public static readonly IReadOnlySet<string> JsonPropertyNames =
        typeof(ExtractionIntentions)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name)
            .Where(name => name is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)!;

    [JsonPropertyName("user_requested_availability")]
    public bool UserRequestedAvailability { get; set; }

    [JsonPropertyName("user_confirmed_booking")]
    public bool UserConfirmedBooking { get; set; }

    [JsonPropertyName("is_information_query")]
    public bool IsInformationQuery { get; set; }

    [JsonPropertyName("user_wants_to_cancel")]
    public bool UserWantsToCancel { get; set; }

    [JsonPropertyName("user_requests_new_payment_link")]
    public bool UserRequestsNewPaymentLink { get; set; }

    [JsonPropertyName("user_says_already_paid")]
    public bool UserSaysAlreadyPaid { get; set; }

    [JsonPropertyName("user_wants_human_assistance")]
    public bool UserWantsHumanAssistance { get; set; }

    [JsonPropertyName("user_wants_to_reschedule")]
    public bool UserWantsToReschedule { get; set; }

    [JsonPropertyName("user_wants_to_hold")]
    public bool UserWantsToHold { get; set; }
}

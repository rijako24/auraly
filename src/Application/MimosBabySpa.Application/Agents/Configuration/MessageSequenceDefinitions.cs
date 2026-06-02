namespace MimosBabySpa.Application.Agents.Configuration;

/// <summary>
/// Catálogo de secuencias nombradas de mensajes outbound (texto + adjuntos opcionales).
/// Fuente: Agents.SettingsJson → messageSequences.
/// </summary>
public sealed class MessageSequenceCatalog : Dictionary<string, MessageSequence>
{
}

/// <summary>
/// Secuencia ordenada de pasos a enviar por WhatsApp.
/// </summary>
public sealed class MessageSequence
{
    public List<MessageSequenceStep> Messages { get; set; } = [];
}

/// <summary>
/// Un paso de la secuencia: cuerpo de texto y adjunto opcional (BusinessAttachments).
/// </summary>
public sealed class MessageSequenceStep
{
    public string? Body { get; set; }

    public Guid? AttachmentId { get; set; }
}

/// <summary>
/// Disparadores de secuencias por webhook externo.
/// Fuente: Agents.SettingsJson → webhooks.
/// </summary>
public sealed class WebhookDefinitions
{
    public Dictionary<string, WompiWebhookOutcomeConfig>? Wompi { get; set; }
}

/// <summary>
/// Outcome de webhook Wompi → nombre de secuencia en messageSequences.
/// </summary>
public sealed class WompiWebhookOutcomeConfig
{
    public string? SendMessageSequence { get; set; }
}

/// <summary>
/// Claves de outcome Wompi soportadas en SettingsJson → webhooks.wompi.
/// </summary>
public static class WompiWebhookOutcomes
{
    public const string ReservationCreated = "reservation_created";
    public const string SlotUnavailableAfterPayment = "slot_unavailable_after_payment";
}

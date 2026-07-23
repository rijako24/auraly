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
    public string Type { get; set; } = "text";

    public string? Body { get; set; }

    public Guid? AttachmentId { get; set; }

    public List<MessageSequenceButton> Buttons { get; set; } = [];

    public string? TemplateName { get; set; }

    public string? Language { get; set; }

    public List<string> HeaderParameters { get; set; } = [];

    public List<string> BodyParameters { get; set; } = [];
}

public sealed class MessageSequenceButton
{
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
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

/// <summary>
/// Disparadores internos del motor para notificaciones outbound.
/// Fuente: Agents.SettingsJson -> notifications.
/// </summary>
public sealed class NotificationDefinitions : Dictionary<string, EventNotificationConfig>
{
    public NotificationDefinitions()
        : base(StringComparer.OrdinalIgnoreCase)
    {
    }
}

public sealed class ExternalEscalationDefinitions
{
    public bool Enabled { get; set; }

    public Dictionary<string, ExternalEscalationEventDefinition> Events { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class EscalationDefinitions
{
    public HumanEscalationDefinitions Human { get; set; } = new();

    public ExternalEscalationDefinitions External { get; set; } = new();
}

public sealed class HumanEscalationDefinitions
{
    public IReadOnlyList<string> Contacts { get; set; } = [];
}

public sealed class ExternalEscalationEventDefinition
{
    public bool Enabled { get; set; }
public int AttemptTimeoutMinutes { get; set; } = 5;

    public string AttemptCodePrefix { get; set; } = "EXT";

    public string? SendMessageSequence { get; set; }

    public string ContactType { get; set; } = string.Empty;

    public string PickupAddress { get; set; } = string.Empty;

    public Dictionary<string, string> OutcomeEvents { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ExternalEscalationContactDefinition> Contacts { get; set; } = [];
}

public sealed class ExternalEscalationContactDefinition
{
    public int Priority { get; set; }

    public Guid? BusinessInboundContactId { get; set; }

    public string PickupAddress { get; set; } = string.Empty;
}

public sealed class EventNotificationConfig
{
    public bool Enabled { get; set; }

    public List<EventNotificationDeliveryConfig> Deliveries { get; set; } = [];
}

public sealed class EventNotificationDeliveryConfig
{
    public string Id { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public IReadOnlyList<string> Recipients { get; set; } = [];

    public string? SendMessageSequence { get; set; }
}

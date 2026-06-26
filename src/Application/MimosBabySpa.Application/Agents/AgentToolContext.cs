using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.Agents;

/// <summary>
/// Contexto de sesión inyectado a cada tool en el turno.
/// Facts, reservas gestionables y pago activo se cargan al inicio y mutan durante el turno.
/// </summary>
public sealed class AgentToolContext
{
    public Guid AgentId { get; init; }
    public Guid BusinessId { get; init; }
    public Guid ConversationId { get; init; }
    public DateOnly BusinessToday { get; init; }
    public DateTimeOffset BusinessNow { get; init; }
    public bool BusinessDayRollover { get; init; }
    public DateOnly? PreviousBusinessDay { get; init; }
    public IReadOnlyList<string> RolloverClearedFacts { get; init; } = [];
    public string ChannelPhone { get; init; } = string.Empty;
    public string LatestUserMessage { get; set; } = string.Empty;
    public string? ProviderMessageId { get; init; }
    public string? ReplyToProviderMessageId { get; init; }
    public string? InteractivePayload { get; init; }
    public IReadOnlyList<string> EscalationContacts { get; init; } = [];
    public int CurrentToolIteration { get; set; }

    public AgentConfig? Config { get; set; }

    public ConversationState ConversationState { get; init; } = null!;
    public Conversation Conversation { get; init; } = null!;
    public Dictionary<string, string> Facts { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, MessageSequenceContext> NotificationContexts { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Citas confirmadas o en espera del cliente en este turno (conversación actual o teléfono del canal).
    /// </summary>
    public IReadOnlyList<Reservation> ManageableReservations { get; set; } = [];

    /// <summary>Única cita gestionable, si la lista tiene exactamente un elemento.</summary>
    public Reservation? SingleManageableReservation =>
        ManageableReservations.Count == 1 ? ManageableReservations[0] : null;

    public PaymentTransaction? ActivePayment { get; set; }

    /// <summary>Politicas efimeras calculadas para el turno actual.</summary>
    public OperatingHoursTurnContext OperatingHours { get; set; } = OperatingHoursTurnContext.Disabled;

    /// <summary>Estado del turno actual (fragmentos, tokens, flags).</summary>
    internal AgentTurnExecution? Turn { get; set; }
}

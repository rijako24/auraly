using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.Agents;

/// <summary>
/// Contexto de sesión inyectado a cada tool en el turno.
/// Facts, reserva activa y pago activo se cargan al inicio y mutan durante el turno.
/// </summary>
public sealed class AgentToolContext
{
    public Guid AgentId { get; init; }
    public Guid BusinessId { get; init; }
    public Guid ConversationId { get; init; }
    public DateOnly BusinessToday { get; init; }
    public DateTimeOffset BusinessNow { get; init; }
    public string ChannelPhone { get; init; } = string.Empty;
    public IReadOnlyList<string> EscalationContacts { get; init; } = [];
    public int CurrentToolIteration { get; set; }

    public ConversationState ConversationState { get; init; } = null!;
    public Conversation Conversation { get; init; } = null!;
    public Dictionary<string, string> Facts { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Reservation? ActiveReservation { get; set; }
    public PaymentTransaction? ActivePayment { get; set; }

    /// <summary>Estado del turno actual (fragmentos, tokens, flags).</summary>
    internal AgentTurnExecution? Turn { get; set; }
}

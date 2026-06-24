namespace MimosBabySpa.Application.Agents;

/// <summary>
/// Mensaje outbound encolado durante un turno del agente.
/// El processor envía la respuesta principal primero y luego estos mensajes en orden.
/// </summary>
public sealed record OutboundMessage(
    string? Body,
    string? MediaUrl,
    string MediaType = "text",
    string? Filename = null,
    IReadOnlyList<OutboundButton>? Buttons = null,
    WhatsAppTemplateMessage? Template = null);

public sealed record OutboundButton(string Id, string Title);

public sealed record WhatsAppTemplateMessage(
    string Name,
    string LanguageCode,
    IReadOnlyList<string> HeaderParameters,
    IReadOnlyList<string> BodyParameters);

/// <summary>
/// Contexto opcional para resolver placeholders de secuencias (reserva, pago, etc.).
/// </summary>
public sealed record MessageSequenceContext
{
    public Domain.Entities.Reservation? Reservation { get; init; }

    public PaymentSequenceContext? Payment { get; init; }

    public IReadOnlyDictionary<string, string> Custom { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Placeholders de secuencias disparadas por webhook de pago (slot tomado, recibo, etc.).
/// </summary>
public sealed class PaymentSequenceContext
{
    public decimal Amount { get; init; }

    public string Currency { get; init; } = string.Empty;

    public string? OriginalTime { get; init; }

    public IReadOnlyList<string> AvailableSlots { get; init; } = [];
}

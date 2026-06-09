namespace MimosBabySpa.Application.Services;

/// <summary>
/// Notificador de escalado a humano. Responsabilidad única: enviar WhatsApp a admins.
///
/// Los contactos se reciben como parámetro desde el nodo Escalate (config del flow),
/// no se leen de ninguna tabla BusinessConfigurations.
/// </summary>
public interface IEscalationNotifier
{
    /// <summary>
    /// Notifica a los contactos indicados. Intenta todos — no aborta en el primer fallo.
    /// Retorna true si al menos uno recibió la notificación.
    /// </summary>
    Task<bool> NotifyAsync(
        Guid businessId,
        IReadOnlyList<string> contacts,
        EscalationNotification notification,
        CancellationToken ct = default);
}

/// <summary>
/// Datos de la notificación de escalado.
/// PaymentReferenceId: cuando se escala por error de link de pago, permite al admin confirmar el pago manualmente.
/// </summary>
public record EscalationNotification(
    Guid ConversationId,
    string CustomerPhone,
    string Reason,
    string? LastUserMessage = null,
    string? PaymentReferenceId = null);


namespace MimosBabySpa.Application.Services;

/// <summary>
/// Notificador de escalado a humano. Responsabilidad Ãºnica: enviar WhatsApp a admins.
///
/// Los contactos se reciben como parÃ¡metro desde el nodo Escalate (config del flow),
/// no se leen de ninguna tabla configuraciones legacy.
/// </summary>
public interface IEscalationNotifier
{
    /// <summary>
    /// Notifica a los contactos indicados. Intenta todos â€” no aborta en el primer fallo.
    /// Retorna true si al menos uno recibiÃ³ la notificaciÃ³n.
    /// </summary>
    Task<bool> NotifyAsync(
        Guid businessId,
        IReadOnlyList<string> contacts,
        EscalationNotification notification,
        CancellationToken ct = default);
}

/// <summary>
/// Datos de la notificaciÃ³n de escalado.
/// </summary>
public record EscalationNotification(
    Guid ConversationId,
    string CustomerPhone,
    string Reason,
    string? LastUserMessage = null);



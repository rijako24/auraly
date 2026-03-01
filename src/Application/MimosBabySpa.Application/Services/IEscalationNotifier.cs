namespace MimosBabySpa.Application.Services;

/// <summary>
/// Notificador de escalado a humano. Responsabilidad única: enviar WhatsApp a admins.
/// No tiene tracking, threshold ni decisión de negocio — eso lo hace el orquestador.
/// </summary>
public interface IEscalationNotifier
{
    /// <summary>
    /// Notifica a los admins configurados del negocio. Intenta todos los contactos.
    /// Retorna true si al menos uno recibió la notificación.
    /// </summary>
    Task<bool> NotifyAdminsAsync(
        Guid businessId,
        EscalationNotification notification,
        CancellationToken ct = default);
}

/// <summary>
/// Datos de la notificación de escalado.
/// </summary>
public record EscalationNotification(
    Guid ConversationId,
    string CustomerPhone,
    string Reason,
    string? LastUserMessage = null);


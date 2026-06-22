using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// EnvÃ­a notificaciones de escalado a admins vÃ­a WhatsApp.
///
/// Los contactos se reciben como parÃ¡metro â€” no se lee ninguna clave de
/// configuraciones legacy. El nodo Escalate en el flow JSON es quien los provee.
/// </summary>
public class EscalationNotifier : IEscalationNotifier
{
    private readonly IWhatsAppService _whatsAppService;
    private readonly IAdminActionLinkService _adminActionLinkService;
    private readonly ILogger<EscalationNotifier> _logger;

    public EscalationNotifier(
        IWhatsAppService whatsAppService,
        IAdminActionLinkService adminActionLinkService,
        ILogger<EscalationNotifier> logger)
    {
        _whatsAppService = whatsAppService;
        _adminActionLinkService = adminActionLinkService;
        _logger = logger;
    }

    public async Task<bool> NotifyAsync(
        Guid businessId,
        IReadOnlyList<string> contacts,
        EscalationNotification notification,
        CancellationToken ct = default)
    {
        if (contacts.Count == 0)
        {
            _logger.LogInformation(
                "Sin contactos de escalado para Biz={BusinessId}", businessId);
            return false;
        }

        var lastMsg = string.IsNullOrWhiteSpace(notification.LastUserMessage)
            ? "â€”"
            : notification.LastUserMessage.Length > 80
                ? notification.LastUserMessage[..80] + "..."
                : notification.LastUserMessage;

        var releaseUrl = _adminActionLinkService.GenerateReleaseUrl(notification.ConversationId);
        var confirmPaymentUrl = !string.IsNullOrWhiteSpace(notification.PaymentReferenceId)
            ? _adminActionLinkService.GeneratePaymentConfirmationUrl(notification.PaymentReferenceId)
            : null;

        var sb = new System.Text.StringBuilder();
        sb.Append("âš ï¸ ConversaciÃ³n requiere asistencia humana\n");
        sb.Append($"Motivo: {notification.Reason}\n");
        sb.Append($"Cliente: {notification.CustomerPhone}\n");
        sb.Append($"ConversaciÃ³n: {notification.ConversationId}\n\n");
        sb.Append($"Ãšltimo mensaje del cliente: {lastMsg}");
        if (confirmPaymentUrl != null)
            sb.Append($"\n\nâœ… Confirmar pago recibido:\n{confirmPaymentUrl}");
        if (releaseUrl != null)
            sb.Append($"\n\nðŸ“Ž Devolver al bot:\n{releaseUrl}");

        var message = sb.ToString();
        var notifiedCount = 0;

        foreach (var number in contacts)
        {
            try
            {
                var normalized = number.Replace("+", "").Replace(" ", "").Trim();
                await _whatsAppService.SendTextMessageAsync(businessId, normalized, message);
                notifiedCount++;
                _logger.LogInformation("NotificaciÃ³n de escalado enviada a {Number}", number);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando notificaciÃ³n de escalado a {Number}", number);
            }
        }

        return notifiedCount > 0;
    }
}


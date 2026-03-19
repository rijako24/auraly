using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Envía notificaciones de escalado a admins vía WhatsApp.
///
/// Los contactos se reciben como parámetro — no se lee ninguna clave de
/// BusinessConfigurations. El nodo Escalate en el flow JSON es quien los provee.
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
            ? "—"
            : notification.LastUserMessage.Length > 80
                ? notification.LastUserMessage[..80] + "..."
                : notification.LastUserMessage;

        var releaseUrl = _adminActionLinkService.GenerateReleaseUrl(notification.ConversationId);
        var confirmPaymentUrl = !string.IsNullOrWhiteSpace(notification.PaymentReferenceId)
            ? _adminActionLinkService.GeneratePaymentConfirmationUrl(notification.PaymentReferenceId)
            : null;

        var sb = new System.Text.StringBuilder();
        sb.Append("⚠️ Conversación requiere asistencia humana\n");
        sb.Append($"Motivo: {notification.Reason}\n");
        sb.Append($"Cliente: {notification.CustomerPhone}\n");
        sb.Append($"Conversación: {notification.ConversationId}\n\n");
        sb.Append($"Último mensaje del cliente: {lastMsg}");
        if (confirmPaymentUrl != null)
            sb.Append($"\n\n✅ Confirmar pago recibido:\n{confirmPaymentUrl}");
        if (releaseUrl != null)
            sb.Append($"\n\n📎 Devolver al bot:\n{releaseUrl}");

        var message = sb.ToString();
        var notifiedCount = 0;

        foreach (var number in contacts)
        {
            try
            {
                var normalized = number.Replace("+", "").Replace(" ", "").Trim();
                await _whatsAppService.SendTextMessageAsync(businessId, normalized, message);
                notifiedCount++;
                _logger.LogInformation("Notificación de escalado enviada a {Number}", number);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando notificación de escalado a {Number}", number);
            }
        }

        return notifiedCount > 0;
    }
}

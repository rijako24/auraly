using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Sends human escalation notifications to configured WhatsApp contacts.
/// </summary>
public class EscalationNotifier : IEscalationNotifier
{
    private readonly IWhatsAppService _whatsAppService;
    private readonly ILogger<EscalationNotifier> _logger;

    public EscalationNotifier(
        IWhatsAppService whatsAppService,
        ILogger<EscalationNotifier> logger)
    {
        _whatsAppService = whatsAppService;
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
                "No human escalation contacts configured for Biz={BusinessId}", businessId);
            return false;
        }

        var lastMsg = string.IsNullOrWhiteSpace(notification.LastUserMessage)
            ? "-"
            : notification.LastUserMessage.Length > 80
                ? notification.LastUserMessage[..80] + "..."
                : notification.LastUserMessage;

        var sb = new System.Text.StringBuilder();
        sb.Append("Conversacion requiere asistencia humana\n");
        sb.Append($"Motivo: {notification.Reason}\n");
        sb.Append($"Cliente: {notification.CustomerPhone}\n");
        sb.Append($"Conversacion: {notification.ConversationId}\n\n");
        sb.Append($"Ultimo mensaje del cliente: {lastMsg}");

        var message = sb.ToString();
        var notifiedCount = 0;

        foreach (var number in contacts)
        {
            try
            {
                var normalized = number.Replace("+", "").Replace(" ", "").Trim();
                await _whatsAppService.SendTextMessageAsync(businessId, normalized, message);
                notifiedCount++;
                _logger.LogInformation("Human escalation notification sent to {Number}", number);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending human escalation notification to {Number}", number);
            }
        }

        return notifiedCount > 0;
    }
}

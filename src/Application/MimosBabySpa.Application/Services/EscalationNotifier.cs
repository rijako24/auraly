using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Envía notificaciones de escalado a admins vía WhatsApp.
/// Lee contactos de BusinessConfiguration.EscalationContacts.
/// Intenta todos los contactos — no aborta en el primer fallo.
/// </summary>
public class EscalationNotifier : IEscalationNotifier
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWhatsAppService _whatsAppService;
    private readonly IAdminActionLinkService _adminActionLinkService;
    private readonly ILogger<EscalationNotifier> _logger;

    public EscalationNotifier(
        IUnitOfWork unitOfWork,
        IWhatsAppService whatsAppService,
        IAdminActionLinkService adminActionLinkService,
        ILogger<EscalationNotifier> logger)
    {
        _unitOfWork = unitOfWork;
        _whatsAppService = whatsAppService;
        _adminActionLinkService = adminActionLinkService;
        _logger = logger;
    }

    public async Task<bool> NotifyAdminsAsync(
        Guid businessId,
        EscalationNotification notification,
        CancellationToken ct = default)
    {
        var adminNumbers = await GetEscalationContactsAsync(businessId, ct);
        if (adminNumbers.Count == 0)
        {
            _logger.LogInformation(
                "Sin contactos de escalado configurados para Biz={BusinessId}",
                businessId);
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
        foreach (var number in adminNumbers)
        {
            try
            {
                var normalizedNumber = number.Replace("+", "").Replace(" ", "").Trim();
                await _whatsAppService.SendTextMessageAsync(businessId, normalizedNumber, message);
                notifiedCount++;
                _logger.LogInformation("Notificación enviada a {Number}", number);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando notificación a {Number}", number);
            }
        }

        return notifiedCount > 0;
    }

    private async Task<List<string>> GetEscalationContactsAsync(Guid businessId, CancellationToken ct)
    {
        var config = await _unitOfWork.BusinessConfigurations
            .GetByBusinessIdAndKeyAsync(businessId, BusinessConfigurationKey.EscalationContacts);
        if (config == null || string.IsNullOrWhiteSpace(config.Value))
            return new List<string>();

        try
        {
            using var doc = JsonDocument.Parse(config.Value);
            var root = doc.RootElement;
            if (!root.TryGetProperty("WhatsAppNumbers", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return new List<string>();

            return arr.EnumerateArray()
                .Select(e => e.GetString())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Cast<string>()
                .ToList();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "JSON inválido en EscalationContacts para Biz={BusinessId}", businessId);
            return new List<string>();
        }
    }
}

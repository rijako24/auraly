using MimosBabySpa.Application.Agents;
using Microsoft.Extensions.Logging;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Envía mensajes outbound (texto, imagen, documento) por WhatsApp en orden.
/// Compartido por el processor conversacional y el webhook de pago.
/// </summary>
public interface IOutboundMessageDispatcher
{
    Task SendAllAsync(
        Guid businessId,
        string phone,
        IReadOnlyList<OutboundMessage> messages,
        CancellationToken ct = default);
}

public sealed class OutboundMessageDispatcher : IOutboundMessageDispatcher
{
    private readonly IWhatsAppService _whatsAppService;
    private readonly ILogger<OutboundMessageDispatcher> _logger;

    public OutboundMessageDispatcher(
        IWhatsAppService whatsAppService,
        ILogger<OutboundMessageDispatcher> logger)
    {
        _whatsAppService = whatsAppService;
        _logger = logger;
    }

    public async Task SendAllAsync(
        Guid businessId,
        string phone,
        IReadOnlyList<OutboundMessage> messages,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(phone) || messages.Count == 0)
            return;

        var index = 0;
        foreach (var message in messages)
        {
            index++;
            try
            {
                await SendOneAsync(businessId, phone, message, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error enviando mensaje outbound {Index}/{Total} a {Phone}",
                    index,
                    messages.Count,
                    phone);
            }
        }
    }

    private async Task SendOneAsync(
        Guid businessId,
        string phone,
        OutboundMessage message,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(message.Body) && string.IsNullOrWhiteSpace(message.MediaUrl))
            return;

        if (string.IsNullOrWhiteSpace(message.MediaUrl))
        {
            await _whatsAppService.SendTextMessageAsync(businessId, phone, message.Body!);
            return;
        }

        if (string.Equals(message.MediaType, "image", StringComparison.OrdinalIgnoreCase))
        {
            await _whatsAppService.SendImageMessageAsync(
                businessId, phone, message.MediaUrl, message.Body);
            return;
        }

        await _whatsAppService.SendDocumentMessageAsync(
            businessId, phone, message.MediaUrl, message.Body, message.Filename);
    }
}

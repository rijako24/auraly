using MimosBabySpa.Application.Agents;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Billing;
using MimosBabySpa.Domain.Enums;

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
        Guid? conversationId = null,
        CancellationToken ct = default,
        bool throwOnFailure = false);
}

public sealed class OutboundMessageDispatcher : IOutboundMessageDispatcher
{
    private readonly IWhatsAppService _whatsAppService;
    private readonly IMessageService _messageService;
    private readonly IUsageBillingService _usageBilling;
    private readonly ILogger<OutboundMessageDispatcher> _logger;

    public OutboundMessageDispatcher(
        IWhatsAppService whatsAppService,
        IMessageService messageService,
        IUsageBillingService usageBilling,
        ILogger<OutboundMessageDispatcher> logger)
    {
        _whatsAppService = whatsAppService;
        _messageService = messageService;
        _usageBilling = usageBilling;
        _logger = logger;
    }

    public async Task SendAllAsync(
        Guid businessId,
        string phone,
        IReadOnlyList<OutboundMessage> messages,
        Guid? conversationId = null,
        CancellationToken ct = default,
        bool throwOnFailure = false)
    {
        if (string.IsNullOrWhiteSpace(phone) || messages.Count == 0)
            return;

        var gate = await _usageBilling.CanProcessAsync(businessId, ct);
        if (!gate.IsAllowed)
        {
            _logger.LogWarning(
                "Business {BusinessId}: outbound blocked by usage gate ({Code})",
                businessId, gate.Code);

            if (throwOnFailure)
                throw new InvalidOperationException($"Outbound blocked by usage gate: {gate.Code} - {gate.Reason}");

            return;
        }

        var index = 0;
        foreach (var message in messages)
        {
            index++;
            try
            {
                await SendOneAsync(businessId, phone, message, ct);
                await SaveSentMessageAsync(conversationId, message, throwOnFailure);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error enviando mensaje outbound {Index}/{Total} a {Phone}",
                    index,
                    messages.Count,
                    phone);

                if (throwOnFailure)
                    throw;
            }
        }

        await _usageBilling.ChargeAsync(new UsageChargeRequest(
            businessId,
            AgentId: null,
            ConversationId: conversationId,
            MessageId: null,
            UsageOperationType.OutboundSequence,
            OutboundMessages: messages.Count,
            MetadataJson: $"{{\"channel\":\"whatsapp\",\"phone\":\"{phone}\"}}"), ct);
    }

    private async Task SendOneAsync(
        Guid businessId,
        string phone,
        OutboundMessage message,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(message.Body) && string.IsNullOrWhiteSpace(message.MediaUrl))
        {
            if (message.Template is null)
                return;
        }

        if (message.Template is not null)
        {
            await _whatsAppService.SendTemplateMessageAsync(
                businessId,
                phone,
                message.Template.Name,
                message.Template.LanguageCode,
                message.Template.HeaderParameters,
                message.Template.BodyParameters,
                message.Buttons);
            return;
        }

        if (message.Buttons is { Count: > 0 } buttons)
        {
            await _whatsAppService.SendButtonMessageAsync(
                businessId,
                phone,
                message.Body ?? string.Empty,
                buttons);
            return;
        }

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

    private async Task SaveSentMessageAsync(Guid? conversationId, OutboundMessage message, bool throwOnFailure)
    {
        if (!conversationId.HasValue)
            return;

        var historyText = BuildHistoryText(message);
        if (string.IsNullOrWhiteSpace(historyText))
            return;

        try
        {
            await _messageService.SaveMessageAsync(conversationId.Value, "bot", historyText);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error guardando mensaje outbound en historial para Conv {ConversationId}",
                conversationId.Value);
        }
    }

    private static string? BuildHistoryText(OutboundMessage message)
    {
        var body = message.Body?.Trim();
        var media = BuildMediaReference(message);

        if (string.IsNullOrWhiteSpace(body) && message.Template is not null)
        {
            var parameters = message.Template.BodyParameters.Count > 0
                ? $" | params: {string.Join(", ", message.Template.BodyParameters)}"
                : string.Empty;
            body = $"[Plantilla WhatsApp: {message.Template.Name} ({message.Template.LanguageCode}){parameters}]";
        }

        if (string.IsNullOrWhiteSpace(body))
            return media;

        return string.IsNullOrWhiteSpace(media)
            ? body
            : $"{body}{Environment.NewLine}{Environment.NewLine}{media}";
    }

    private static string? BuildMediaReference(OutboundMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.MediaUrl))
            return null;

        var mediaType = string.IsNullOrWhiteSpace(message.MediaType)
            ? "media"
            : message.MediaType.Trim().ToLowerInvariant();

        var filename = string.IsNullOrWhiteSpace(message.Filename)
            ? null
            : message.Filename.Trim();

        return filename is null
            ? $"[{mediaType}] {message.MediaUrl}"
            : $"[{mediaType}] {filename} - {message.MediaUrl}";
    }
}


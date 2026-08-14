using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Auraly.Platform.Application.Agents;
using Auraly.Platform.Domain.Repositories;

namespace Auraly.Platform.Application.Services;

public interface IEventNotificationDispatcher
{
    Task SendEventAsync(
        Guid businessId,
        AgentConfig config,
        string eventName,
        IReadOnlyDictionary<string, string>? custom = null,
        CancellationToken ct = default);

    Task SendEventAsync(
        Guid businessId,
        AgentConfig config,
        string eventName,
        MessageSequenceContext context,
        CancellationToken ct = default);

    Task SendEventForActiveAgentAsync(
        Guid businessId,
        string eventName,
        MessageSequenceContext context,
        CancellationToken ct = default);
}

public sealed class EventNotificationDispatcher : IEventNotificationDispatcher
{
    private readonly IActiveAgentConfigResolver _activeAgentConfig;
    private readonly IMessageSequenceResolver _sequenceResolver;
    private readonly IOutboundMessageDispatcher _outboundDispatcher;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<EventNotificationDispatcher> _logger;

    public EventNotificationDispatcher(
        IActiveAgentConfigResolver activeAgentConfig,
        IMessageSequenceResolver sequenceResolver,
        IOutboundMessageDispatcher outboundDispatcher,
        IUnitOfWork unitOfWork,
        ILogger<EventNotificationDispatcher> logger)
    {
        _activeAgentConfig = activeAgentConfig;
        _sequenceResolver = sequenceResolver;
        _outboundDispatcher = outboundDispatcher;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public Task SendEventAsync(
        Guid businessId,
        AgentConfig config,
        string eventName,
        IReadOnlyDictionary<string, string>? custom = null,
        CancellationToken ct = default) =>
        SendEventAsync(
            businessId,
            config,
            eventName,
            new MessageSequenceContext
            {
                Custom = custom ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            },
            ct);

    public async Task SendEventForActiveAgentAsync(
        Guid businessId,
        string eventName,
        MessageSequenceContext context,
        CancellationToken ct = default)
    {
        var config = await _activeAgentConfig.GetActiveConfigAsync(businessId, ct);
        if (config is null)
        {
            _logger.LogWarning(
                "Event notification: no active agent for event '{Event}' BusinessId={BusinessId}",
                eventName,
                businessId);
            return;
        }

        await SendEventAsync(businessId, config, eventName, context, ct);
    }

    public async Task SendEventAsync(
        Guid businessId,
        AgentConfig config,
        string eventName,
        MessageSequenceContext context,
        CancellationToken ct = default)
    {
        if (!config.Notifications.TryGetValue(eventName, out var notification) || !notification.Enabled)
            return;

        foreach (var delivery in notification.Deliveries.Where(value => value.Enabled))
        {
            try
            {
                await SendDeliveryAsync(
                    businessId,
                    config,
                    eventName,
                    delivery.Id,
                    delivery.Recipients,
                    delivery.SendMessageSequence,
                    context,
                    ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(
                    exception,
                    "Event notification: delivery '{Delivery}' failed for event '{Event}' BusinessId={BusinessId}",
                    delivery.Id,
                    eventName,
                    businessId);
            }
        }
    }
    private async Task SendDeliveryAsync(
        Guid businessId,
        AgentConfig config,
        string eventName,
        string deliveryId,
        IReadOnlyList<string> configuredRecipients,
        string? configuredSequence,
        MessageSequenceContext context,
        CancellationToken ct)
    {
        var sequenceName = configuredSequence?.Trim();
        if (string.IsNullOrWhiteSpace(sequenceName))
        {
            _logger.LogWarning(
                "Event notification: delivery '{Delivery}' for event '{Event}' has no sequence BusinessId={BusinessId}",
                deliveryId,
                eventName,
                businessId);
            return;
        }

        if (!config.MessageSequences.ContainsKey(sequenceName))
        {
            _logger.LogWarning(
                "Event notification: sequence '{Sequence}' is not configured for delivery '{Delivery}' event '{Event}' BusinessId={BusinessId}",
                sequenceName,
                deliveryId,
                eventName,
                businessId);
            return;
        }

        var recipients = await ResolveRecipientsAsync(
            businessId,
            configuredRecipients,
            context.Custom,
            ct);
        if (recipients.Count == 0)
        {
            _logger.LogWarning(
                "Event notification: delivery '{Delivery}' for event '{Event}' resolved no recipients BusinessId={BusinessId}",
                deliveryId,
                eventName,
                businessId);
            return;
        }

        var messages = await _sequenceResolver.ResolveAsync(
            businessId,
            sequenceName,
            config.MessageSequences,
            context,
            ct);
        if (messages.Count == 0)
        {
            _logger.LogWarning(
                "Event notification: delivery '{Delivery}' event '{Event}' sequence '{Sequence}' resolved to zero messages BusinessId={BusinessId}",
                deliveryId,
                eventName,
                sequenceName,
                businessId);
            return;
        }

        foreach (var recipient in recipients)
            await _outboundDispatcher.SendAllAsync(businessId, recipient.Phone, messages, recipient.ConversationId, ct);

        _logger.LogInformation(
            "Event notification: delivery '{Delivery}' event '{Event}' sequence '{Sequence}' sent to {Count} recipient(s) BusinessId={BusinessId}",
            deliveryId,
            eventName,
            sequenceName,
            recipients.Count,
            businessId);
    }

    private async Task<IReadOnlyList<NotificationRecipient>> ResolveRecipientsAsync(
        Guid businessId,
        IReadOnlyList<string> configuredRecipients,
        IReadOnlyDictionary<string, string> custom,
        CancellationToken ct)
    {
        var sourceConversationId = TryReadGuid(custom, "source_conversation_id");
        var recipients = new List<NotificationRecipient>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var configured in configuredRecipients)
        {
            var raw = configured?.Trim();
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            if (raw.Equals("source:conversation", StringComparison.OrdinalIgnoreCase))
            {
                if (sourceConversationId is Guid conversationId)
                {
                    var conversation = await _unitOfWork.Conversations.GetByIdAsync(conversationId);
                    if (conversation is not null
                        && !string.IsNullOrWhiteSpace(conversation.UserNumber)
                        && seen.Add(conversation.UserNumber))
                        recipients.Add(new NotificationRecipient(conversation.UserNumber.Trim(), conversation.ConversationId));
                }
                continue;
            }
            if (raw.StartsWith("inbound:", StringComparison.OrdinalIgnoreCase))

            {
                var selector = raw["inbound:".Length..].Trim();
                if (string.IsNullOrWhiteSpace(selector))
                    continue;

                var contacts = await _unitOfWork.BusinessInboundContacts.GetActiveByBusinessAsync(businessId, ct);
                foreach (var contact in contacts.Where(contact =>
                             contact.Type.Equals(selector, StringComparison.OrdinalIgnoreCase)
                             || contact.Key.Equals(selector, StringComparison.OrdinalIgnoreCase)))
                {
                    var contactPhone = contact.PhoneNumber.Trim();
                    if (!string.IsNullOrWhiteSpace(contactPhone) && seen.Add(contactPhone))
                        recipients.Add(new NotificationRecipient(contactPhone, null));
                }
                continue;
            }

            var phone = ResolveCustomPlaceholders(raw, custom).Trim();
            if (string.IsNullOrWhiteSpace(phone) || phone.Contains('{') || phone.Contains('}'))
                continue;

            if (seen.Add(phone))
                recipients.Add(new NotificationRecipient(phone, null));
        }


        return recipients;
    }

    private static string ResolveCustomPlaceholders(string value, IReadOnlyDictionary<string, string> custom)
    {
        return Regex.Replace(value, "\\{(?<key>[^{}]+)\\}", match =>
        {
            var key = match.Groups["key"].Value.Trim();
            return custom.TryGetValue(key, out var resolved) ? resolved : string.Empty;
        });
    }

    private static Guid? TryReadGuid(IReadOnlyDictionary<string, string> custom, string key)
    {
        return custom.TryGetValue(key, out var value) && Guid.TryParse(value, out var parsed)
            ? parsed
            : null;
    }

    private sealed record NotificationRecipient(string Phone, Guid? ConversationId);
}


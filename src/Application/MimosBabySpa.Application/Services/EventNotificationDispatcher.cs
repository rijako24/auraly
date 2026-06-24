using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

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

        var sequenceName = notification.SendMessageSequence?.Trim();
        if (string.IsNullOrWhiteSpace(sequenceName))
        {
            _logger.LogWarning(
                "Event notification: event '{Event}' enabled but sendMessageSequence is empty for BusinessId={BusinessId}",
                eventName,
                businessId);
            return;
        }

        if (!config.MessageSequences.ContainsKey(sequenceName))
        {
            _logger.LogWarning(
                "Event notification: sequence '{Sequence}' is not configured for event '{Event}' BusinessId={BusinessId}",
                sequenceName,
                eventName,
                businessId);
            return;
        }

        var recipients = await ResolveRecipientsAsync(notification.Recipients, context.Custom);

        if (recipients.Count == 0)
        {
            _logger.LogWarning(
                "Event notification: event '{Event}' enabled but recipients is empty for BusinessId={BusinessId}",
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
                "Event notification: event '{Event}' sequence '{Sequence}' resolved to zero messages for BusinessId={BusinessId}",
                eventName,
                sequenceName,
                businessId);
            return;
        }

        foreach (var recipient in recipients)
            await _outboundDispatcher.SendAllAsync(businessId, recipient.Phone, messages, recipient.ConversationId, ct);

        _logger.LogInformation(
            "Event notification: event '{Event}' sequence '{Sequence}' sent to {Count} recipient(s) for BusinessId={BusinessId}",
            eventName,
            sequenceName,
            recipients.Count,
            businessId);
    }
    private async Task<IReadOnlyList<NotificationRecipient>> ResolveRecipientsAsync(
        IReadOnlyList<string> configuredRecipients,
        IReadOnlyDictionary<string, string> custom)
    {
        var sourceConversationId = TryReadGuid(custom, "source_conversation_id");
        var recipients = new List<NotificationRecipient>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var configured in configuredRecipients)
        {
            var raw = configured?.Trim();
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var phone = ResolveCustomPlaceholders(raw, custom).Trim();
            if (string.IsNullOrWhiteSpace(phone) || phone.Contains('{') || phone.Contains('}'))
                continue;

            if (seen.Add(phone))
                recipients.Add(new NotificationRecipient(phone, null));
        }

        if (recipients.Count == 0 && sourceConversationId is Guid conversationId)
        {
            var conversation = await _unitOfWork.Conversations.GetByIdAsync(conversationId);
            if (conversation is not null && !string.IsNullOrWhiteSpace(conversation.UserNumber) && seen.Add(conversation.UserNumber))
                recipients.Add(new NotificationRecipient(conversation.UserNumber.Trim(), conversation.ConversationId));
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


using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Agents;

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
    private readonly ILogger<EventNotificationDispatcher> _logger;

    public EventNotificationDispatcher(
        IActiveAgentConfigResolver activeAgentConfig,
        IMessageSequenceResolver sequenceResolver,
        IOutboundMessageDispatcher outboundDispatcher,
        ILogger<EventNotificationDispatcher> logger)
    {
        _activeAgentConfig = activeAgentConfig;
        _sequenceResolver = sequenceResolver;
        _outboundDispatcher = outboundDispatcher;
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

        var recipients = notification.Recipients
            .Select(r => r.Trim())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

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
            await _outboundDispatcher.SendAllAsync(businessId, recipient, messages, conversationId: null, ct);

        _logger.LogInformation(
            "Event notification: event '{Event}' sequence '{Sequence}' sent to {Count} recipient(s) for BusinessId={BusinessId}",
            eventName,
            sequenceName,
            recipients.Count,
            businessId);
    }
}
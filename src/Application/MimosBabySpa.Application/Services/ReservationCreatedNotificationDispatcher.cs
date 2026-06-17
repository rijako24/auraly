using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Application.Services;

public interface IReservationCreatedNotificationDispatcher
{
    Task SendAsync(
        Guid businessId,
        Reservation reservation,
        AgentConfig config,
        IReadOnlyDictionary<string, string>? custom = null,
        CancellationToken ct = default);

    Task SendForActiveAgentAsync(
        Guid businessId,
        Reservation reservation,
        IReadOnlyDictionary<string, string>? custom = null,
        CancellationToken ct = default);
}

public sealed class ReservationCreatedNotificationDispatcher : IReservationCreatedNotificationDispatcher
{
    private readonly IActiveAgentConfigResolver _activeAgentConfig;
    private readonly IMessageSequenceResolver _sequenceResolver;
    private readonly IOutboundMessageDispatcher _outboundDispatcher;
    private readonly ILogger<ReservationCreatedNotificationDispatcher> _logger;

    public ReservationCreatedNotificationDispatcher(
        IActiveAgentConfigResolver activeAgentConfig,
        IMessageSequenceResolver sequenceResolver,
        IOutboundMessageDispatcher outboundDispatcher,
        ILogger<ReservationCreatedNotificationDispatcher> logger)
    {
        _activeAgentConfig = activeAgentConfig;
        _sequenceResolver = sequenceResolver;
        _outboundDispatcher = outboundDispatcher;
        _logger = logger;
    }

    public async Task SendForActiveAgentAsync(
        Guid businessId,
        Reservation reservation,
        IReadOnlyDictionary<string, string>? custom = null,
        CancellationToken ct = default)
    {
        var config = await _activeAgentConfig.GetActiveConfigAsync(businessId, ct);
        if (config is null)
        {
            _logger.LogWarning(
                "Reservation notification: no active agent for BusinessId={BusinessId}",
                businessId);
            return;
        }

        await SendAsync(businessId, reservation, config, custom, ct);
    }

    public async Task SendAsync(
        Guid businessId,
        Reservation reservation,
        AgentConfig config,
        IReadOnlyDictionary<string, string>? custom = null,
        CancellationToken ct = default)
    {
        var notification = config.Notifications.ReservationCreated;
        if (!notification.Enabled)
            return;

        var sequenceName = notification.SendMessageSequence?.Trim();
        if (string.IsNullOrWhiteSpace(sequenceName))
        {
            _logger.LogWarning(
                "Reservation notification: enabled but sendMessageSequence is empty for BusinessId={BusinessId}",
                businessId);
            return;
        }

        if (!config.MessageSequences.ContainsKey(sequenceName))
        {
            _logger.LogWarning(
                "Reservation notification: sequence '{Sequence}' is not configured for BusinessId={BusinessId}",
                sequenceName,
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
                "Reservation notification: enabled but recipients is empty for BusinessId={BusinessId}",
                businessId);
            return;
        }

        var context = new MessageSequenceContext
        {
            Reservation = reservation,
            Custom = custom ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };

        var messages = await _sequenceResolver.ResolveAsync(
            businessId,
            sequenceName,
            config.MessageSequences,
            context,
            ct);

        if (messages.Count == 0)
        {
            _logger.LogWarning(
                "Reservation notification: sequence '{Sequence}' resolved to zero messages for BusinessId={BusinessId}",
                sequenceName,
                businessId);
            return;
        }

        foreach (var recipient in recipients)
        {
            await _outboundDispatcher.SendAllAsync(
                businessId,
                recipient,
                messages,
                conversationId: null,
                ct);
        }

        _logger.LogInformation(
            "Reservation notification: sequence '{Sequence}' sent to {Count} recipient(s) for BusinessId={BusinessId}",
            sequenceName,
            recipients.Count,
            businessId);
    }
}

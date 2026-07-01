using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

public interface IExternalEscalationOutcomePublisher
{
    Task PublishAsync(
        Guid businessId,
        Guid attemptId,
        string outcomeKey,
        IReadOnlyDictionary<string, string>? payload = null,
        CancellationToken ct = default);
}

public sealed class ExternalEscalationOutcomePublisher : IExternalEscalationOutcomePublisher
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAgentConfigProvider _configProvider;
    private readonly IEventNotificationDispatcher _notifications;
    private readonly ILogger<ExternalEscalationOutcomePublisher> _logger;

    public ExternalEscalationOutcomePublisher(
        IUnitOfWork unitOfWork,
        IAgentConfigProvider configProvider,
        IEventNotificationDispatcher notifications,
        ILogger<ExternalEscalationOutcomePublisher> logger)
    {
        _unitOfWork = unitOfWork;
        _configProvider = configProvider;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task PublishAsync(
        Guid businessId,
        Guid attemptId,
        string outcomeKey,
        IReadOnlyDictionary<string, string>? payload = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(outcomeKey))
            return;

        var attempt = await _unitOfWork.ExternalEscalationAttempts.GetByIdAsync(attemptId, ct);
        if (attempt is null || attempt.BusinessId != businessId)
            return;

        var normalizedOutcome = outcomeKey.Trim();
        var custom = MergePayload(ReadCustomPayload(attempt.CustomPayloadJson), payload);
        custom["outcome_key"] = normalizedOutcome;

        var config = await _configProvider.GetConfigAsync(attempt.SourceAgentId, ct);
        if (!TryResolveNotificationEvent(config, attempt.EventName, normalizedOutcome, out var eventName))
        {
            _logger.LogWarning(
                "External escalation outcome '{Outcome}' is not mapped for event '{ExternalEvent}' AgentId={AgentId} BusinessId={BusinessId}",
                normalizedOutcome,
                attempt.EventName,
                attempt.SourceAgentId,
                businessId);
            return;
        }

        try
        {
            await _notifications.SendEventAsync(
                businessId,
                config,
                eventName,
                BuildNotificationPayload(attempt, custom),
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "External escalation outcome event '{Event}' failed for AttemptId={AttemptId} BusinessId={BusinessId}",
                eventName,
                attempt.ExternalEscalationAttemptId,
                businessId);
        }
    }

    private static bool TryResolveNotificationEvent(
        AgentConfig config,
        string externalEventName,
        string outcomeKey,
        out string eventName)
    {
        eventName = string.Empty;
        if (!config.Escalations.External.Events.TryGetValue(externalEventName, out var definition))
            return false;

        foreach (var (configuredOutcome, configuredEvent) in definition.OutcomeEvents)
        {
            if (!configuredOutcome.Equals(outcomeKey, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(configuredEvent))
            {
                continue;
            }

            eventName = configuredEvent.Trim();
            return true;
        }

        return false;
    }

    private static Dictionary<string, string> BuildNotificationPayload(
        ExternalEscalationAttempt attempt,
        IReadOnlyDictionary<string, string> custom)
    {
        var payload = new Dictionary<string, string>(custom, StringComparer.OrdinalIgnoreCase)
        {
            ["external_interaction_id"] = attempt.ExternalEscalationAttemptId.ToString(),
            ["external_event_name"] = attempt.EventName,
            ["target_type"] = attempt.TargetType,
            ["target_id"] = attempt.TargetId.ToString(),
            ["attempt_code"] = attempt.AttemptCode,
            ["contact_key"] = attempt.ContactKey,
            ["contact_name"] = attempt.ContactNameSnapshot,
            ["contact_role"] = attempt.ContactRoleSnapshot,
            ["contact_phone"] = attempt.ContactPhoneSnapshot,
            ["contact_type"] = attempt.ContactTypeSnapshot ?? string.Empty,
            ["business_inbound_contact_id"] = attempt.BusinessInboundContactIdSnapshot?.ToString() ?? string.Empty,
            ["pickup_address"] = attempt.PickupAddressSnapshot ?? string.Empty,
            ["escalated_at_utc"] = attempt.EscalatedAt.ToString("O")
        };

        if (attempt.CompletedAt is DateTime completedAt)
            payload["completed_at_utc"] = completedAt.ToString("O");

        return payload;
    }

    private static Dictionary<string, string> MergePayload(
        IReadOnlyDictionary<string, string> basePayload,
        IReadOnlyDictionary<string, string>? responsePayload)
    {
        var merged = new Dictionary<string, string>(basePayload, StringComparer.OrdinalIgnoreCase);
        if (responsePayload is null)
            return merged;

        foreach (var (key, value) in responsePayload)
        {
            if (!string.IsNullOrWhiteSpace(key))
                merged[key] = value ?? string.Empty;
        }

        return merged;
    }

    private static IReadOnlyDictionary<string, string> ReadCustomPayload(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}

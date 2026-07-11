using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

public interface IExternalEscalationOutcomePublisher
{
    Task<Guid> EnqueueAsync(
        Guid businessId,
        Guid attemptId,
        string outcomeKey,
        IReadOnlyDictionary<string, string>? payload = null,
        CancellationToken ct = default);

    Task<bool> PublishAsync(Guid deliveryId, CancellationToken ct = default);
    Task<int> PublishPendingAsync(CancellationToken ct = default);
}

public sealed class ExternalEscalationOutcomePublisher : IExternalEscalationOutcomePublisher
{
    private const int PendingBatchSize = 100;
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

    public async Task<Guid> EnqueueAsync(
        Guid businessId,
        Guid attemptId,
        string outcomeKey,
        IReadOnlyDictionary<string, string>? payload = null,
        CancellationToken ct = default)
    {
        var normalizedOutcome = outcomeKey?.Trim() ?? string.Empty;
        if (normalizedOutcome.Length == 0)
            throw new ArgumentException("Outcome key is required.", nameof(outcomeKey));

        var existing = await _unitOfWork.ExternalEscalationOutcomeDeliveries
            .GetByAttemptAndOutcomeAsync(attemptId, normalizedOutcome, ct);
        if (existing is not null)
            return existing.ExternalEscalationOutcomeDeliveryId;

        var now = DateTime.UtcNow;
        var delivery = new ExternalEscalationOutcomeDelivery
        {
            ExternalEscalationOutcomeDeliveryId = Guid.NewGuid(),
            BusinessId = businessId,
            ExternalEscalationAttemptId = attemptId,
            OutcomeKey = normalizedOutcome,
            PayloadJson = JsonSerializer.Serialize(payload ?? new Dictionary<string, string>()),
            CreatedAt = now,
            NextAttemptAt = now
        };

        await _unitOfWork.ExternalEscalationOutcomeDeliveries.AddAsync(delivery, ct);
        return delivery.ExternalEscalationOutcomeDeliveryId;
    }

    public async Task<bool> PublishAsync(Guid deliveryId, CancellationToken ct = default)
    {
        var delivery = await _unitOfWork.ExternalEscalationOutcomeDeliveries.GetByIdAsync(deliveryId, ct);
        if (delivery is null)
            return false;
        if (delivery.PublishedAt is not null)
            return true;

        var attempt = await _unitOfWork.ExternalEscalationAttempts.GetByIdAsync(delivery.ExternalEscalationAttemptId, ct);
        if (attempt is null || attempt.BusinessId != delivery.BusinessId)
        {
            await RecordFailureAsync(delivery, "external_escalation_attempt_not_found", ct);
            return false;
        }

        var config = await _configProvider.GetConfigAsync(attempt.SourceAgentId, ct);
        if (!TryResolveNotificationEvent(config, attempt.EventName, delivery.OutcomeKey, out var eventName))
        {
            await RecordFailureAsync(delivery, "external_escalation_outcome_not_configured", ct);
            return false;
        }

        delivery.PublishAttempts++;
        delivery.LastAttemptAt = DateTime.UtcNow;
        delivery.NextAttemptAt = delivery.LastAttemptAt.Value.Add(RetryDelay(delivery.PublishAttempts));
        delivery.LastError = null;
        await _unitOfWork.ExternalEscalationOutcomeDeliveries.UpdateAsync(delivery, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        try
        {
            var custom = MergePayload(ReadCustomPayload(attempt.CustomPayloadJson), ReadPayload(delivery.PayloadJson));
            custom["outcome_key"] = delivery.OutcomeKey;
            custom["external_outcome_delivery_id"] = delivery.ExternalEscalationOutcomeDeliveryId.ToString();

            await _notifications.SendEventAsync(
                delivery.BusinessId,
                config,
                eventName,
                BuildNotificationPayload(attempt, custom),
                ct);

            delivery.PublishedAt = DateTime.UtcNow;
            delivery.LastError = null;
            await _unitOfWork.ExternalEscalationOutcomeDeliveries.UpdateAsync(delivery, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            delivery.LastError = Truncate(ex.Message, 4000);
            await _unitOfWork.ExternalEscalationOutcomeDeliveries.UpdateAsync(delivery, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            _logger.LogWarning(ex,
                "External escalation outcome delivery failed DeliveryId={DeliveryId} AttemptId={AttemptId}",
                delivery.ExternalEscalationOutcomeDeliveryId,
                attempt.ExternalEscalationAttemptId);
            return false;
        }
    }

    public async Task<int> PublishPendingAsync(CancellationToken ct = default)
    {
        var pending = await _unitOfWork.ExternalEscalationOutcomeDeliveries
            .GetPendingAsync(DateTime.UtcNow, PendingBatchSize, ct);
        var published = 0;
        foreach (var delivery in pending)
        {
            if (await PublishAsync(delivery.ExternalEscalationOutcomeDeliveryId, ct))
                published++;
        }

        return published;
    }

    private async Task RecordFailureAsync(ExternalEscalationOutcomeDelivery delivery, string error, CancellationToken ct)
    {
        delivery.PublishAttempts++;
        delivery.LastAttemptAt = DateTime.UtcNow;
        delivery.NextAttemptAt = delivery.LastAttemptAt.Value.Add(RetryDelay(delivery.PublishAttempts));
        delivery.LastError = error;
        await _unitOfWork.ExternalEscalationOutcomeDeliveries.UpdateAsync(delivery, ct);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    private static TimeSpan RetryDelay(int attempts) =>
        TimeSpan.FromMinutes(Math.Min(60, Math.Pow(2, Math.Clamp(attempts, 1, 6))));

    private static bool TryResolveNotificationEvent(AgentConfig config, string externalEventName, string outcomeKey, out string eventName)
    {
        eventName = string.Empty;
        if (!config.Escalations.External.Events.TryGetValue(externalEventName, out var definition))
            return false;

        foreach (var (configuredOutcome, configuredEvent) in definition.OutcomeEvents)
        {
            if (!configuredOutcome.Equals(outcomeKey, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(configuredEvent))
                continue;
            eventName = configuredEvent.Trim();
            return true;
        }
        return false;
    }

    private static Dictionary<string, string> BuildNotificationPayload(ExternalEscalationAttempt attempt, IReadOnlyDictionary<string, string> custom)
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

    private static Dictionary<string, string> MergePayload(IReadOnlyDictionary<string, string> first, IReadOnlyDictionary<string, string> second)
    {
        var merged = new Dictionary<string, string>(first, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in second)
            if (!string.IsNullOrWhiteSpace(key)) merged[key] = value ?? string.Empty;
        return merged;
    }

    private static IReadOnlyDictionary<string, string> ReadCustomPayload(string? json) => ReadPayload(json ?? "{}");

    private static IReadOnlyDictionary<string, string> ReadPayload(string json)
    {
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

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}

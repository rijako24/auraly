using Auraly.BuildingBlocks.Domain.Identifiers;

namespace Auraly.BuildingBlocks.Application.Outbox;

public enum OutboxMessageStatus
{
    Pending,
    Processing,
    Published,
    Failed
}

public sealed class OutboxMessage
{
    public OutboxMessage(
        Guid id,
        TenantId tenantId,
        string type,
        string payload,
        DateTimeOffset occurredAt)
    {
        if (id == Guid.Empty) throw new ArgumentException("An outbox ID is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(type)) throw new ArgumentException("A message type is required.", nameof(type));
        if (string.IsNullOrWhiteSpace(payload)) throw new ArgumentException("A payload is required.", nameof(payload));

        Id = id;
        TenantId = tenantId;
        Type = type;
        Payload = payload;
        OccurredAt = occurredAt;
        Status = OutboxMessageStatus.Pending;
    }

    public Guid Id { get; }
    public TenantId TenantId { get; }
    public string Type { get; }
    public string Payload { get; }
    public DateTimeOffset OccurredAt { get; }
    public OutboxMessageStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public string? LastError { get; private set; }

    public void StartAttempt()
    {
        if (Status == OutboxMessageStatus.Published)
        {
            throw new InvalidOperationException("A published message cannot be retried.");
        }

        AttemptCount++;
        Status = OutboxMessageStatus.Processing;
        LastError = null;
    }

    public void MarkPublished(DateTimeOffset processedAt)
    {
        if (Status != OutboxMessageStatus.Processing)
        {
            throw new InvalidOperationException("Only a processing message can be published.");
        }

        Status = OutboxMessageStatus.Published;
        ProcessedAt = processedAt;
    }

    public void MarkFailed(string error)
    {
        if (Status != OutboxMessageStatus.Processing)
        {
            throw new InvalidOperationException("Only a processing message can fail.");
        }

        Status = OutboxMessageStatus.Failed;
        LastError = string.IsNullOrWhiteSpace(error) ? "Unknown error" : error.Trim();
    }
}

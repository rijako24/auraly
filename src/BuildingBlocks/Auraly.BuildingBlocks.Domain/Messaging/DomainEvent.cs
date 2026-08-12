namespace Auraly.BuildingBlocks.Domain.Messaging;

public abstract record DomainEvent(Guid EventId, DateTimeOffset OccurredAt);

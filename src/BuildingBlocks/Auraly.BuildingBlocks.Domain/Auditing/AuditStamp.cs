using Auraly.BuildingBlocks.Domain.Identifiers;

namespace Auraly.BuildingBlocks.Domain.Auditing;

public sealed record AuditStamp(
    DateTimeOffset CreatedAt,
    UserId CreatedBy,
    DateTimeOffset? LastModifiedAt = null,
    UserId? LastModifiedBy = null)
{
    public AuditStamp Modified(DateTimeOffset occurredAt, UserId userId)
    {
        if (occurredAt < CreatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(occurredAt),
                "Modification time cannot precede creation time.");
        }

        return this with { LastModifiedAt = occurredAt, LastModifiedBy = userId };
    }
}

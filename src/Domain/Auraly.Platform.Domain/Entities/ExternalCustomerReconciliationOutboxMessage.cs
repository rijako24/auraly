namespace Auraly.Platform.Domain.Entities;

public sealed class ExternalCustomerReconciliationOutboxMessage
{
    public Guid MessageId { get; set; }
    public Guid ExternalCommerceCustomerId { get; set; }
    public Guid BusinessId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset AvailableAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public string? LastError { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public ExternalCommerceCustomer ExternalCommerceCustomer { get; set; } = null!;
    public Business Business { get; set; } = null!;
}

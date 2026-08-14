namespace Auraly.Platform.Domain.Entities;

public sealed class ExternalCommerceCustomer
{
    public Guid ExternalCommerceCustomerId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid IntegrationConnectionId { get; set; }
    public string ExternalAccountId { get; set; } = string.Empty;
    public string ExternalCustomerId { get; set; } = string.Empty;
    public Guid? PartyId { get; set; }
    public Guid? CustomerId { get; set; }
    public string ReconciliationStatus { get; set; } = "Pending";
    public string? ReconciliationError { get; set; }
    public DateTime? ReconciledAt { get; set; }
    public Guid? ReconciledBy { get; set; }
    public string? ReconciliationOrigin { get; set; }
    public string? Name { get; set; }
    public string PhoneNormalized { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime LastSyncedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public Business Business { get; set; } = null!;
    public IntegrationConnection IntegrationConnection { get; set; } = null!;
}

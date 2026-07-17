namespace MimosBabySpa.Domain.Entities;

public sealed class ExternalCommerceCustomer
{
    public Guid ExternalCommerceCustomerId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid IntegrationConnectionId { get; set; }
    public string ExternalAccountId { get; set; } = string.Empty;
    public string ExternalCustomerId { get; set; } = string.Empty;
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

using System.ComponentModel.DataAnnotations.Schema;

namespace Auraly.Platform.Domain.Entities;

public class Tenant
{
    public string TenantKey { get; private set; } = string.Empty;
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int MaximumUsers { get; set; } = 5;
    public int MaximumEnrolledDevices { get; set; } = 1;
    public string InventoryCostBasis { get; set; } = "LatestReceiptCost";
    [NotMapped] public int ActiveUserCount => AppUsers.Count(user => user.IsActive);
    [NotMapped] public int ActiveEnrolledDeviceCount { get; set; }
    [NotMapped] public string? LegalName { get; set; }
    [NotMapped] public string? Nit { get; set; }
    [NotMapped] public string? VerificationDigit { get; set; }
    [NotMapped] public string? EntityType { get; set; }
    [NotMapped] public string? IdentificationTypeCode { get; set; }
    [NotMapped] public Guid? PrimaryBusinessId { get; set; }
    [NotMapped] public string? LogoMediaRef { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    
    // Navigation properties
    public virtual ICollection<Business> Businesses { get; set; } = new List<Business>();
    public virtual ICollection<AppUser> AppUsers { get; set; } = new List<AppUser>();
}

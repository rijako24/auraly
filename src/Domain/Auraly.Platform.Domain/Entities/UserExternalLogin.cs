namespace Auraly.Platform.Domain.Entities;

public class UserExternalLogin
{
    public Guid ExternalLoginId { get; set; }
    public Guid UserId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ProviderKey { get; set; } = string.Empty;
    public string? ProviderDisplayName { get; set; }
    public string? ProviderEmail { get; set; }
    public DateTime CreatedAt { get; set; }

    public virtual AppUser User { get; set; } = null!;
}

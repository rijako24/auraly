namespace MimosBabySpa.Domain.Entities;

public class AppUser
{
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string NormalizedUsername { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public byte[]? PosOfflinePasswordSalt { get; set; }
    public byte[]? PosOfflinePasswordHash { get; set; }
    public int? PosOfflinePasswordIterations { get; set; }
    public DateTimeOffset? PosOfflinePasswordChangedAt { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? AvatarUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public bool EmailConfirmed { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
    public int AccessFailedCount { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }

    public virtual Tenant Tenant { get; set; } = null!;
    public virtual AppUser? CreatedByUser { get; set; }
    public virtual ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public virtual ICollection<UserExternalLogin> ExternalLogins { get; set; } = new List<UserExternalLogin>();
    public virtual ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();

    public string FullName => $"{FirstName} {LastName}";
    public bool IsLockedOut => LockoutEnd.HasValue && LockoutEnd > DateTimeOffset.UtcNow;

    public void RecordFailedLogin(int maxAttempts, TimeSpan lockoutDuration)
    {
        AccessFailedCount++;
        if (AccessFailedCount >= maxAttempts)
            LockoutEnd = DateTimeOffset.UtcNow.Add(lockoutDuration);
    }

    public void RecordSuccessfulLogin()
    {
        AccessFailedCount = 0;
        LockoutEnd = null;
        LastLoginAt = DateTime.UtcNow;
    }
}

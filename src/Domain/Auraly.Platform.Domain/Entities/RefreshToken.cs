namespace Auraly.Platform.Domain.Entities;

public class RefreshToken
{
    public Guid RefreshTokenId { get; set; }
    public Guid UserId { get; set; }
    public string Token { get; set; } = string.Empty;
    public string? DeviceInfo { get; set; }
    public string? IpAddress { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public Guid? ReplacedByTokenId { get; set; }

    public virtual AppUser User { get; set; } = null!;
    public virtual RefreshToken? ReplacedByToken { get; set; }

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsRevoked => RevokedAt.HasValue;
    public bool IsActive => !IsRevoked && !IsExpired;
}

using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Domain.Entities;

public class IntegrationConnection
{
    public Guid IntegrationConnectionId { get; set; }
    public Guid BusinessId { get; set; }
    public IntegrationProvider Provider { get; set; }
    public IntegrationCapability Capability { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? AccountIdentifier { get; set; }
    public string SettingsJson { get; set; } = "{}";
    public string? SecretsJson { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime? LastSyncAt { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public virtual Business Business { get; set; } = null!;
}

namespace MimosBabySpa.Domain.Entities;

public class AgentTemplate
{
    public Guid AgentTemplateId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? SettingsJson { get; set; }
    public bool IsSystemTemplate { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public virtual ICollection<Agent> Agents { get; set; } = new List<Agent>();
}

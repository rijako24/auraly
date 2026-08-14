namespace Auraly.Platform.Domain.Entities;

public class AgentType
{
    public Guid AgentTypeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public virtual ICollection<Agent> Agents { get; set; } = new List<Agent>();
}

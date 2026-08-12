using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Domain.Entities;

public class Agent
{
    public Guid AgentId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid AgentTypeId { get; set; }
    public Guid? AgentTemplateId { get; set; }
    public AgentBotType BotType { get; set; } = AgentBotType.Reservation;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Kind { get; set; } = "customer";
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// JSON with engine params, messages and escalation configuration.
    /// Structure includes SettingsJson escalations: { human: {...}, external: {...} }.
    /// </summary>
    public string? SettingsJson { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual AgentType AgentType { get; set; } = null!;
    public virtual AgentTemplate? AgentTemplate { get; set; }
}

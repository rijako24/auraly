namespace MimosBabySpa.Domain.Entities;

public class Agent
{
    public Guid AgentId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid AgentTypeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// JSON with engine params, messages and escalation configuration.
    /// Structure: { engine: {...}, messages: {...}, escalation: {...} }
    /// </summary>
    public string? SettingsJson { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual AgentType AgentType { get; set; } = null!;
    public virtual ICollection<AgentPromptSection> PromptSections { get; set; } = new List<AgentPromptSection>();
    public virtual ICollection<AgentKnowledgeSource> KnowledgeSources { get; set; } = new List<AgentKnowledgeSource>();
    public virtual ICollection<FlowDefinitionEntity> FlowDefinitions { get; set; } = new List<FlowDefinitionEntity>();
}

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
    /// Structure includes SettingsJson escalations: { human: {...}, external: {...} }.
    /// </summary>
    public string? SettingsJson { get; set; }

    /// <summary>
    /// Full system prompt for this agent in Markdown. Single source of truth for LLM context.
    /// </summary>
    public string? SystemPromptMarkdown { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual AgentType AgentType { get; set; } = null!;
}

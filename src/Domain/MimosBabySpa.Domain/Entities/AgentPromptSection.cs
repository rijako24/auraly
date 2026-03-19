namespace MimosBabySpa.Domain.Entities;

public class AgentPromptSection
{
    public Guid AgentPromptSectionId { get; set; }
    public Guid AgentId { get; set; }

    /// <summary>
    /// Unique key within the agent, e.g. "identity", "instructions", "sales_strategy".
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Displayed as a markdown heading in the prompt, e.g. "ROL E IDENTIDAD".
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Prompt content. Supports Handlebars template variables.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Where in the system prompt this section is injected.
    /// Values: system_header | before_instructions | after_instructions | context_footer
    /// </summary>
    public string InjectionPoint { get; set; } = "before_instructions";

    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public virtual Agent Agent { get; set; } = null!;
}

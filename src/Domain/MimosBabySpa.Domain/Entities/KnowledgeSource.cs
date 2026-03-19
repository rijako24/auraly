using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Domain.Entities;

public class KnowledgeSource
{
    public Guid KnowledgeSourceId { get; set; }
    public Guid BusinessId { get; set; }
    public KnowledgeSourceType Type { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Plain text content ready to be injected into the LLM prompt.
    /// The engine does NOT interpret this content — it's passed as-is to the model.
    /// The Type field is purely informational for the admin UI; it does not affect how
    /// the engine renders the content.
    ///
    /// Format tip: use markdown headings, bullet lists, and clear structure so the LLM
    /// can extract and present information naturally.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual ICollection<AgentKnowledgeSource> AgentKnowledgeSources { get; set; } = new List<AgentKnowledgeSource>();
}

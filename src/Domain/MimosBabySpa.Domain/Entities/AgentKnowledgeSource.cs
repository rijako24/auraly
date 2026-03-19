namespace MimosBabySpa.Domain.Entities;

public class AgentKnowledgeSource
{
    public Guid AgentKnowledgeSourceId { get; set; }
    public Guid AgentId { get; set; }
    public Guid KnowledgeSourceId { get; set; }

    /// <summary>
    /// When true, this knowledge source is automatically injected into every prompt turn.
    /// </summary>
    public bool AutoInject { get; set; } = false;

    public int DisplayOrder { get; set; }

    public virtual Agent Agent { get; set; } = null!;
    public virtual KnowledgeSource KnowledgeSource { get; set; } = null!;
}

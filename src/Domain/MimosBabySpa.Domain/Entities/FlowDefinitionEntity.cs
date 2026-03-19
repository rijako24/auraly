namespace MimosBabySpa.Domain.Entities;

public class FlowDefinitionEntity
{
    public Guid FlowDefinitionId { get; set; }
    public Guid AgentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>
    /// Serialized FlowDefinitionDocument JSON (variables, intentions, nodes, edges, config).
    /// </summary>
    public string DefinitionJson { get; set; } = string.Empty;

    /// <summary>
    /// Display version string, e.g. "1.0", "2.1-beta".
    /// </summary>
    public string Version { get; set; } = "1.0";

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public virtual Agent Agent { get; set; } = null!;
}

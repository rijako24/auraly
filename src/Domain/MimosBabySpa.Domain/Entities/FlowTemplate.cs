namespace MimosBabySpa.Domain.Entities;

public class FlowTemplate
{
    public Guid FlowTemplateId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Category { get; set; }

    /// <summary>
    /// Serialized FlowDefinitionDocument JSON used as starting template.
    /// </summary>
    public string DefinitionJson { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

namespace MimosBabySpa.Domain.Entities;

/// <summary>
/// Catálogo de tipos de nodo para el editor de flujos (admin). Los metadatos viven en BD.
/// </summary>
public class FlowNodeCatalog
{
    public Guid FlowNodeCatalogId { get; set; }
    public string CatalogKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int FlowNodeType { get; set; }
    public string Icon { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? Color { get; set; }
    public string InputsJson { get; set; } = "[]";
    public string OutputsJson { get; set; } = "[]";
    public string ConfigSchemaJson { get; set; } = "{}";
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

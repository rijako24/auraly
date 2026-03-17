namespace MimosBabySpa.Application.Configuration;

/// <summary>
/// Proyección de categoría de servicio para la capa de aplicación.
/// </summary>
public class CategoryInfo
{
    public Guid CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
}

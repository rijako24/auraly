namespace Auraly.Platform.Application.Configuration;

/// <summary>
/// Proyección de categoría de servicio para la capa de aplicación.
/// </summary>
public class CategoryInfo
{
    public Guid CategoryId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Frase introductoria de la categoría para el catálogo del LLM (opcional).
    /// </summary>
    public string? Description { get; set; }

    public int DisplayOrder { get; set; }
}

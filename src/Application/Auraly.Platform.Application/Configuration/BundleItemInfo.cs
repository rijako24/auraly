namespace Auraly.Platform.Application.Configuration;

/// <summary>
/// Proyección de un componente dentro de un bundle de servicio.
/// El ServiceCatalogBuilder usa esta información para mostrar al LLM
/// la composición real del bundle sin necesidad de inferencia desde texto.
/// </summary>
public class BundleItemInfo
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int DisplayOrder { get; set; }
}

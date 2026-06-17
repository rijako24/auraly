namespace MimosBabySpa.Application.Configuration;

/// <summary>
/// Proyección de una regla de add-on para el catálogo.
/// Indica qué add-on se puede ofrecer y con qué servicios es compatible (por categoría o servicio específico).
/// </summary>
public class AddOnRuleInfo
{
    public string AddOnName { get; set; } = string.Empty;
    public string AddOnDescription { get; set; } = string.Empty;
    public decimal AddOnPrice { get; set; }
    public bool IncludeInCheckoutTotal { get; set; } = true;
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Nombre del servicio principal (afinidad específica). Null cuando la compatibilidad es por categoría.
    /// </summary>
    public string? CompatibleWithServiceName { get; set; }

    /// <summary>
    /// ID de la categoría compatible (derivada de CompatibleService.CategoryId).
    /// Null = compatible con todos los servicios.
    /// </summary>
    public Guid? CompatibleCategoryId { get; set; }

    /// <summary>
    /// Nombre de la categoría compatible (para presentación).
    /// </summary>
    public string? CompatibleCategoryName { get; set; }
}

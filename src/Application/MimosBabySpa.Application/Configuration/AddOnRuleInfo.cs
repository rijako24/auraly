using MimosBabySpa.Domain.Enums;

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
    public int DisplayOrder { get; set; }

    /// <summary>
    /// Nombre del servicio principal (afinidad específica). Null cuando la compatibilidad es por categoría.
    /// </summary>
    public string? CompatibleWithServiceName { get; set; }

    /// <summary>
    /// Categoría con la que es compatible (viene de AddOnService.Category).
    /// Null = compatible con todos los Standard.
    /// </summary>
    public ServiceCategory? CompatibleServiceCategory { get; set; }
}

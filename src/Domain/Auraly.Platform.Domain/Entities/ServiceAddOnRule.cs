using Auraly.Platform.Domain.Enums;

namespace Auraly.Platform.Domain.Entities;

/// <summary>
/// Define qué add-ons ofrecer y en qué orden.
/// La compatibilidad con categorías viene de AddOnService.Category (el add-on indica con qué categoría es compatible).
/// CompatibleServiceId: afinidad específica (opcional). Null = usar AddOnService.Category para compatibilidad.
/// </summary>
public class ServiceAddOnRule
{
    public Guid ServiceAddOnRuleId { get; set; }
    public Guid BusinessId { get; set; }

    /// <summary>
    /// El add-on (FK → Services donde ServiceType = AddOn).
    /// </summary>
    public Guid AddOnServiceId { get; set; }

    /// <summary>
    /// El servicio principal compatible (afinidad específica). Null = usar AddOnService.Category para compatibilidad.
    /// </summary>
    public Guid? CompatibleServiceId { get; set; }

    /// <summary>
    /// Orden de presentación al ofrecer add-ons (menor = primero).
    /// </summary>
    public int DisplayOrder { get; set; } = 1;

    // Navigation properties
    public virtual Business Business { get; set; } = null!;
    public virtual Service AddOnService { get; set; } = null!;
    public virtual Service? CompatibleService { get; set; }
}

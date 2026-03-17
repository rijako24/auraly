namespace MimosBabySpa.Domain.Entities;

/// <summary>
/// Categoría de servicio por negocio. Define agrupación en catálogo.
/// Multitenant: cada negocio crea sus propias categorías (Plan, Taller, Clase, Otros, etc.).
/// </summary>
public class ServiceCategory
{
    public Guid ServiceCategoryId { get; set; }
    public Guid BusinessId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public virtual Business Business { get; set; } = null!;
}

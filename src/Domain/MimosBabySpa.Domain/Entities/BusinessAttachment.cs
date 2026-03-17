namespace MimosBabySpa.Domain.Entities;

/// <summary>
/// Adjunto genérico por negocio. Reutilizable desde ServiceCategories u otros contextos.
/// BlobPath es relativo al contenedor del negocio (business-{BusinessId}).
/// </summary>
public class BusinessAttachment
{
    public Guid BusinessAttachmentId { get; set; }
    public Guid BusinessId { get; set; }
    public string BlobPath { get; set; } = string.Empty;
    public string MediaType { get; set; } = "document";
    public string? Filename { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public virtual Business Business { get; set; } = null!;
}

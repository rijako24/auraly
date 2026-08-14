namespace Auraly.Platform.Domain.Entities;

public class IntegrationChannelWarehouse
{
    public Guid IntegrationChannelWarehouseId { get; set; }
    public Guid BusinessId { get; set; }
    public Guid IntegrationConnectionId { get; set; }
    public Guid BusinessWhatsAppNumberId { get; set; }
    public string WarehouseCode { get; set; } = string.Empty;
    public string? WarehouseName { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual IntegrationConnection IntegrationConnection { get; set; } = null!;
    public virtual BusinessWhatsAppNumber BusinessWhatsAppNumber { get; set; } = null!;
}

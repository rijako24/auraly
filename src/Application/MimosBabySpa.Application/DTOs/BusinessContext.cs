namespace MimosBabySpa.Application.DTOs;

public class BusinessContext
{
    public Guid BusinessId { get; set; }
    public Guid TenantId { get; set; }
    public string BusinessName { get; set; } = string.Empty;
    public BusinessConfigurationDto Configuration { get; set; } = new(); // Siempre tiene configuración (puede estar vacía)
    public BusinessWhatsAppNumberDto WhatsAppNumber { get; set; } = null!;
}

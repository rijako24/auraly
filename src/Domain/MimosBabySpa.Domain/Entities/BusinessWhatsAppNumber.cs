namespace MimosBabySpa.Domain.Entities;

public class BusinessWhatsAppNumber
{
    public Guid BusinessWhatsAppNumberId { get; set; }
    public Guid BusinessId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty; // Número en formato internacional: +573001234567
    public string WhatsAppPhoneNumberId { get; set; } = string.Empty; // ID de WhatsApp Cloud API
    public string WhatsAppAccessToken { get; set; } = string.Empty; // Token de acceso de WhatsApp
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    
    // Navigation properties
    public virtual Business Business { get; set; } = null!;
}

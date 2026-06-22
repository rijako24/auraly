namespace MimosBabySpa.Application.DTOs;

public class BusinessContext
{
    public Guid BusinessId { get; set; }
    public Guid TenantId { get; set; }
    public string BusinessName { get; set; } = string.Empty;

    /// <summary>
    /// Agent assigned to the WhatsApp number this message arrived on.
    /// Null only when the number has no agent assigned yet.
    /// </summary>
    public Guid? AgentId { get; set; }
    public BusinessWhatsAppNumberDto WhatsAppNumber { get; set; } = null!;
}


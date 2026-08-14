namespace Auraly.Platform.Domain.Entities;

public class BusinessWhatsAppNumber
{
    public Guid BusinessWhatsAppNumberId { get; set; }
    public Guid BusinessId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string? WhatsAppBusinessAccountId { get; set; }
    public string WhatsAppPhoneNumberId { get; set; } = string.Empty;
    public string WhatsAppAccessToken { get; set; } = string.Empty;

    /// <summary>
    /// The agent responsible for handling conversations arriving on this WhatsApp number.
    /// Resolving the agent directly from the incoming channel eliminates the need for
    /// WhatsApp message arrival.
    /// </summary>
    public Guid? AgentId { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual Agent? Agent { get; set; }
}

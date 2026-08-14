namespace Auraly.Platform.Application.DTOs;

/// <summary>
/// Representa un mensaje entrante de WhatsApp
/// </summary>
public class IncomingMessage
{
    public string UserNumber { get; set; } = string.Empty;
    public string MessageText { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? ReplyToProviderMessageId { get; set; }
    public string? InteractivePayload { get; set; }
    public IReadOnlyDictionary<string, string> Facts { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public string? RecipientPhoneNumberId { get; set; }
}

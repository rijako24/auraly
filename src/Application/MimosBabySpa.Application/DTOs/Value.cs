using System.Text.Json.Serialization;

namespace MimosBabySpa.Application.DTOs;

public class Value
{
    public WebhookMetadata? Metadata { get; set; }
    public List<Message> Messages { get; set; } = new();
    public List<Contact> Contacts { get; set; } = new();
}

/// <summary>
/// Metadata del webhook de WhatsApp (value.metadata).
/// phone_number_id identifica el número que recibió el mensaje.
/// </summary>
public class WebhookMetadata
{
    [JsonPropertyName("phone_number_id")]
    public string PhoneNumberId { get; set; } = string.Empty;
}

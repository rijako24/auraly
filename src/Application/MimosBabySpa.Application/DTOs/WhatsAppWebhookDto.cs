namespace MimosBabySpa.Application.DTOs;

public class WhatsAppWebhookDto
{
    public string Object { get; set; } = string.Empty;
    public List<Entry> Entry { get; set; } = new();
}

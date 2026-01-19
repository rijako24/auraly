namespace MimosBabySpa.Application.DTOs;

/// <summary>
/// Representa un mensaje entrante de WhatsApp
/// </summary>
public class IncomingMessage
{
    public string UserNumber { get; set; } = string.Empty;
    public string MessageText { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
}

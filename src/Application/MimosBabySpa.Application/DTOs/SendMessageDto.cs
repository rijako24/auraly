namespace MimosBabySpa.Application.DTOs;

public class SendMessageDto
{
    public string To { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
}

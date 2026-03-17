namespace MimosBabySpa.Application.Identity.DTOs;

public record MessageDto(
    Guid MessageId,
    Guid ConversationId,
    string Sender,
    string MessageText,
    DateTime Timestamp);

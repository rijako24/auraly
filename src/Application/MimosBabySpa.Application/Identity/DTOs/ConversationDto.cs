namespace MimosBabySpa.Application.Identity.DTOs;

public record ConversationDto(
    Guid ConversationId,
    Guid BusinessId,
    string UserNumber,
    string? LastMessage,
    DateTime Timestamp,
    string? CustomerName,
    string? CustomerEmail,
    string Status,
    DateTime OpenedAt,
    DateTime LastActivityAt,
    DateTime? ClosedAt,
    string? CloseReason);

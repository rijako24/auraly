namespace MimosBabySpa.Application.Identity.DTOs;

public record ConversationDto(
    Guid ConversationId,
    Guid BusinessId,
    Guid? AgentId,
    string UserNumber,
    string? LastMessage,
    DateTime Timestamp,
    string? CustomerName,
    string? CustomerEmail,
    string? CurrentStageName,
    string Status,
    DateTime OpenedAt,
    DateTime LastActivityAt,
    DateTime? ClosedAt,
    string? CloseReason,
    string Owner,
    bool BotEnabled);

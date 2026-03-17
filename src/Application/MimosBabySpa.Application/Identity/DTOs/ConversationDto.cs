namespace MimosBabySpa.Application.Identity.DTOs;

public record ConversationDto(
    Guid ConversationId,
    Guid BusinessId,
    string UserNumber,
    string? LastMessage,
    string? LastIntent,
    DateTime Timestamp,
    string? CustomerName,
    int? BabyAge,
    string? RecommendedPlan,
    string State);

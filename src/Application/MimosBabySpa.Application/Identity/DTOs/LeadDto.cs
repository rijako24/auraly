namespace MimosBabySpa.Application.Identity.DTOs;

public record LeadDto(
    Guid LeadId,
    Guid BusinessId,
    string UserNumber,
    string Status,
    DateTime Timestamp,
    string? CustomerName,
    string? Notes,
    Guid? ConversationId = null,
    string? ConversationStatus = null,
    string? CurrentStageName = null,
    DateTime? LastActivityAt = null,
    string? QualificationBand = null,
    string? QualificationLabel = null,
    int? QualificationPriority = null,
    string? QualificationFlowId = null,
    string? QualificationStageId = null,
    DateTime? QualificationUpdatedAt = null,
    DateTime? ConvertedAt = null);
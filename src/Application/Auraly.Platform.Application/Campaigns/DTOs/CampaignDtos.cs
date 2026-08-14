namespace Auraly.Platform.Application.Campaigns.DTOs;

public sealed record CampaignDto(
    Guid CampaignId,
    Guid BusinessId,
    Guid CreatedByUserId,
    string Name,
    string Status,
    string SourceType,
    string? FiltersJson,
    string TemplateName,
    string LanguageCode,
    string TemplateCategory,
    string? ParameterMappingJson,
    DateTime? ScheduledAtUtc,
    int RecipientCount,
    int SentCount,
    int FailedCount,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    IReadOnlyList<CampaignRecipientDto>? Recipients = null);

public sealed record CampaignRecipientDto(
    Guid CampaignRecipientId,
    Guid CampaignId,
    Guid BusinessId,
    string PhoneNormalized,
    string? CustomerName,
    Guid? SourceLeadId,
    Guid? SourceReservationId,
    string Status,
    string? WhatsAppMessageId,
    string? Error,
    string? VariablesJson,
    int AttemptCount,
    DateTime CreatedAt,
    DateTime? LastAttemptAtUtc,
    DateTime? SentAt);

public sealed record CreateCampaignRequest(
    Guid BusinessId,
    string Name,
    string SourceType,
    CampaignAudienceRequest Audience,
    string TemplateName,
    string LanguageCode,
    string TemplateCategory,
    IReadOnlyList<string> BodyParameterKeys,
    DateTime? ScheduledAtUtc = null);

public sealed record CampaignAudienceRequest(
    string? SegmentKey,
    int? InactiveDays,
    IReadOnlyList<ImportedCampaignRecipientRequest>? ImportedRecipients);

public sealed record ImportedCampaignRecipientRequest(
    string Phone,
    string? CustomerName,
    IReadOnlyDictionary<string, string>? Variables);

public sealed record CampaignDispatchMessage(
    Guid CampaignId,
    Guid BusinessId,
    Guid TenantId,
    Guid RequestedByUserId);

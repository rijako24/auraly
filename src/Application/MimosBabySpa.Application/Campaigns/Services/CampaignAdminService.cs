using System.Text.Json;
using MimosBabySpa.Application.Campaigns.DTOs;
using MimosBabySpa.Application.Campaigns.Interfaces;
using MimosBabySpa.Application.Common.DTOs;
using MimosBabySpa.Application.Common.Exceptions;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Campaigns.Services;

public sealed class CampaignAdminService : ICampaignAdminService
{
    private const int MaxRecipientsPerCampaign = 5000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IUnitOfWork _unitOfWork;
    private readonly ICampaignQueueService _queue;

    public CampaignAdminService(IUnitOfWork unitOfWork, ICampaignQueueService queue)
    {
        _unitOfWork = unitOfWork;
        _queue = queue;
    }

    public async Task<PagedResponse<CampaignDto>> GetPagedByBusinessIdAsync(
        Guid tenantId,
        bool canAccessAllTenants,
        Guid businessId,
        PagedRequest request,
        CancellationToken ct = default)
    {
        await EnsureBusinessAccessAsync(tenantId, canAccessAllTenants, businessId, ct);
        var (items, totalCount) = await _unitOfWork.Campaigns.GetPagedByBusinessIdAsync(
            businessId,
            request.Page,
            request.PageSize,
            request.Search,
            ct);

        return new PagedResponse<CampaignDto>(items.Select(c => MapToDto(c)).ToList(), totalCount, request.Page, request.PageSize);
    }

    public async Task<CampaignDto> GetByIdAsync(
        Guid tenantId,
        bool canAccessAllTenants,
        Guid campaignId,
        CancellationToken ct = default)
    {
        var campaign = await _unitOfWork.Campaigns.GetByIdAsync(campaignId, ct)
            ?? throw new NotFoundException(nameof(Campaign), campaignId);

        await EnsureBusinessAccessAsync(tenantId, canAccessAllTenants, campaign.BusinessId, ct);
        return MapToDto(campaign, includeRecipients: true);
    }

    public async Task<CampaignDto> CreateAsync(
        Guid tenantId,
        bool canAccessAllTenants,
        Guid userId,
        CreateCampaignRequest request,
        CancellationToken ct = default)
    {
        await EnsureBusinessAccessAsync(tenantId, canAccessAllTenants, request.BusinessId, ct);
        ValidateCreate(request);

        var now = DateTime.UtcNow;
        var recipientDrafts = await ResolveRecipientsAsync(request, now, ct);
        if (recipientDrafts.Count == 0)
            throw new DomainValidationException("Audience", "La audiencia no produjo destinatarios.");

        if (recipientDrafts.Count > MaxRecipientsPerCampaign)
            throw new DomainValidationException("Audience", $"La campaña excede el máximo de {MaxRecipientsPerCampaign} destinatarios.");

        var campaignId = Guid.NewGuid();
        var isScheduled = request.ScheduledAtUtc.HasValue && request.ScheduledAtUtc.Value > now;
        var campaign = new Campaign
        {
            CampaignId = campaignId,
            BusinessId = request.BusinessId,
            CreatedByUserId = userId,
            Name = request.Name.Trim(),
            Status = isScheduled ? CampaignStatuses.Scheduled : CampaignStatuses.Queued,
            SourceType = NormalizeSourceType(request.SourceType),
            FiltersJson = JsonSerializer.Serialize(request.Audience, JsonOptions),
            TemplateName = request.TemplateName.Trim(),
            LanguageCode = string.IsNullOrWhiteSpace(request.LanguageCode) ? "es_CO" : request.LanguageCode.Trim(),
            TemplateCategory = NormalizeTemplateCategory(request.TemplateCategory),
            ParameterMappingJson = JsonSerializer.Serialize(new CampaignParameterMapping(request.BodyParameterKeys), JsonOptions),
            ScheduledAtUtc = request.ScheduledAtUtc,
            RecipientCount = recipientDrafts.Count,
            CreatedAt = now
        };

        var recipients = recipientDrafts.Select(r => new CampaignRecipient
        {
            CampaignRecipientId = Guid.NewGuid(),
            CampaignId = campaignId,
            BusinessId = request.BusinessId,
            PhoneNormalized = r.PhoneNormalized,
            CustomerName = r.CustomerName,
            SourceLeadId = r.SourceLeadId,
            SourceReservationId = r.SourceReservationId,
            Status = CampaignRecipientStatuses.Pending,
            VariablesJson = JsonSerializer.Serialize(r.Variables, JsonOptions),
            CreatedAt = now
        }).ToList();

        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await _unitOfWork.Campaigns.AddAsync(campaign, ct);
            await _unitOfWork.Campaigns.AddRecipientsAsync(recipients, ct);
            await _unitOfWork.SaveChangesAsync(ct);
        }, ct);

        try
        {
            await _queue.EnqueueAsync(
                new CampaignDispatchMessage(campaign.CampaignId, campaign.BusinessId, tenantId, userId),
                request.ScheduledAtUtc,
                ct);
        }
        catch
        {
            campaign.Status = CampaignStatuses.QueueFailed;
            campaign.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.Campaigns.UpdateAsync(campaign, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            throw;
        }

        return MapToDto(campaign);
    }

    private async Task EnsureBusinessAccessAsync(Guid tenantId, bool canAccessAllTenants, Guid businessId, CancellationToken ct)
    {
        var business = await _unitOfWork.Businesses.GetByIdAsync(businessId)
            ?? throw new NotFoundException(nameof(Business), businessId);

        if (!canAccessAllTenants && business.TenantId != tenantId)
            throw new NotFoundException(nameof(Business), businessId);
    }

    private async Task<IReadOnlyList<RecipientDraft>> ResolveRecipientsAsync(
        CreateCampaignRequest request,
        DateTime now,
        CancellationToken ct)
    {
        var sourceType = NormalizeSourceType(request.SourceType);
        if (sourceType == CampaignSourceTypes.Import)
            return ResolveImportedRecipients(request.Audience.ImportedRecipients);

        var segmentKey = request.Audience.SegmentKey?.Trim();
        var inactiveDays = Math.Clamp(request.Audience.InactiveDays ?? 30, 1, 3650);
        var cutoff = now.AddDays(-inactiveDays);

        if (string.Equals(segmentKey, "reserved_no_return", StringComparison.OrdinalIgnoreCase))
        {
            var reservations = await _unitOfWork.Reservations.GetLatestCompletedCustomerReservationsWithoutFutureAsync(
                request.BusinessId, cutoff, now, MaxRecipientsPerCampaign, ct);

            return reservations
                .Where(r => !string.IsNullOrWhiteSpace(r.CustomerPhoneSnapshot))
                .Select(r => new RecipientDraft(
                    NormalizePhone(r.CustomerPhoneSnapshot!),
                    r.CustomerNameSnapshot,
                    null,
                    r.ReservationId,
                    BuildVariables(r.CustomerNameSnapshot, r.CustomerPhoneSnapshot, r.ReservationDateTime)))
                .DistinctBy(r => r.PhoneNormalized, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (string.Equals(segmentKey, "inactive_leads", StringComparison.OrdinalIgnoreCase))
        {
            var leads = await _unitOfWork.Leads.GetInactiveByBusinessIdAsync(
                request.BusinessId, cutoff, MaxRecipientsPerCampaign, ct);

            return leads
                .Where(l => !string.IsNullOrWhiteSpace(l.UserNumber))
                .Select(l => new RecipientDraft(
                    NormalizePhone(l.UserNumber),
                    l.CustomerName,
                    l.LeadId,
                    null,
                    BuildVariables(l.CustomerName, l.UserNumber, l.Timestamp)))
                .DistinctBy(r => r.PhoneNormalized, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        throw new DomainValidationException("Audience", "Segmento de audiencia no soportado.");
    }

    private static IReadOnlyList<RecipientDraft> ResolveImportedRecipients(
        IReadOnlyList<ImportedCampaignRecipientRequest>? imported)
    {
        if (imported is null || imported.Count == 0)
            throw new DomainValidationException("Audience", "Debes importar al menos un destinatario.");

        return imported
            .Where(r => !string.IsNullOrWhiteSpace(r.Phone))
            .Select(r =>
            {
                var variables = new Dictionary<string, string>(r.Variables ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase)
                {
                    ["CustomerName"] = r.CustomerName ?? string.Empty,
                    ["Phone"] = r.Phone
                };
                return new RecipientDraft(NormalizePhone(r.Phone), r.CustomerName, null, null, variables);
            })
            .DistinctBy(r => r.PhoneNormalized, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, string> BuildVariables(string? customerName, string? phone, DateTime? lastReservationAt) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["CustomerName"] = customerName ?? string.Empty,
            ["Phone"] = phone ?? string.Empty,
            ["LastReservationDate"] = lastReservationAt?.ToString("yyyy-MM-dd") ?? string.Empty
        };

    private static void ValidateCreate(CreateCampaignRequest request)
    {
        if (request.BusinessId == Guid.Empty)
            throw new DomainValidationException("BusinessId", "El negocio es obligatorio.");
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new DomainValidationException("Name", "El nombre de la campaña es obligatorio.");
        if (string.IsNullOrWhiteSpace(request.TemplateName))
            throw new DomainValidationException("TemplateName", "La plantilla de WhatsApp es obligatoria.");
    }

    private static string NormalizePhone(string phone)
    {
        var trimmed = phone.Trim();
        return trimmed.StartsWith("+", StringComparison.Ordinal) ? trimmed : new string(trimmed.Where(char.IsDigit).ToArray());
    }

    private static string NormalizeSourceType(string sourceType) =>
        string.Equals(sourceType, CampaignSourceTypes.Import, StringComparison.OrdinalIgnoreCase)
            ? CampaignSourceTypes.Import
            : CampaignSourceTypes.Segment;

    private static string NormalizeTemplateCategory(string templateCategory) =>
        string.Equals(templateCategory, "Utility", StringComparison.OrdinalIgnoreCase)
            ? "Utility"
            : "Marketing";

    private static CampaignDto MapToDto(Campaign campaign, bool includeRecipients = false) =>
        new(
            campaign.CampaignId,
            campaign.BusinessId,
            campaign.CreatedByUserId,
            campaign.Name,
            campaign.Status,
            campaign.SourceType,
            campaign.FiltersJson,
            campaign.TemplateName,
            campaign.LanguageCode,
            campaign.TemplateCategory,
            campaign.ParameterMappingJson,
            campaign.ScheduledAtUtc,
            campaign.RecipientCount,
            campaign.SentCount,
            campaign.FailedCount,
            campaign.CreatedAt,
            campaign.UpdatedAt,
            includeRecipients ? campaign.Recipients.Select(MapRecipient).ToList() : null);

    private static CampaignRecipientDto MapRecipient(CampaignRecipient recipient) =>
        new(
            recipient.CampaignRecipientId,
            recipient.CampaignId,
            recipient.BusinessId,
            recipient.PhoneNormalized,
            recipient.CustomerName,
            recipient.SourceLeadId,
            recipient.SourceReservationId,
            recipient.Status,
            recipient.WhatsAppMessageId,
            recipient.Error,
            recipient.VariablesJson,
            recipient.AttemptCount,
            recipient.CreatedAt,
            recipient.LastAttemptAtUtc,
            recipient.SentAt);

    private sealed record RecipientDraft(
        string PhoneNormalized,
        string? CustomerName,
        Guid? SourceLeadId,
        Guid? SourceReservationId,
        IReadOnlyDictionary<string, string> Variables);

    private sealed record CampaignParameterMapping(IReadOnlyList<string> BodyParameterKeys);
}

public static class CampaignStatuses
{
    public const string Draft = "Draft";
    public const string Scheduled = "Scheduled";
    public const string Queued = "Queued";
    public const string Processing = "Processing";
    public const string Completed = "Completed";
    public const string CompletedWithErrors = "CompletedWithErrors";
    public const string Cancelled = "Cancelled";
    public const string Failed = "Failed";
    public const string QueueFailed = "QueueFailed";
}

public static class CampaignRecipientStatuses
{
    public const string Pending = "Pending";
    public const string Sending = "Sending";
    public const string Sent = "Sent";
    public const string Failed = "Failed";
    public const string Skipped = "Skipped";
}

public static class CampaignSourceTypes
{
    public const string Segment = "Segment";
    public const string Import = "Import";
}

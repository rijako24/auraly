using Microsoft.Extensions.Logging;
using Auraly.Platform.Application.Common.DTOs;
using Auraly.Platform.Application.Common.Exceptions;
using Auraly.Platform.Application.Common.Interfaces;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;

namespace Auraly.Platform.Application.Identity.Services;

public class LeadAdminService : ILeadAdminService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditService _auditService;
    private readonly ICorrelationIdProvider _correlationIdProvider;
    private readonly ILogger<LeadAdminService> _logger;

    public LeadAdminService(
        IUnitOfWork unitOfWork,
        IAuditService auditService,
        ICorrelationIdProvider correlationIdProvider,
        ILogger<LeadAdminService> logger)
    {
        _unitOfWork = unitOfWork;
        _auditService = auditService;
        _correlationIdProvider = correlationIdProvider;
        _logger = logger;
    }

    public async Task<LeadDto> GetByIdAsync(Guid tenantId, Guid leadId, CancellationToken ct)
    {
        var lead = await _unitOfWork.Leads.GetByIdAsync(leadId)
            ?? throw new NotFoundException(nameof(Lead), leadId);
        await EnsureBusinessBelongsToTenantAsync(tenantId, lead.BusinessId, ct);
        return MapToDto(lead);
    }

    public async Task<IReadOnlyList<LeadDto>> GetByBusinessIdAsync(Guid tenantId, Guid businessId, CancellationToken ct)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);
        var leads = await _unitOfWork.Leads.GetByBusinessIdAsync(businessId);
        return leads.Select(MapToDto).ToList();
    }

    public async Task<PagedResponse<LeadDto>> GetPagedByBusinessIdAsync(
        Guid tenantId, Guid businessId, PagedRequest request, CancellationToken ct)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);
        var (items, totalCount) = await _unitOfWork.Leads.GetPagedByBusinessIdAsync(
            businessId, request.Page, request.PageSize, request.Search, ct);
        var conversations = await GetLatestConversationsByUserNumberAsync(businessId, items, ct);

        return new PagedResponse<LeadDto>(
            items.Select(l => MapToDto(l, conversations.GetValueOrDefault(l.UserNumber))).ToList(),
            totalCount,
            request.Page,
            request.PageSize);
    }

    public async Task<LeadDto> CreateAsync(Guid tenantId, CreateLeadRequest request, CancellationToken ct)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, request.BusinessId, ct);

        var existing = await _unitOfWork.Leads.GetByBusinessIdAndUserNumberAsync(request.BusinessId, request.UserNumber);
        if (existing is not null)
            throw new ConflictException($"Ya existe un lead con el número '{request.UserNumber}' para este negocio.");

        var lead = new Lead
        {
            LeadId = Guid.NewGuid(),
            BusinessId = request.BusinessId,
            UserNumber = request.UserNumber,
            Status = "New",
            Timestamp = DateTime.UtcNow,
            CustomerName = request.CustomerName,
            Notes = request.Notes
        };

        await _unitOfWork.Leads.CreateAsync(lead);
        await _unitOfWork.SaveChangesAsync(ct);

        await _auditService.LogAsync("Create", "Lead", lead.LeadId.ToString(), null, lead, ct);
        _logger.LogInformation("Lead created for business {BusinessId}, UserNumber {UserNumber} [CorrelationId: {CorrelationId}]",
            request.BusinessId, request.UserNumber, _correlationIdProvider.CorrelationId);

        return MapToDto(lead);
    }

    public async Task<LeadDto> UpdateAsync(Guid tenantId, Guid leadId, UpdateLeadRequest request, CancellationToken ct)
    {
        var lead = await _unitOfWork.Leads.GetByIdAsync(leadId)
            ?? throw new NotFoundException(nameof(Lead), leadId);
        await EnsureBusinessBelongsToTenantAsync(tenantId, lead.BusinessId, ct);

        var oldState = MapToDto(lead);

        if (request.Status is not null) lead.Status = request.Status;
        if (request.CustomerName is not null) lead.CustomerName = request.CustomerName;
        if (request.Notes is not null) lead.Notes = request.Notes;

        await _unitOfWork.Leads.UpdateAsync(lead);
        await _unitOfWork.SaveChangesAsync(ct);

        await _auditService.LogAsync("Update", "Lead", leadId.ToString(), oldState, MapToDto(lead), ct);
        return MapToDto(lead);
    }

    private async Task EnsureBusinessBelongsToTenantAsync(Guid tenantId, Guid businessId, CancellationToken ct)
    {
        var business = await _unitOfWork.Businesses.GetByIdAsync(businessId)
            ?? throw new NotFoundException(nameof(Business), businessId);
        if (business.TenantId != tenantId)
            throw new NotFoundException(nameof(Business), businessId);
    }

    private async Task<Dictionary<string, Conversation>> GetLatestConversationsByUserNumberAsync(
        Guid businessId,
        IReadOnlyList<Lead> leads,
        CancellationToken ct)
    {
        var conversations = new Dictionary<string, Conversation>(StringComparer.OrdinalIgnoreCase);

        foreach (var userNumber in leads
            .Select(l => l.UserNumber)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var conversation = await _unitOfWork.Conversations.GetByBusinessIdAndUserNumberAsync(businessId, userNumber);
            if (conversation is not null)
                conversations[userNumber] = conversation;
        }

        return conversations;
    }

    private static LeadDto MapToDto(Lead l) => MapToDto(l, null);

    private static LeadDto MapToDto(Lead l, Conversation? conversation)
    {
        return new LeadDto(
            l.LeadId,
            l.BusinessId,
            l.UserNumber,
            l.Status,
            conversation?.LastActivityAt ?? l.Timestamp,
            l.CustomerName ?? conversation?.CustomerName,
            l.Notes,
            conversation?.ConversationId,
            conversation?.Status.ToString(),
            conversation?.CurrentStageName,
            conversation?.LastActivityAt,
            l.QualificationBand,
            l.QualificationLabel,
            l.QualificationPriority,
            l.QualificationFlowId,
            l.QualificationStageId,
            l.QualificationUpdatedAt,
            l.ConvertedAt);
    }
}

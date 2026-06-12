using MimosBabySpa.Application.Common.DTOs;
using MimosBabySpa.Application.Common.Exceptions;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Application.StateManagement;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Models;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Identity.Services;

public class ConversationAdminService : IConversationAdminService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConversationStateManager _stateManager;
    private readonly IMessageService _messageService;
    private readonly IAgentConversationService _agentService;
    private readonly ILeadService _leadService;
    private readonly IAgentRepository _agentRepository;

    public ConversationAdminService(
        IUnitOfWork unitOfWork,
        IConversationStateManager stateManager,
        IMessageService messageService,
        IAgentConversationService agentService,
        ILeadService leadService,
        IAgentRepository agentRepository)
    {
        _unitOfWork = unitOfWork;
        _stateManager = stateManager;
        _messageService = messageService;
        _agentService = agentService;
        _leadService = leadService;
        _agentRepository = agentRepository;
    }

    public async Task<PagedResponse<ConversationDto>> GetPagedByBusinessIdAsync(
        Guid tenantId, Guid businessId, PagedRequest request,
        ConversationLifecycleStatus? status, CancellationToken ct)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);

        var (items, totalCount) = await _unitOfWork.Conversations.GetPagedByBusinessIdAsync(
            businessId, request.Page, request.PageSize, request.Search, status, ct);

        return new PagedResponse<ConversationDto>(
            items.Select(MapToDto).ToList(), totalCount, request.Page, request.PageSize);
    }

    public async Task<ConversationDto> GetByIdAsync(Guid tenantId, Guid conversationId, CancellationToken ct)
    {
        var conv = await _unitOfWork.Conversations.GetByIdAsync(conversationId)
            ?? throw new NotFoundException(nameof(Conversation), conversationId);

        await EnsureBusinessBelongsToTenantAsync(tenantId, conv.BusinessId, ct);
        return MapToDto(conv);
    }

    public async Task<PagedResponse<MessageDto>> GetMessagesByConversationIdAsync(
        Guid tenantId, Guid conversationId, PagedRequest request, CancellationToken ct)
    {
        var conv = await _unitOfWork.Conversations.GetByIdAsync(conversationId)
            ?? throw new NotFoundException(nameof(Conversation), conversationId);

        await EnsureBusinessBelongsToTenantAsync(tenantId, conv.BusinessId, ct);

        var (items, totalCount) = await _unitOfWork.Messages.GetPagedByConversationIdAsync(
            conversationId, request.Page, request.PageSize, ct);

        return new PagedResponse<MessageDto>(
            items.Select(m => new MessageDto(m.MessageId, m.ConversationId, m.Sender, m.MessageText, m.Timestamp))
                .ToList(), totalCount, request.Page, request.PageSize);
    }

    public async Task<WebConversationMessageResponse> SendWebMessageAsync(
        Guid tenantId, Guid conversationId, WebConversationMessageRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            throw new DomainValidationException("Message", "El mensaje no puede estar vacio.");

        var conv = await _unitOfWork.Conversations.GetByIdAsync(conversationId)
            ?? throw new NotFoundException(nameof(Conversation), conversationId);

        await EnsureBusinessBelongsToTenantAsync(tenantId, conv.BusinessId, ct);

        var state = await _stateManager.GetStateByConversationIdAsync(conversationId, ct);
        if (state?.Owner == ConversationOwner.Human)
        {
            await _messageService.SaveMessageAsync(conversationId, "User", request.Message.Trim());
            await _unitOfWork.SaveChangesAsync(ct);
            return new WebConversationMessageResponse(string.Empty, false, false);
        }

        var agent = (await _agentRepository.GetByBusinessAsync(conv.BusinessId, ct))
            .FirstOrDefault(a => a.IsActive);
        if (agent is null)
            throw new DomainValidationException("Agent", "No hay un agente activo para este negocio.");

        var result = await _agentService.ProcessMessageAsync(
            agent.AgentId,
            conversationId,
            request.Message.Trim(),
            conv.UserNumber,
            ct);

        if (!result.Success)
            throw new DomainValidationException("Agent", result.ErrorMessage ?? "No se pudo procesar el mensaje.");

        var lead = await _leadService.GetOrCreateLeadAsync(conv.BusinessId, conv.UserNumber, conv.CustomerName);
        await UpdateLeadStatusAsync(lead, result.ReservationCreated);

        return new WebConversationMessageResponse(
            result.Response,
            result.EscalatedToHuman,
            result.ReservationCreated);
    }

    private static ConversationDto MapToDto(Conversation c) =>
        new(
            c.ConversationId, c.BusinessId, c.UserNumber,
            c.LastMessage, c.Timestamp,
            c.CustomerName, c.CustomerEmail, c.CurrentStageName,
            c.Status.ToString(),
            c.OpenedAt, c.LastActivityAt, c.ClosedAt, c.CloseReason);

    private async Task UpdateLeadStatusAsync(Lead lead, bool reservationCreated)
    {
        var newStatus = reservationCreated ? "Closed" : "Contacted";
        if (newStatus == lead.Status)
            return;

        await _leadService.UpdateLeadAsync(lead.LeadId, status: newStatus);
    }

    private async Task EnsureBusinessBelongsToTenantAsync(Guid tenantId, Guid businessId, CancellationToken ct)
    {
        var business = await _unitOfWork.Businesses.GetByIdAsync(businessId)
            ?? throw new NotFoundException(nameof(Business), businessId);
        if (business.TenantId != tenantId)
            throw new NotFoundException(nameof(Business), businessId);
    }
}

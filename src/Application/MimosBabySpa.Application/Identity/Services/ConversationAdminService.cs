using Microsoft.Extensions.DependencyInjection;
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
    private readonly IServiceProvider _serviceProvider;
    private readonly IConversationStateManager _stateManager;

    public ConversationAdminService(
        IUnitOfWork unitOfWork,
        IServiceProvider serviceProvider,
        IConversationStateManager stateManager)
    {
        _unitOfWork = unitOfWork;
        _serviceProvider = serviceProvider;
        _stateManager = stateManager;
    }

    public async Task<PagedResponse<ConversationDto>> GetPagedByBusinessIdAsync(
        Guid tenantId, bool canAccessAllTenants, Guid businessId, PagedRequest request,
        ConversationLifecycleStatus? status, CancellationToken ct)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, canAccessAllTenants, businessId, ct);

        var (items, totalCount) = await _unitOfWork.Conversations.GetPagedByBusinessIdAsync(
            businessId, request.Page, request.PageSize, request.Search, status, ct);

        var dtos = new List<ConversationDto>(items.Count);
        foreach (var item in items)
        {
            dtos.Add(await MapToDtoAsync(item, ct));
        }

        return new PagedResponse<ConversationDto>(
            dtos, totalCount, request.Page, request.PageSize);
    }

    public async Task<ConversationDto> GetByIdAsync(Guid tenantId, bool canAccessAllTenants, Guid conversationId, CancellationToken ct)
    {
        var conv = await _unitOfWork.Conversations.GetByIdAsync(conversationId)
            ?? throw new NotFoundException(nameof(Conversation), conversationId);

        await EnsureBusinessBelongsToTenantAsync(tenantId, canAccessAllTenants, conv.BusinessId, ct);
        return await MapToDtoAsync(conv, ct);
    }

    public async Task<PagedResponse<MessageDto>> GetMessagesByConversationIdAsync(
        Guid tenantId, bool canAccessAllTenants, Guid conversationId, PagedRequest request, CancellationToken ct)
    {
        var conv = await _unitOfWork.Conversations.GetByIdAsync(conversationId)
            ?? throw new NotFoundException(nameof(Conversation), conversationId);

        await EnsureBusinessBelongsToTenantAsync(tenantId, canAccessAllTenants, conv.BusinessId, ct);

        var (items, totalCount) = await _unitOfWork.Messages.GetPagedByConversationIdAsync(
            conversationId, request.Page, request.PageSize, ct);

        return new PagedResponse<MessageDto>(
            items.Select(m => new MessageDto(m.MessageId, m.ConversationId, m.Sender, m.MessageText, m.Timestamp))
                .ToList(), totalCount, request.Page, request.PageSize);
    }

    public async Task<WebConversationMessageResponse> SendWebMessageAsync(
        Guid tenantId, bool canAccessAllTenants, Guid conversationId, WebConversationMessageRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            throw new DomainValidationException("Message", "El mensaje no puede estar vacio.");

        var conv = await _unitOfWork.Conversations.GetByIdAsync(conversationId)
            ?? throw new NotFoundException(nameof(Conversation), conversationId);

        await EnsureBusinessBelongsToTenantAsync(tenantId, canAccessAllTenants, conv.BusinessId, ct);

        var text = request.Message.Trim();
        var outboundMessages = _serviceProvider.GetRequiredService<IOutboundMessageDispatcher>();
        await outboundMessages.SendAllAsync(
            conv.BusinessId,
            conv.UserNumber,
            [new OutboundMessage(text, null)],
            conversationId,
            ct,
            throwOnFailure: true);

        return new WebConversationMessageResponse(text, false, false);
    }

    public async Task<ConversationDto> UpdateOwnerAsync(
        Guid tenantId, bool canAccessAllTenants, Guid conversationId, UpdateConversationOwnerRequest request, CancellationToken ct = default)
    {
        if (!Enum.TryParse<ConversationOwner>(request.Owner, ignoreCase: true, out var owner))
            throw new DomainValidationException("Owner", "El propietario debe ser Bot o Human.");

        var conv = await _unitOfWork.Conversations.GetByIdAsync(conversationId)
            ?? throw new NotFoundException(nameof(Conversation), conversationId);

        await EnsureBusinessBelongsToTenantAsync(tenantId, canAccessAllTenants, conv.BusinessId, ct);

        var state = await _stateManager.GetOrCreateStateAsync(
            conversationId, conv.BusinessId, conv.UserNumber, ct);
        state.Owner = owner;
        await _stateManager.SaveStateAsync(conversationId, state, ct);

        return MapToDto(conv, state.Owner);
    }

    private async Task<ConversationDto> MapToDtoAsync(Conversation c, CancellationToken ct)
    {
        var state = await _stateManager.GetStateByConversationIdAsync(c.ConversationId, ct);
        return MapToDto(c, state?.Owner ?? ConversationOwner.Bot);
    }

    private static ConversationDto MapToDto(Conversation c, ConversationOwner owner) =>
        new(
            c.ConversationId, c.BusinessId, c.UserNumber,
            c.LastMessage, c.Timestamp,
            c.CustomerName, c.CustomerEmail, c.CurrentStageName,
            c.Status.ToString(),
            c.OpenedAt, c.LastActivityAt, c.ClosedAt, c.CloseReason,
            owner.ToString(), owner == ConversationOwner.Bot);

    private async Task EnsureBusinessBelongsToTenantAsync(Guid tenantId, bool canAccessAllTenants, Guid businessId, CancellationToken ct)
    {
        var business = await _unitOfWork.Businesses.GetByIdAsync(businessId)
            ?? throw new NotFoundException(nameof(Business), businessId);
        if (!canAccessAllTenants && business.TenantId != tenantId)
            throw new NotFoundException(nameof(Business), businessId);
    }
}

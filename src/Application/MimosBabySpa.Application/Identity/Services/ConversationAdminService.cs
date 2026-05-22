using MimosBabySpa.Application.Common.DTOs;
using MimosBabySpa.Application.Common.Exceptions;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Identity.Services;

public class ConversationAdminService : IConversationAdminService
{
    private readonly IUnitOfWork _unitOfWork;

    public ConversationAdminService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<PagedResponse<ConversationDto>> GetPagedByBusinessIdAsync(
        Guid tenantId, Guid businessId, PagedRequest request,
        Domain.Enums.ConversationState? state, CancellationToken ct)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);

        var (items, totalCount) = await _unitOfWork.Conversations.GetPagedByBusinessIdAsync(
            businessId, request.Page, request.PageSize, request.Search, state, ct);

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

    private static ConversationDto MapToDto(Conversation c) =>
        new(
            c.ConversationId, c.BusinessId, c.UserNumber,
            c.LastMessage, c.Timestamp,
            c.CustomerName, c.CustomerEmail,
            c.State.ToString());

    private async Task EnsureBusinessBelongsToTenantAsync(Guid tenantId, Guid businessId, CancellationToken ct)
    {
        var business = await _unitOfWork.Businesses.GetByIdAsync(businessId)
            ?? throw new NotFoundException(nameof(Business), businessId);
        if (business.TenantId != tenantId)
            throw new NotFoundException(nameof(Business), businessId);
    }
}

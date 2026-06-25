using MimosBabySpa.Application.Common.DTOs;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Identity.Interfaces;

public interface IConversationAdminService
{
    Task<PagedResponse<ConversationDto>> GetPagedByBusinessIdAsync(
        Guid tenantId, Guid businessId, PagedRequest request,
        ConversationLifecycleStatus? status = null, CancellationToken ct = default);

    Task<ConversationDto> GetByIdAsync(
        Guid tenantId, Guid conversationId, CancellationToken ct = default);

    Task<PagedResponse<MessageDto>> GetMessagesByConversationIdAsync(
        Guid tenantId, Guid conversationId, PagedRequest request,
        CancellationToken ct = default);

    Task<WebConversationMessageResponse> SendWebMessageAsync(
        Guid tenantId, Guid conversationId, WebConversationMessageRequest request,
        CancellationToken ct = default);

    Task<ConversationDto> UpdateOwnerAsync(
        Guid tenantId, Guid conversationId, UpdateConversationOwnerRequest request,
        CancellationToken ct = default);
}

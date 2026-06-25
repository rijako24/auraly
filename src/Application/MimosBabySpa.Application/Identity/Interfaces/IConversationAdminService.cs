using MimosBabySpa.Application.Common.DTOs;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Identity.Interfaces;

public interface IConversationAdminService
{
    Task<PagedResponse<ConversationDto>> GetPagedByBusinessIdAsync(
        Guid tenantId, bool canAccessAllTenants, Guid businessId, PagedRequest request,
        ConversationLifecycleStatus? status = null, CancellationToken ct = default);

    Task<ConversationDto> GetByIdAsync(
        Guid tenantId, bool canAccessAllTenants, Guid conversationId, CancellationToken ct = default);

    Task<PagedResponse<MessageDto>> GetMessagesByConversationIdAsync(
        Guid tenantId, bool canAccessAllTenants, Guid conversationId, PagedRequest request,
        CancellationToken ct = default);

    Task<WebConversationMessageResponse> SendWebMessageAsync(
        Guid tenantId, bool canAccessAllTenants, Guid conversationId, WebConversationMessageRequest request,
        CancellationToken ct = default);

    Task<ConversationDto> UpdateOwnerAsync(
        Guid tenantId, bool canAccessAllTenants, Guid conversationId, UpdateConversationOwnerRequest request,
        CancellationToken ct = default);
}

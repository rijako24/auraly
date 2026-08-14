using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Auraly.Platform.Application.Common.DTOs;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Platform.Domain.Enums;
using Auraly.Api.Authorization;
using Auraly.Api.Extensions;

namespace Auraly.Api.Controllers;

[ApiController]
[Route("api/v1/conversations")]
[Authorize]
public class ConversationsController : ControllerBase
{
    private readonly IConversationAdminService _service;

    public ConversationsController(IConversationAdminService service)
    {
        _service = service;
    }

    [HttpGet]
    [PermissionAuthorize("conversations.read")]
    public async Task<ActionResult<PagedResponse<ConversationDto>>> GetByBusiness(
        [FromQuery] Guid businessId,
        [FromQuery] ConversationLifecycleStatus? status,
        [FromQuery] PagedRequest request,
        [FromQuery] Guid? agentId,
        CancellationToken ct)
    {
        return Ok(await _service.GetPagedByBusinessIdAsync(
            User.GetTenantId(), User.HasPermission("tenants.read"), businessId, request, status, ct, agentId));
    }

    [HttpGet("{conversationId:guid}")]
    [PermissionAuthorize("conversations.read")]
    public async Task<ActionResult<ConversationDto>> GetById(
        Guid conversationId,
        CancellationToken ct)
    {
        return Ok(await _service.GetByIdAsync(User.GetTenantId(), User.HasPermission("tenants.read"), conversationId, ct));
    }

    [HttpGet("{conversationId:guid}/messages")]
    [PermissionAuthorize("conversations.read")]
    public async Task<ActionResult<PagedResponse<MessageDto>>> GetMessages(
        Guid conversationId,
        [FromQuery] PagedRequest request,
        CancellationToken ct)
    {
        return Ok(await _service.GetMessagesByConversationIdAsync(
            User.GetTenantId(), User.HasPermission("tenants.read"), conversationId, request, ct));
    }

    [HttpPost("{conversationId:guid}/messages/web")]
    [PermissionAuthorize("conversations.read")]
    public async Task<ActionResult<WebConversationMessageResponse>> SendWebMessage(
        Guid conversationId,
        [FromBody] WebConversationMessageRequest request,
        CancellationToken ct)
    {
        return Ok(await _service.SendWebMessageAsync(
            User.GetTenantId(), User.HasPermission("tenants.read"), conversationId, request, ct));
    }

    [HttpPatch("{conversationId:guid}/owner")]
    [PermissionAuthorize("conversations.read")]
    public async Task<ActionResult<ConversationDto>> UpdateOwner(
        Guid conversationId,
        [FromBody] UpdateConversationOwnerRequest request,
        CancellationToken ct)
    {
        return Ok(await _service.UpdateOwnerAsync(
            User.GetTenantId(), User.HasPermission("tenants.read"), conversationId, request, ct));
    }
}

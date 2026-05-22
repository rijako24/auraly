using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MimosBabySpa.Application.Common.DTOs;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.WebAPI.Authorization;
using MimosBabySpa.WebAPI.Extensions;

namespace MimosBabySpa.WebAPI.Controllers;

[ApiController]
[Route("api/conversations")]
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
        CancellationToken ct)
    {
        return Ok(await _service.GetPagedByBusinessIdAsync(
            User.GetTenantId(), businessId, request, status, ct));
    }

    [HttpGet("{conversationId:guid}")]
    [PermissionAuthorize("conversations.read")]
    public async Task<ActionResult<ConversationDto>> GetById(
        Guid conversationId,
        CancellationToken ct)
    {
        return Ok(await _service.GetByIdAsync(User.GetTenantId(), conversationId, ct));
    }

    [HttpGet("{conversationId:guid}/messages")]
    [PermissionAuthorize("conversations.read")]
    public async Task<ActionResult<PagedResponse<MessageDto>>> GetMessages(
        Guid conversationId,
        [FromQuery] PagedRequest request,
        CancellationToken ct)
    {
        return Ok(await _service.GetMessagesByConversationIdAsync(
            User.GetTenantId(), conversationId, request, ct));
    }
}

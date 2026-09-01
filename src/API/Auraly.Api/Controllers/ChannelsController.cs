using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Api.Authorization;
using Auraly.Api.Extensions;

namespace Auraly.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/businesses/{businessId:guid}/channels")]
public sealed class ChannelsController : ControllerBase
{
    private readonly IWhatsAppChannelAdminService _service;
    public ChannelsController(IWhatsAppChannelAdminService service) => _service = service;

    [HttpGet]
    [PermissionAuthorize("agents.read")]
    public async Task<ActionResult<IReadOnlyList<WhatsAppChannelDto>>> GetAll(Guid businessId, CancellationToken ct) =>
        Ok(await _service.GetByBusinessAsync(User.GetTenantId(), User.HasPermission("tenants.read"), businessId, ct));

    [HttpPost("whatsapp")]
    [PermissionAuthorize("agents.update")]
    public async Task<ActionResult<WhatsAppChannelDto>> Create(Guid businessId, [FromBody] CreateWhatsAppChannelRequest request, CancellationToken ct)
    {
        var result = await _service.CreateAsync(User.GetTenantId(), User.HasPermission("tenants.read"), businessId, request, ct);
        return Created($"api/v1/businesses/{businessId}/channels/whatsapp/{result.BusinessWhatsAppNumberId}", result);
    }

    [HttpPut("whatsapp/{channelId:guid}")]
    [PermissionAuthorize("agents.update")]
    public async Task<ActionResult<WhatsAppChannelDto>> Update(Guid businessId, Guid channelId, [FromBody] UpdateWhatsAppChannelRequest request, CancellationToken ct) =>
        Ok(await _service.UpdateAsync(User.GetTenantId(), User.HasPermission("tenants.read"), businessId, channelId, request, ct));

    [HttpDelete("whatsapp/{channelId:guid}")]
    [PermissionAuthorize("agents.update")]
    public async Task<IActionResult> Deactivate(Guid businessId, Guid channelId, CancellationToken ct)
    {
        await _service.DeactivateAsync(User.GetTenantId(), User.HasPermission("tenants.read"), businessId, channelId, ct);
        return NoContent();
    }

    [HttpPost("whatsapp/{channelId:guid}/validate")]
    [PermissionAuthorize("agents.update")]
    public async Task<ActionResult<WhatsAppChannelConnectionStatusDto>> Validate(Guid businessId, Guid channelId, CancellationToken ct) =>
        Ok(await _service.ValidateAsync(User.GetTenantId(), User.HasPermission("tenants.read"), businessId, channelId, ct));
}

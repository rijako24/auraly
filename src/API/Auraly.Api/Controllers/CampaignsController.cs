using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Auraly.Platform.Application.Campaigns.DTOs;
using Auraly.Platform.Application.Campaigns.Interfaces;
using Auraly.Platform.Application.Common.DTOs;
using Auraly.Api.Authorization;
using Auraly.Api.Extensions;

namespace Auraly.Api.Controllers;

[ApiController]
[Route("api/v1/campaigns")]
[Authorize]
public class CampaignsController : ControllerBase
{
    private readonly ICampaignAdminService _service;

    public CampaignsController(ICampaignAdminService service)
    {
        _service = service;
    }

    [HttpGet]
    [PermissionAuthorize("campaigns.read")]
    public async Task<ActionResult<PagedResponse<CampaignDto>>> GetByBusiness(
        [FromQuery] Guid businessId, [FromQuery] PagedRequest request, CancellationToken ct)
    {
        return Ok(await _service.GetPagedByBusinessIdAsync(
            User.GetTenantId(),
            User.HasPermission("tenants.read"),
            businessId,
            request,
            ct));
    }

    [HttpGet("{campaignId:guid}")]
    [PermissionAuthorize("campaigns.read")]
    public async Task<ActionResult<CampaignDto>> GetById(Guid campaignId, CancellationToken ct)
    {
        return Ok(await _service.GetByIdAsync(
            User.GetTenantId(),
            User.HasPermission("tenants.read"),
            campaignId,
            ct));
    }

    [HttpPost]
    [PermissionAuthorize("campaigns.create")]
    public async Task<ActionResult<CampaignDto>> Create(
        [FromBody] CreateCampaignRequest request, CancellationToken ct)
    {
        var result = await _service.CreateAsync(
            User.GetTenantId(),
            User.HasPermission("tenants.read"),
            User.GetUserId(),
            request,
            ct);

        return CreatedAtAction(nameof(GetById), new { campaignId = result.CampaignId }, result);
    }
}

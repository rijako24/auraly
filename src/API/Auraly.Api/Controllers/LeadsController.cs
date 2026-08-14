using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Auraly.Platform.Application.Common.DTOs;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Api.Authorization;
using Auraly.Api.Extensions;

namespace Auraly.Api.Controllers;

[ApiController]
[Route("api/v1/leads")]
[Authorize]
public class LeadsController : ControllerBase
{
    private readonly ILeadAdminService _service;

    public LeadsController(ILeadAdminService service)
    {
        _service = service;
    }

    [HttpGet]
    [PermissionAuthorize("leads.read")]
    public async Task<ActionResult<PagedResponse<LeadDto>>> GetByBusiness(
        [FromQuery] Guid businessId, [FromQuery] PagedRequest request, CancellationToken ct)
    {
        return Ok(await _service.GetPagedByBusinessIdAsync(User.GetTenantId(), businessId, request, ct));
    }

    [HttpGet("{leadId:guid}")]
    [PermissionAuthorize("leads.read")]
    public async Task<ActionResult<LeadDto>> GetById(Guid leadId, CancellationToken ct)
    {
        return Ok(await _service.GetByIdAsync(User.GetTenantId(), leadId, ct));
    }

    [HttpPost]
    [PermissionAuthorize("leads.create")]
    public async Task<ActionResult<LeadDto>> Create(
        [FromBody] CreateLeadRequest request, CancellationToken ct)
    {
        var result = await _service.CreateAsync(User.GetTenantId(), request, ct);
        return CreatedAtAction(nameof(GetById), new { leadId = result.LeadId }, result);
    }

    [HttpPut("{leadId:guid}")]
    [PermissionAuthorize("leads.update")]
    public async Task<ActionResult<LeadDto>> Update(
        Guid leadId, [FromBody] UpdateLeadRequest request, CancellationToken ct)
    {
        return Ok(await _service.UpdateAsync(User.GetTenantId(), leadId, request, ct));
    }
}

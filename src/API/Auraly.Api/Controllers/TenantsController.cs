using System.Security.Claims;
using Auraly.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MimosBabySpa.Application.Common.DTOs;
using Auraly.Contracts.Tenants;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;

namespace Auraly.Api.Controllers;

[ApiController]
[Route("api/v1/tenants")]
[Authorize]
public sealed class TenantsController(ITenantService tenantService) : ControllerBase
{
    [HttpGet]
    [PermissionAuthorize("tenants.read")]
    public async Task<ActionResult<PagedResponse<TenantDto>>> GetAll([FromQuery] PagedRequest request, CancellationToken ct) =>
        Ok(await tenantService.GetPagedAsync(request, ct));

    [HttpGet("{tenantId:guid}")]
    [PermissionAuthorize("tenants.read")]
    public async Task<ActionResult<TenantDto>> GetById(Guid tenantId, CancellationToken ct) =>
        Ok(await tenantService.GetByIdAsync(tenantId, ct));

    [HttpPost]
    [PermissionAuthorize("tenants.create")]
    public async Task<ActionResult<ProvisionTenantResult>> Create([FromBody] ProvisionTenantRequest request, CancellationToken ct)
    {
        var actor = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var userId)
            ? userId : (Guid?)null;
        var result = await tenantService.ProvisionAsync(request, actor, ct);
        return CreatedAtAction(nameof(GetById), new { tenantId = result.TenantId }, result);
    }

    [HttpPut("{tenantId:guid}")]
    [PermissionAuthorize("tenants.update")]
    public async Task<ActionResult<TenantDto>> Update(Guid tenantId, [FromBody] UpdateTenantRequest request, CancellationToken ct) =>
        Ok(await tenantService.UpdateAsync(tenantId, request.Name, request.Email, ct));

    [HttpDelete("{tenantId:guid}")]
    [PermissionAuthorize("tenants.update")]
    public async Task<IActionResult> Deactivate(Guid tenantId, CancellationToken ct)
    {
        await tenantService.DeactivateAsync(tenantId, ct);
        return NoContent();
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MimosBabySpa.Application.Common.DTOs;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.WebAPI.Authorization;

namespace MimosBabySpa.WebAPI.Controllers;

[ApiController]
[Route("api/tenants")]
[Authorize]
public class TenantsController : ControllerBase
{
    private readonly ITenantService _tenantService;

    public TenantsController(ITenantService tenantService)
    {
        _tenantService = tenantService;
    }

    [HttpGet]
    [PermissionAuthorize("tenants.read")]
    public async Task<ActionResult<PagedResponse<TenantDto>>> GetAll(
        [FromQuery] PagedRequest request, CancellationToken ct)
    {
        return Ok(await _tenantService.GetPagedAsync(request, ct));
    }

    [HttpGet("{tenantId:guid}")]
    [PermissionAuthorize("tenants.read")]
    public async Task<ActionResult<TenantDto>> GetById(Guid tenantId, CancellationToken ct)
    {
        return Ok(await _tenantService.GetByIdAsync(tenantId, ct));
    }

    [HttpPost]
    [PermissionAuthorize("tenants.create")]
    public async Task<ActionResult<TenantDto>> Create(
        [FromBody] CreateTenantRequest request, CancellationToken ct)
    {
        var result = await _tenantService.CreateAsync(request.Name, request.Email, ct);
        return CreatedAtAction(nameof(GetById), new { tenantId = result.TenantId }, result);
    }

    [HttpPut("{tenantId:guid}")]
    [PermissionAuthorize("tenants.update")]
    public async Task<ActionResult<TenantDto>> Update(
        Guid tenantId, [FromBody] UpdateTenantRequest request, CancellationToken ct)
    {
        return Ok(await _tenantService.UpdateAsync(tenantId, request.Name, request.Email, ct));
    }

    [HttpDelete("{tenantId:guid}")]
    [PermissionAuthorize("tenants.update")]
    public async Task<IActionResult> Deactivate(Guid tenantId, CancellationToken ct)
    {
        await _tenantService.DeactivateAsync(tenantId, ct);
        return NoContent();
    }
}

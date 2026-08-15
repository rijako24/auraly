using Auraly.Api.Authorization;
using Auraly.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Auraly.Platform.Application.Common.DTOs;
using Auraly.Platform.Application.Common.Exceptions;
using Auraly.Contracts.Tenants;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;

namespace Auraly.Api.Controllers;

[ApiController]
[Route("api/v1/tenants")]
[Authorize]
public sealed class TenantsController(ITenantService tenantService, ITenantDeviceAdminStore deviceAdmin) : ControllerBase
{
    [HttpGet]
    [PermissionAuthorize("tenants.read")]
    public async Task<ActionResult<PagedResponse<TenantDto>>> GetAll([FromQuery] PagedRequest request, CancellationToken ct) => Ok(await tenantService.GetPagedAsync(request, ct));

    [HttpGet("{tenantId:guid}")]
    [PermissionAuthorize("tenants.read")]
    public async Task<ActionResult<TenantDto>> GetById(Guid tenantId, CancellationToken ct) => Ok(await tenantService.GetByIdAsync(tenantId, ct));

    [HttpPost]
    [PermissionAuthorize("tenants.create")]
    public async Task<ActionResult<ProvisionTenantResult>> Create([FromBody] ProvisionTenantRequest request, CancellationToken ct)
    {
        var result = await tenantService.ProvisionAsync(request, User.GetUserId(), ct);
        return CreatedAtAction(nameof(GetById), new { tenantId = result.TenantId }, result);
    }

    [HttpPut("{tenantId:guid}")]
    public async Task<ActionResult<TenantDto>> Update(Guid tenantId, [FromBody] UpdateTenantRequest request, CancellationToken ct)
    {
        if (request.Name is not null || request.Email is not null) EnsurePermission("tenants.update");
        if (request.MaximumUsers.HasValue || request.MaximumEnrolledDevices.HasValue) EnsurePermission("tenants.capacity.update");
        return Ok(await tenantService.UpdateAsync(tenantId, request.Name, request.Email, request.MaximumUsers, request.MaximumEnrolledDevices, ct));
    }

    [HttpGet("{tenantId:guid}/devices")]
    [PermissionAuthorize("tenants.devices.read")]
    public async Task<ActionResult<IReadOnlyList<TenantEnrolledDeviceDto>>> GetDevices(Guid tenantId, CancellationToken ct) => Ok(await deviceAdmin.ListAsync(tenantId, ct));

    [HttpDelete("{tenantId:guid}/devices/{deviceId:guid}")]
    [PermissionAuthorize("tenants.devices.revoke")]
    public async Task<IActionResult> DeactivateDevice(Guid tenantId, Guid deviceId, CancellationToken ct)
    {
        await deviceAdmin.DeactivateAsync(tenantId, deviceId, ct);
        return NoContent();
    }

    [HttpPost("{tenantId:guid}/activate")]
    [PermissionAuthorize("tenants.status.update")]
    public async Task<IActionResult> Activate(Guid tenantId, CancellationToken ct)
    {
        await tenantService.ActivateAsync(tenantId, ct);
        return NoContent();
    }

    [HttpDelete("{tenantId:guid}")]
    [PermissionAuthorize("tenants.status.update")]
    public async Task<IActionResult> Deactivate(Guid tenantId, CancellationToken ct)
    {
        await tenantService.DeactivateAsync(tenantId, ct);
        return NoContent();
    }

    private void EnsurePermission(string permission)
    {
        if (!User.HasPermission(permission)) throw new ForbiddenException($"Falta el permiso '{permission}'.");
    }
}
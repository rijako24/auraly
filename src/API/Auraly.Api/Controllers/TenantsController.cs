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
    private const long MaxLogoBytes = 4 * 1024 * 1024;
    private const long MaxLogoRequestBytes = MaxLogoBytes + 64 * 1024;

    [HttpGet]
    [PermissionAuthorize("tenants.read")]
    public async Task<ActionResult<PagedResponse<TenantDto>>> GetAll([FromQuery] PagedRequest request, CancellationToken ct) => Ok(await tenantService.GetPagedAsync(request, ct));

    [HttpGet("{tenantId:guid}")]
    [PermissionAuthorize("tenants.read")]
    public async Task<ActionResult<TenantDto>> GetById(Guid tenantId, CancellationToken ct) => Ok(await tenantService.GetByIdAsync(tenantId, ct));

    [HttpGet("branding")]
    public async Task<ActionResult<TenantBrandingDto>> GetBranding(CancellationToken ct) =>
        Ok(await tenantService.GetBrandingAsync(User.GetTenantId(), ct));

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
        if (request.Name is not null || request.Email is not null || request.LegalName is not null
            || request.Nit is not null || request.VerificationDigit is not null
            || request.EntityType is not null || request.IdentificationTypeCode is not null
            || request.InventoryCostBasis is not null)
            EnsurePermission("tenants.update");
        if (request.MaximumUsers.HasValue || request.MaximumEnrolledDevices.HasValue) EnsurePermission("tenants.capacity.update");
        return Ok(await tenantService.UpdateAsync(tenantId, request.Name, request.Email,
            request.MaximumUsers, request.MaximumEnrolledDevices, request.LegalName, request.Nit,
            request.VerificationDigit, request.EntityType, request.IdentificationTypeCode,
            request.InventoryCostBasis, ct));
    }

    [HttpPost("{tenantId:guid}/logo")]
    [PermissionAuthorize("tenants.update")]
    [RequestSizeLimit(MaxLogoRequestBytes)]
    public async Task<ActionResult<TenantDto>> UploadLogo(Guid tenantId, IFormFile file,
        CancellationToken ct)
    {
        if (file.Length is <= 0 or > MaxLogoBytes
            || !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "El logo debe ser una imagen JPG, PNG o WEBP de máximo 4 MB." });
        await using var stream = file.OpenReadStream();
        return Ok(await tenantService.UploadLogoAsync(tenantId, stream, file.FileName, ct));
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

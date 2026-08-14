using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Api.Authorization;
using Auraly.Api.Extensions;

namespace Auraly.Api.Controllers;

[ApiController]
[Route("api/v1/businesses/{businessId:guid}/integrations")]
[Authorize]
public class IntegrationsController : ControllerBase
{
    private readonly IIntegrationAdminService _service;

    public IntegrationsController(IIntegrationAdminService service)
    {
        _service = service;
    }

    [HttpGet]
    [PermissionAuthorize("business_config.read")]
    public async Task<ActionResult<IntegrationSettingsDto>> Get(Guid businessId, CancellationToken ct)
    {
        return Ok(await _service.GetSettingsAsync(User.GetTenantId(), businessId, ct));
    }

    [HttpPut("google-calendar")]
    [PermissionAuthorize("business_config.update")]
    public async Task<ActionResult<IntegrationSettingsDto>> UpdateGoogleCalendar(
        Guid businessId,
        [FromBody] UpdateGoogleCalendarIntegrationRequest request,
        CancellationToken ct)
    {
        return Ok(await _service.UpdateGoogleCalendarAsync(User.GetTenantId(), businessId, request, ct));
    }

    [HttpPut("wompi")]
    [PermissionAuthorize("business_config.update")]
    public async Task<ActionResult<IntegrationSettingsDto>> UpdateWompi(
        Guid businessId,
        [FromBody] UpdateWompiIntegrationRequest request,
        CancellationToken ct)
    {
        return Ok(await _service.UpdateWompiAsync(User.GetTenantId(), businessId, request, ct));
    }

    [HttpPut("operational-mode")]
    [PermissionAuthorize("business_config.update")]
    public async Task<ActionResult<IntegrationSettingsDto>> UpdateOperationalMode(
        Guid businessId,
        [FromBody] UpdateOperationalModeRequest request,
        CancellationToken ct)
    {
        return Ok(await _service.UpdateOperationalModeAsync(User.GetTenantId(), businessId, request, ct));
    }

    [HttpPut("commerce/siigo")]
    [PermissionAuthorize("business_config.update")]
    public async Task<ActionResult<IntegrationSettingsDto>> UpdateSiigoCommerce(
        Guid businessId,
        [FromBody] UpdateSiigoCommerceIntegrationRequest request,
        CancellationToken ct)
    {
        return Ok(await _service.UpdateSiigoCommerceAsync(User.GetTenantId(), businessId, request, ct));
    }

    [HttpPut("commerce/mantis")]
    [PermissionAuthorize("business_config.update")]
    public async Task<ActionResult<IntegrationSettingsDto>> UpdateMantis(
        Guid businessId,
        [FromBody] UpdateMantisIntegrationRequest request,
        CancellationToken ct)
    {
        return Ok(await _service.UpdateMantisAsync(User.GetTenantId(), businessId, request, ct));
    }

    [HttpPut("commerce/xion")]
    [PermissionAuthorize("business_config.update")]
    public async Task<ActionResult<IntegrationSettingsDto>> UpdateXion(
        Guid businessId,
        [FromBody] UpdateXionIntegrationRequest request,
        CancellationToken ct)
    {
        return Ok(await _service.UpdateXionAsync(User.GetTenantId(), businessId, request, ct));
    }
    [HttpGet("commerce/mantis/warehouses")]
    [PermissionAuthorize("business_config.read")]
    public async Task<ActionResult<IReadOnlyList<MantisChannelWarehouseDto>>> GetMantisWarehouses(
        Guid businessId,
        CancellationToken ct)
    {
        return Ok(await _service.GetMantisChannelWarehousesAsync(
            User.GetTenantId(), businessId, ct));
    }

    [HttpPut("commerce/mantis/warehouses")]
    [PermissionAuthorize("business_config.update")]
    public async Task<ActionResult<IReadOnlyList<MantisChannelWarehouseDto>>> UpdateMantisWarehouses(
        Guid businessId,
        [FromBody] UpdateMantisChannelWarehousesRequest request,
        CancellationToken ct)
    {
        return Ok(await _service.UpdateMantisChannelWarehousesAsync(
            User.GetTenantId(), businessId, request, ct));
    }
}

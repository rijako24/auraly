using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Auraly.Platform.Application.Common.DTOs;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Api.Authorization;
using Auraly.Api.Extensions;

namespace Auraly.Api.Controllers;

[ApiController]
[Route("api/v1/services")]
[Authorize]
public class ServicesController : ControllerBase
{
    private readonly IServiceAdminService _service;

    public ServicesController(IServiceAdminService service)
    {
        _service = service;
    }

    [HttpGet]
    [PermissionAuthorize("services.read")]
    public async Task<ActionResult<PagedResponse<ServiceDto>>> GetByBusiness(
        [FromQuery] Guid businessId, [FromQuery] PagedRequest request, CancellationToken ct)
    {
        return Ok(await _service.GetPagedByBusinessIdAsync(User.GetTenantId(), businessId, request, ct));
    }

    [HttpGet("{serviceId:guid}")]
    [PermissionAuthorize("services.read")]
    public async Task<ActionResult<ServiceDto>> GetById(Guid serviceId, CancellationToken ct)
    {
        return Ok(await _service.GetByIdAsync(User.GetTenantId(), serviceId, ct));
    }

    [HttpPost]
    [PermissionAuthorize("services.create")]
    public async Task<ActionResult<ServiceDto>> Create(
        [FromBody] CreateServiceRequest request, CancellationToken ct)
    {
        var result = await _service.CreateAsync(User.GetTenantId(), request, ct);
        return CreatedAtAction(nameof(GetById), new { serviceId = result.ServiceId }, result);
    }

    [HttpPut("{serviceId:guid}")]
    [PermissionAuthorize("services.update")]
    public async Task<ActionResult<ServiceDto>> Update(
        Guid serviceId, [FromBody] UpdateServiceRequest request, CancellationToken ct)
    {
        return Ok(await _service.UpdateAsync(User.GetTenantId(), serviceId, request, ct));
    }

    [HttpDelete("{serviceId:guid}")]
    [PermissionAuthorize("services.delete")]
    public async Task<IActionResult> Deactivate(Guid serviceId, CancellationToken ct)
    {
        await _service.DeactivateAsync(User.GetTenantId(), serviceId, ct);
        return NoContent();
    }
}

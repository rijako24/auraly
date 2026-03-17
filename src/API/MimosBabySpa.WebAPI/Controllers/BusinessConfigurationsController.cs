using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.WebAPI.Authorization;
using MimosBabySpa.WebAPI.Extensions;

namespace MimosBabySpa.WebAPI.Controllers;

[ApiController]
[Route("api/businesses/{businessId:guid}/configurations")]
[Authorize]
public class BusinessConfigurationsController : ControllerBase
{
    private readonly IBusinessConfigurationAdminService _service;

    public BusinessConfigurationsController(IBusinessConfigurationAdminService service)
    {
        _service = service;
    }

    [HttpGet]
    [PermissionAuthorize("business_config.read")]
    public async Task<IActionResult> Get(Guid businessId, CancellationToken ct)
    {
        return Ok(await _service.GetConfigurationAsync(User.GetTenantId(), businessId, ct));
    }

    [HttpPut]
    [PermissionAuthorize("business_config.update")]
    public async Task<IActionResult> Update(
        Guid businessId, [FromBody] UpdateBusinessConfigurationRequest request, CancellationToken ct)
    {
        return Ok(await _service.UpdateConfigurationAsync(User.GetTenantId(), businessId, request, ct));
    }
}

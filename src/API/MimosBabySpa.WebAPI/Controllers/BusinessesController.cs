using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MimosBabySpa.Application.Common.DTOs;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.WebAPI.Authorization;
using MimosBabySpa.WebAPI.Extensions;

namespace MimosBabySpa.WebAPI.Controllers;

[ApiController]
[Route("api/businesses")]
[Authorize]
public class BusinessesController : ControllerBase
{
    private readonly IBusinessAdminService _businessService;

    public BusinessesController(IBusinessAdminService businessService)
    {
        _businessService = businessService;
    }

    [HttpGet]
    [PermissionAuthorize("businesses.read")]
    public async Task<ActionResult<PagedResponse<BusinessDto>>> GetAll(
        [FromQuery] PagedRequest request, CancellationToken ct)
    {
        if (User.HasPermission("tenants.read"))
            return Ok(await _businessService.GetPagedAsync(request, ct));

        return Ok(await _businessService.GetPagedByTenantAsync(User.GetTenantId(), request, ct));
    }

    [HttpGet("{businessId:guid}")]
    [PermissionAuthorize("businesses.read")]
    public async Task<ActionResult<BusinessDto>> GetById(Guid businessId, CancellationToken ct)
    {
        return Ok(await _businessService.GetByIdAsync(businessId, ct));
    }

    [HttpPost]
    [PermissionAuthorize("businesses.create")]
    public async Task<ActionResult<BusinessDto>> Create(
        [FromBody] CreateBusinessRequest request, CancellationToken ct)
    {
        var result = await _businessService.CreateAsync(User.GetTenantId(), request, ct);
        return CreatedAtAction(nameof(GetById), new { businessId = result.BusinessId }, result);
    }

    [HttpPut("{businessId:guid}")]
    [PermissionAuthorize("businesses.update")]
    public async Task<ActionResult<BusinessDto>> Update(
        Guid businessId, [FromBody] UpdateBusinessRequest request, CancellationToken ct)
    {
        return Ok(await _businessService.UpdateAsync(businessId, request, ct));
    }

    [HttpDelete("{businessId:guid}")]
    [PermissionAuthorize("businesses.delete")]
    public async Task<IActionResult> Deactivate(Guid businessId, CancellationToken ct)
    {
        await _businessService.DeactivateAsync(businessId, ct);
        return NoContent();
    }
}


using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MimosBabySpa.Application.Common.DTOs;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using Auraly.Api.Authorization;
using Auraly.Api.Extensions;

namespace Auraly.Api.Controllers;

[ApiController]
[Route("api/v1/businesses")]
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
        return Ok(await _businessService.GetPagedByTenantAsync(
            User.GetTenantId(), request, ct));
    }

    [HttpGet("{businessId:guid}")]
    [PermissionAuthorize("businesses.read")]
    public async Task<ActionResult<BusinessDto>> GetById(Guid businessId, CancellationToken ct)
    {
        return Ok(await _businessService.GetByIdAsync(
            User.GetTenantId(), false, businessId, ct));
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
        return Ok(await _businessService.UpdateAsync(
            User.GetTenantId(), false, businessId, request, ct));
    }

    [HttpDelete("{businessId:guid}")]
    [PermissionAuthorize("businesses.delete")]
    public async Task<IActionResult> Deactivate(Guid businessId, CancellationToken ct)
    {
        await _businessService.DeactivateAsync(
            User.GetTenantId(), false, businessId, ct);
        return NoContent();
    }
}

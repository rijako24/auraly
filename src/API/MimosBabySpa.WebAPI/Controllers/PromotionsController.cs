using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MimosBabySpa.Application.Common.DTOs;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.WebAPI.Authorization;
using MimosBabySpa.WebAPI.Extensions;

namespace MimosBabySpa.WebAPI.Controllers;

[ApiController]
[Route("api/businesses/{businessId:guid}/promotions")]
[Authorize]
public sealed class PromotionsController : ControllerBase
{
    private readonly IPromotionAdminService _service;

    public PromotionsController(IPromotionAdminService service) => _service = service;

    [HttpGet]
    [PermissionAuthorize("promotions.read")]
    public async Task<ActionResult<PagedResponse<PromotionDto>>> GetByBusiness(
        Guid businessId,
        [FromQuery] PagedRequest request,
        CancellationToken ct)
    {
        return Ok(await _service.GetPagedByBusinessIdAsync(User.GetTenantId(), businessId, request, ct));
    }

    [HttpGet("{promotionId:guid}")]
    [PermissionAuthorize("promotions.read")]
    public async Task<ActionResult<PromotionDto>> GetById(Guid businessId, Guid promotionId, CancellationToken ct)
    {
        return Ok(await _service.GetByIdAsync(User.GetTenantId(), businessId, promotionId, ct));
    }

    [HttpPost]
    [PermissionAuthorize("promotions.create")]
    public async Task<ActionResult<PromotionDto>> Create(
        Guid businessId,
        [FromBody] CreatePromotionRequest request,
        CancellationToken ct)
    {
        var normalized = request with { BusinessId = businessId };
        var result = await _service.CreateAsync(User.GetTenantId(), normalized, ct);
        return CreatedAtAction(nameof(GetById), new { businessId, promotionId = result.PromotionId }, result);
    }

    [HttpPut("{promotionId:guid}")]
    [PermissionAuthorize("promotions.update")]
    public async Task<ActionResult<PromotionDto>> Update(
        Guid businessId,
        Guid promotionId,
        [FromBody] UpdatePromotionRequest request,
        CancellationToken ct)
    {
        return Ok(await _service.UpdateAsync(User.GetTenantId(), businessId, promotionId, request, ct));
    }

    [HttpDelete("{promotionId:guid}")]
    [PermissionAuthorize("promotions.delete")]
    public async Task<IActionResult> Deactivate(Guid businessId, Guid promotionId, CancellationToken ct)
    {
        await _service.DeactivateAsync(User.GetTenantId(), businessId, promotionId, ct);
        return NoContent();
    }
}

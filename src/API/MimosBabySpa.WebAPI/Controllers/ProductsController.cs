using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MimosBabySpa.Application.Common.DTOs;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.WebAPI.Authorization;
using MimosBabySpa.WebAPI.Extensions;

namespace MimosBabySpa.WebAPI.Controllers;

[ApiController]
[Route("api/businesses/{businessId:guid}/products")]
[Authorize]
public class ProductsController : ControllerBase
{
    private readonly IProductAdminService _service;

    public ProductsController(IProductAdminService service) => _service = service;

    [HttpGet]
    [PermissionAuthorize("products.read")]
    public async Task<ActionResult<PagedResponse<ProductDto>>> GetByBusiness(
        Guid businessId,
        [FromQuery] PagedRequest request,
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default) =>
        Ok(await _service.GetPagedByBusinessIdAsync(
            User.GetTenantId(),
            businessId,
            request,
            includeInactive,
            ct));

    [HttpPatch("{productId:guid}/status")]
    [PermissionAuthorize("products.update")]
    public async Task<ActionResult<ProductDto>> UpdateStatus(
        Guid businessId,
        Guid productId,
        [FromBody] UpdateProductStatusRequest request,
        CancellationToken ct) =>
        Ok(await _service.UpdateStatusAsync(
            User.GetTenantId(),
            businessId,
            productId,
            request,
            ct));

    [HttpPut("{productId:guid}")]
    [PermissionAuthorize("products.update")]
    public async Task<ActionResult<ProductDto>> Update(
        Guid businessId,
        Guid productId,
        [FromBody] UpdateProductRequest request,
        CancellationToken ct) =>
        Ok(await _service.UpdateAsync(
            User.GetTenantId(),
            businessId,
            productId,
            request,
            ct));
}

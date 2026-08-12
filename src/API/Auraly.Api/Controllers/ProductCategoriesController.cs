using Auraly.Api.Authorization;
using Auraly.Api.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;

namespace Auraly.Api.Controllers;

[ApiController]
[Route("api/v1/businesses/{businessId:guid}/product-categories")]
[Authorize]
public sealed class ProductCategoriesController(IProductAdminService service) : ControllerBase
{
    [HttpGet]
    [PermissionAuthorize("products.read")]
    public async Task<ActionResult<IReadOnlyList<ProductCategoryAdminDto>>> List(
        Guid businessId,
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default) =>
        Ok(await service.GetCategoriesAsync(User.GetTenantId(), businessId, includeInactive, ct));

    [HttpPost]
    [PermissionAuthorize("products.update")]
    public async Task<ActionResult<ProductCategoryAdminDto>> Create(
        Guid businessId,
        [FromBody] CreateProductCategoryRequest request,
        CancellationToken ct = default)
    {
        var result = await service.CreateCategoryAsync(User.GetTenantId(), businessId, request, ct);
        return Created($"/api/v1/businesses/{businessId}/product-categories/{result.ProductCategoryId}", result);
    }

    [HttpPut("{productCategoryId:guid}")]
    [PermissionAuthorize("products.update")]
    public async Task<ActionResult<ProductCategoryAdminDto>> Update(
        Guid businessId,
        Guid productCategoryId,
        [FromBody] UpdateProductCategoryRequest request,
        CancellationToken ct = default) =>
        Ok(await service.UpdateCategoryAsync(
            User.GetTenantId(), businessId, productCategoryId, request, ct));
}
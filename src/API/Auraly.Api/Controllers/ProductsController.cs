using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Auraly.Platform.Application.Common.DTOs;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Api.Authorization;
using Auraly.Api.Extensions;

namespace Auraly.Api.Controllers;

[ApiController]
[Route("api/v1/businesses/{businessId:guid}/products")]
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

    [HttpGet("{productId:guid}/search-terms")]
    [PermissionAuthorize("products.read")]
    public async Task<ActionResult<IReadOnlyList<string>>> GetSearchTerms(
        Guid businessId,
        Guid productId,
        CancellationToken ct) =>
        Ok(await _service.GetSearchTermsAsync(
            User.GetTenantId(),
            businessId,
            productId,
            ct));
}

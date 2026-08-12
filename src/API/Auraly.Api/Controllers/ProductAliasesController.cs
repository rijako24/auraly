using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.Identity.Interfaces;
using Auraly.Api.Authorization;
using Auraly.Api.Extensions;

namespace Auraly.Api.Controllers;

[ApiController]
[Route("api/v1/businesses/{businessId:guid}/products")]
[Authorize]
public sealed class ProductAliasesController : ControllerBase
{
    private readonly IProductAliasAdminService _service;

    public ProductAliasesController(IProductAliasAdminService service) => _service = service;

    [HttpGet("{productId:guid}/aliases")]
    [PermissionAuthorize("products.read")]
    public async Task<ActionResult<IReadOnlyList<ProductAliasDto>>> Get(
        Guid businessId, Guid productId, CancellationToken ct) =>
        Ok(await _service.GetByProductAsync(User.GetTenantId(), businessId, productId, ct));

    [HttpPut("{productId:guid}/aliases/{productAliasId:guid}/review")]
    [PermissionAuthorize("products.update")]
    public async Task<ActionResult<ProductAliasDto>> Review(
        Guid businessId,
        Guid productId,
        Guid productAliasId,
        [FromBody] ReviewProductAliasRequest request,
        CancellationToken ct) =>
        Ok(await _service.ReviewAsync(
            User.GetTenantId(), businessId, productId, productAliasId, request, ct));

    [HttpPost("{productId:guid}/aliases/{productAliasId:guid}/promote")]
    [PermissionAuthorize("products.update")]
    public async Task<ActionResult<ProductAliasDto>> Promote(
        Guid businessId,
        Guid productId,
        Guid productAliasId,
        [FromBody] PromoteProductAliasRequest request,
        CancellationToken ct) =>
        Ok(await _service.PromoteAsync(
            User.GetTenantId(), businessId, productId, productAliasId, request, ct));

    [HttpPost("aliases/import")]
    [PermissionAuthorize("products.update")]
    public async Task<ActionResult<ProductAliasImportResult>> Import(
        Guid businessId, [FromBody] ProductAliasImportRequest request, CancellationToken ct) =>
        Ok(await _service.ImportAsync(User.GetTenantId(), businessId, request, ct));
}

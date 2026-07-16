using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.WebAPI.Authorization;
using MimosBabySpa.WebAPI.Extensions;

namespace MimosBabySpa.WebAPI.Controllers;

[ApiController]
[Route("api/businesses/{businessId:guid}/products")]
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

    [HttpPost("aliases/import")]
    [PermissionAuthorize("products.update")]
    public async Task<ActionResult<ProductAliasImportResult>> Import(
        Guid businessId, [FromBody] ProductAliasImportRequest request, CancellationToken ct) =>
        Ok(await _service.ImportAsync(User.GetTenantId(), businessId, request, ct));
}

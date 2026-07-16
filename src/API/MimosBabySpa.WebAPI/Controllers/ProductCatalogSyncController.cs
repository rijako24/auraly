using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.WebAPI.Authorization;
using MimosBabySpa.WebAPI.Extensions;

namespace MimosBabySpa.WebAPI.Controllers;

[ApiController]
[Route("api/businesses/{businessId:guid}/products/catalog")]
[Authorize]
public sealed class ProductCatalogSyncController : ControllerBase
{
    private readonly IProductCatalogAdminService _service;
    public ProductCatalogSyncController(IProductCatalogAdminService service) => _service = service;

    [HttpPost("sync")]
    [PermissionAuthorize("products.update")]
    public async Task<ActionResult<ProductCatalogSyncResult>> Sync(
        Guid businessId, [FromBody] ProductCatalogSyncRequest request, CancellationToken ct) =>
        Ok(await _service.SyncAsync(User.GetTenantId(), businessId, request, ct));
}

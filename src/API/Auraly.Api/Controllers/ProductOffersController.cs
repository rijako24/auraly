using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Api.Authorization;
using Auraly.Api.Extensions;

namespace Auraly.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/businesses/{businessId:guid}/products/{productId:guid}")]
public sealed class ProductOffersController : ControllerBase
{
    private const long MaxImageBytes = 8 * 1024 * 1024;
    private readonly IProductOfferAdminService _service;

    public ProductOffersController(IProductOfferAdminService service) => _service = service;

    [HttpGet("offers")]
    [PermissionAuthorize("products.read")]
    public async Task<ActionResult<IReadOnlyList<ProductOfferDto>>> GetOffers(
        Guid businessId, Guid productId, CancellationToken ct) =>
        Ok(await _service.GetOffersAsync(User.GetTenantId(), businessId, productId, ct));

    [HttpPost("offers")]
    [PermissionAuthorize("products.update")]
    public async Task<ActionResult<ProductOfferDto>> CreateOffer(
        Guid businessId, Guid productId, SaveProductOfferRequest request, CancellationToken ct) =>
        Ok(await _service.CreateOfferAsync(User.GetTenantId(), businessId, productId, request, ct));

    [HttpPut("offers/{productOfferId:guid}")]
    [PermissionAuthorize("products.update")]
    public async Task<ActionResult<ProductOfferDto>> UpdateOffer(
        Guid businessId, Guid productId, Guid productOfferId, SaveProductOfferRequest request, CancellationToken ct) =>
        Ok(await _service.UpdateOfferAsync(
            User.GetTenantId(), businessId, productId, productOfferId, request, ct));

    [HttpGet("images")]
    [PermissionAuthorize("products.read")]
    public async Task<ActionResult<IReadOnlyList<ProductImageDto>>> GetImages(
        Guid businessId, Guid productId, CancellationToken ct) =>
        Ok(await _service.GetImagesAsync(User.GetTenantId(), businessId, productId, ct));

    [HttpPost("images/url")]
    [PermissionAuthorize("products.update")]
    public async Task<ActionResult<ProductImageDto>> AddImageUrl(
        Guid businessId, Guid productId, AddProductImageUrlRequest request, CancellationToken ct) =>
        Ok(await _service.AddImageUrlAsync(User.GetTenantId(), businessId, productId, request, ct));

    [HttpPost("images/upload")]
    [PermissionAuthorize("products.update")]
    [RequestSizeLimit(MaxImageBytes)]
    public async Task<ActionResult<ProductImageDto>> UploadImage(
        Guid businessId,
        Guid productId,
        IFormFile file,
        [FromForm] Guid? productOfferId,
        [FromForm] string? altText,
        [FromForm] bool isPrimary = false,
        CancellationToken ct = default)
    {
        if (file.Length == 0 || file.Length > MaxImageBytes)
            return BadRequest(new { error = "La imagen debe pesar entre 1 byte y 8 MB." });
        await using var stream = file.OpenReadStream();
        return Ok(await _service.UploadImageAsync(
            User.GetTenantId(), businessId, productId, productOfferId,
            stream, file.FileName, altText, isPrimary, ct));
    }

    [HttpDelete("images/{productImageId:guid}")]
    [PermissionAuthorize("products.update")]
    public async Task<IActionResult> DeleteImage(
        Guid businessId, Guid productId, Guid productImageId, CancellationToken ct)
    {
        await _service.DeleteImageAsync(User.GetTenantId(), businessId, productId, productImageId, ct);
        return NoContent();
    }

    [HttpPut("images/{productImageId:guid}/primary")]
    [PermissionAuthorize("products.update")]
    public async Task<ActionResult<ProductImageDto>> SetPrimaryImage(
        Guid businessId, Guid productId, Guid productImageId, CancellationToken ct) =>
        Ok(await _service.SetPrimaryImageAsync(
            User.GetTenantId(), businessId, productId, productImageId, ct));

}

using MimosBabySpa.Application.Identity.DTOs;

namespace MimosBabySpa.Application.Identity.Interfaces;

public interface IProductOfferAdminService
{
    Task<IReadOnlyList<ProductOfferDto>> GetOffersAsync(
        Guid tenantId, Guid businessId, Guid productId, CancellationToken ct = default);
    Task<ProductOfferDto> CreateOfferAsync(
        Guid tenantId, Guid businessId, Guid productId, SaveProductOfferRequest request, CancellationToken ct = default);
    Task<ProductOfferDto> UpdateOfferAsync(
        Guid tenantId, Guid businessId, Guid productId, Guid productOfferId, SaveProductOfferRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<ProductImageDto>> GetImagesAsync(
        Guid tenantId, Guid businessId, Guid productId, CancellationToken ct = default);
    Task<ProductImageDto> AddImageUrlAsync(
        Guid tenantId, Guid businessId, Guid productId, AddProductImageUrlRequest request, CancellationToken ct = default);
    Task<ProductImageDto> UploadImageAsync(
        Guid tenantId, Guid businessId, Guid productId, Guid? productOfferId, Stream stream,
        string fileName, string? altText, bool isPrimary, CancellationToken ct = default);
    Task<ProductImageDto> SetPrimaryImageAsync(
        Guid tenantId, Guid businessId, Guid productId, Guid productImageId, CancellationToken ct = default);
    Task DeleteImageAsync(
        Guid tenantId, Guid businessId, Guid productId, Guid productImageId, CancellationToken ct = default);
}

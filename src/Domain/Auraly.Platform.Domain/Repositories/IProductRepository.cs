using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Domain.Repositories;

public sealed record ProductListFilter(
    IReadOnlyCollection<Guid>? CategoryIds = null,
    Guid? SupplierId = null,
    Guid? BrandId = null,
    bool? ManagesInventory = null,
    bool? AllowsFractionalSale = null,
    bool? IsWeighable = null);

public sealed record ProductPricePublication(Product Product, decimal Amount, string Currency);

public interface IProductRepository
{
    Task<IReadOnlyList<Product>> SearchAsync(
        Guid businessId,
        string? query,
        string? category,
        int limit,
        CancellationToken ct = default,
        bool includeInactive = false);
    Task<(IReadOnlyList<Product> Items, int TotalCount)> SearchPageAsync(
        Guid businessId,
        string? query,
        string? category,
        int page,
        int pageSize,
        CancellationToken ct = default,
        bool includeInactive = false);


    Task<(IReadOnlyList<Product> Items, int TotalCount)> GetPagedByBusinessIdAsync(
        Guid businessId,
        int page,
        int pageSize,
        string? search = null,
        bool includeInactive = false,
        ProductListFilter? filter = null,
        CancellationToken ct = default);

    Task<Product?> GetByIdAsync(Guid businessId, Guid productId, CancellationToken ct = default);
    Task<Product?> GetByExternalIdAsync(Guid businessId, Guid integrationConnectionId, string externalProductId, CancellationToken ct = default);
    Task<IReadOnlyList<Product>> GetByExternalIdsAsync(Guid businessId, Guid integrationConnectionId, IReadOnlyCollection<string> externalProductIds, CancellationToken ct = default);
    Task<Product?> GetByAnyExternalIdAsync(Guid businessId, string externalProductId, CancellationToken ct = default);
    Task<Product?> GetBySkuAsync(Guid businessId, string sku, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetSearchTermsAsync(Guid businessId, Guid productId, CancellationToken ct = default);
    Task<IReadOnlyList<Product>> SearchByIndexTermsAsync(Guid businessId, IReadOnlyCollection<string> terms, int limit, CancellationToken ct = default);
    Task<IReadOnlyList<Product>> GetLinkedFamilyAsync(Guid businessId, IReadOnlyCollection<Guid> productIds, CancellationToken ct = default);
    Task<IReadOnlyList<Product>> GetIdentityCatalogAsync(Guid businessId, CancellationToken ct = default);
    Task<bool> HasAnyIdentityAsync(Guid businessId, Guid integrationConnectionId, CancellationToken ct = default);
    Task ReplaceSearchTermsAsync(Product product, CancellationToken ct = default);
    Task ReplaceSearchTermsAsync(IReadOnlyCollection<Product> products, CancellationToken ct = default);
    Task UpdateCategoryNameAsync(Guid businessId, Guid productCategoryId, string categoryName, CancellationToken ct = default);
    Task<Product> CreateAsync(Product product, CancellationToken ct = default);
    Task CreateManyAsync(IReadOnlyCollection<Product> products, CancellationToken ct = default);
    Task<Product> UpdateAsync(Product product, CancellationToken ct = default);
    Task UpdateManyAsync(IReadOnlyCollection<Product> products, CancellationToken ct = default);
    Task PublishPriceAsync(Product product, decimal amount, string currency, CancellationToken ct = default);
    Task PublishPricesAsync(IReadOnlyCollection<ProductPricePublication> publications, CancellationToken ct = default);
    Task<IReadOnlyList<ProductOffer>> SearchOffersAsync(Guid businessId, string productQuery, string condition, CancellationToken ct = default);
    Task<IReadOnlyList<ProductOffer>> GetOffersAsync(Guid businessId, Guid productId, CancellationToken ct = default);
    Task<ProductOffer?> GetOfferByIdAsync(Guid businessId, Guid productOfferId, CancellationToken ct = default);
    Task<ProductOffer> CreateOfferAsync(ProductOffer offer, CancellationToken ct = default);
    Task<ProductOffer> UpdateOfferAsync(ProductOffer offer, CancellationToken ct = default);
    Task<IReadOnlyList<ProductImage>> GetImagesAsync(Guid businessId, Guid productId, CancellationToken ct = default);
    Task<IReadOnlyList<ProductImage>> GetTrackedImagesAsync(Guid businessId, Guid productId, CancellationToken ct = default);
    Task<ProductImage?> GetImageByIdAsync(Guid businessId, Guid productImageId, CancellationToken ct = default);
    Task<ProductImage> CreateImageAsync(ProductImage image, CancellationToken ct = default);
    Task DeleteImageAsync(ProductImage image, CancellationToken ct = default);
}

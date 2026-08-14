using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Infrastructure.Repositories;

public sealed partial class ProductRepository
{
    public async Task<IReadOnlyList<ProductOffer>> SearchOffersAsync(
        Guid businessId,
        string productQuery,
        string condition,
        CancellationToken ct = default)
    {
        var query = _context.ProductOffers
            .AsNoTracking()
            .Include(value => value.Product)
                .ThenInclude(value => value.Images)
            .Include(value => value.Images)
            .Where(value =>
                value.BusinessId == businessId
                && value.Product.IsActive
                && value.IsActive
                && value.IsAvailable
                && value.Condition == condition);

        foreach (var term in Domain.Catalog.CatalogSearchText.GetSearchTerms(productQuery))
        {
            var searchTerm = term;
            query = query.Where(value =>
                value.Product.Name.Contains(searchTerm)
                || value.Product.Sku != null && value.Product.Sku.Contains(searchTerm)
                || value.Product.Description != null && value.Product.Description.Contains(searchTerm));
        }

        return await query
            .OrderBy(value => value.UnitPrice)
            .ThenBy(value => value.StorageGb)
            .Take(10)
            .ToListAsync(ct);
    }

    public Task<IReadOnlyList<ProductOffer>> GetOffersAsync(
        Guid businessId,
        Guid productId,
        CancellationToken ct = default) =>
        _context.ProductOffers
            .AsNoTracking()
            .Where(value => value.BusinessId == businessId && value.ProductId == productId)
            .OrderBy(value => value.Condition)
            .ThenBy(value => value.StorageGb)
            .ToListAsync(ct)
            .ContinueWith<IReadOnlyList<ProductOffer>>(task => task.Result, ct);

    public Task<ProductOffer?> GetOfferByIdAsync(
        Guid businessId,
        Guid productOfferId,
        CancellationToken ct = default) =>
        _context.ProductOffers.FirstOrDefaultAsync(
            value => value.BusinessId == businessId && value.ProductOfferId == productOfferId,
            ct);

    public Task<ProductOffer> CreateOfferAsync(ProductOffer offer, CancellationToken ct = default)
    {
        _context.ProductOffers.Add(offer);
        return Task.FromResult(offer);
    }

    public Task<ProductOffer> UpdateOfferAsync(ProductOffer offer, CancellationToken ct = default)
    {
        _context.ProductOffers.Update(offer);
        return Task.FromResult(offer);
    }

    public Task<IReadOnlyList<ProductImage>> GetImagesAsync(
        Guid businessId,
        Guid productId,
        CancellationToken ct = default) =>
        _context.ProductImages
            .AsNoTracking()
            .Where(value => value.BusinessId == businessId && value.ProductId == productId)
            .OrderByDescending(value => value.IsPrimary)
            .ThenBy(value => value.DisplayOrder)
            .ToListAsync(ct)
            .ContinueWith<IReadOnlyList<ProductImage>>(task => task.Result, ct);

    public Task<ProductImage?> GetImageByIdAsync(
        Guid businessId,
        Guid productImageId,
        CancellationToken ct = default) =>
        _context.ProductImages.FirstOrDefaultAsync(
            value => value.BusinessId == businessId && value.ProductImageId == productImageId,
            ct);

    public Task<ProductImage> CreateImageAsync(ProductImage image, CancellationToken ct = default)
    {
        _context.ProductImages.Add(image);
        return Task.FromResult(image);
    }

    public Task DeleteImageAsync(ProductImage image, CancellationToken ct = default)
    {
        _context.ProductImages.Remove(image);
        return Task.CompletedTask;
    }
}

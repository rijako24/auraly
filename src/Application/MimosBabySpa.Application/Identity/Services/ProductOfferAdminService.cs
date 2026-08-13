using MimosBabySpa.Application.Common.Exceptions;
using MimosBabySpa.Application.Common.Interfaces;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Identity.Services;

public sealed class ProductOfferAdminService : IProductOfferAdminService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IBlobStorageService _blobStorage;
    private readonly IAuditService _audit;
    private readonly IMediaUrlResolver _mediaUrlResolver;

    public ProductOfferAdminService(
        IUnitOfWork unitOfWork,
        IBlobStorageService blobStorage,
        IAuditService audit,
        IMediaUrlResolver mediaUrlResolver)
    {
        _unitOfWork = unitOfWork;
        _blobStorage = blobStorage;
        _audit = audit;
        _mediaUrlResolver = mediaUrlResolver;
    }

    public async Task<IReadOnlyList<ProductOfferDto>> GetOffersAsync(
        Guid tenantId, Guid businessId, Guid productId, CancellationToken ct = default)
    {
        await EnsureProductAsync(tenantId, businessId, productId, ct);
        return (await _unitOfWork.Products.GetOffersAsync(businessId, productId, ct))
            .Select(MapOffer)
            .ToList();
    }

    public async Task<ProductOfferDto> CreateOfferAsync(
        Guid tenantId,
        Guid businessId,
        Guid productId,
        SaveProductOfferRequest request,
        CancellationToken ct = default)
    {
        await EnsureProductAsync(tenantId, businessId, productId, ct);
        var normalized = Normalize(request);
        var offer = new ProductOffer
        {
            ProductOfferId = Guid.NewGuid(),
            ProductId = productId,
            BusinessId = businessId,
            CreatedAt = DateTime.UtcNow
        };
        Apply(offer, normalized);
        await _unitOfWork.Products.CreateOfferAsync(offer, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        await _audit.LogAsync("Create", "ProductOffer", offer.ProductOfferId.ToString(), null, MapOffer(offer), ct);
        return MapOffer(offer);
    }

    public async Task<ProductOfferDto> UpdateOfferAsync(
        Guid tenantId,
        Guid businessId,
        Guid productId,
        Guid productOfferId,
        SaveProductOfferRequest request,
        CancellationToken ct = default)
    {
        await EnsureProductAsync(tenantId, businessId, productId, ct);
        var offer = await _unitOfWork.Products.GetOfferByIdAsync(businessId, productOfferId, ct)
            ?? throw new NotFoundException(nameof(ProductOffer), productOfferId);
        if (offer.ProductId != productId)
            throw new NotFoundException(nameof(ProductOffer), productOfferId);
        var before = MapOffer(offer);
        Apply(offer, Normalize(request));
        offer.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Products.UpdateOfferAsync(offer, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        await _audit.LogAsync("Update", "ProductOffer", offer.ProductOfferId.ToString(), before, MapOffer(offer), ct);
        return MapOffer(offer);
    }

    public async Task<IReadOnlyList<ProductImageDto>> GetImagesAsync(
        Guid tenantId, Guid businessId, Guid productId, CancellationToken ct = default)
    {
        await EnsureProductAsync(tenantId, businessId, productId, ct);
        var images = await _unitOfWork.Products.GetImagesAsync(businessId, productId, ct);
        var result = new List<ProductImageDto>(images.Count);
        foreach (var image in images)
        {
            var resolvedUrl = await _mediaUrlResolver.ResolveAsync(businessId, image.MediaUrl, ct);
            result.Add(MapImage(image, resolvedUrl));
        }
        return result;
    }

    public async Task<ProductImageDto> AddImageUrlAsync(
        Guid tenantId,
        Guid businessId,
        Guid productId,
        AddProductImageUrlRequest request,
        CancellationToken ct = default)
    {
        await EnsureProductAsync(tenantId, businessId, productId, ct);
        if (!Uri.TryCreate(request.MediaUrl?.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
            throw new DomainValidationException("MediaUrl", "La imagen debe usar una URL HTTPS valida.");
        await EnsureOfferBelongsToProductAsync(businessId, productId, request.ProductOfferId, ct);
        return await AddImageAsync(
            businessId, productId, request.ProductOfferId, uri.ToString(), request.AltText,
            request.DisplayOrder, request.IsPrimary, ct);
    }

    public async Task<ProductImageDto> UploadImageAsync(
        Guid tenantId,
        Guid businessId,
        Guid productId,
        Guid? productOfferId,
        Stream stream,
        string fileName,
        string? altText,
        bool isPrimary,
        CancellationToken ct = default)
    {
        await EnsureProductAsync(tenantId, businessId, productId, ct);
        await EnsureOfferBelongsToProductAsync(businessId, productId, productOfferId, ct);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is not (".jpg" or ".jpeg" or ".png" or ".webp"))
            throw new DomainValidationException("file", "Use una imagen JPG, PNG o WEBP.");
        var blobName = $"products/{productId:N}/{Guid.NewGuid():N}{extension}";
        var url = await _blobStorage.UploadImageAsync(businessId, stream, blobName);
        return await AddImageAsync(businessId, productId, productOfferId, url, altText, 0, isPrimary, ct);
    }

    public async Task DeleteImageAsync(
        Guid tenantId,
        Guid businessId,
        Guid productId,
        Guid productImageId,
        CancellationToken ct = default)
    {
        await EnsureProductAsync(tenantId, businessId, productId, ct);
        var image = await _unitOfWork.Products.GetImageByIdAsync(businessId, productImageId, ct)
            ?? throw new NotFoundException(nameof(ProductImage), productImageId);
        if (image.ProductId != productId)
            throw new NotFoundException(nameof(ProductImage), productImageId);
        if (image.IsPrimary)
        {
            var replacement = (await _unitOfWork.Products.GetImagesAsync(businessId, productId, ct))
                .Where(candidate => candidate.ProductImageId != productImageId && candidate.IsActive)
                .OrderBy(candidate => candidate.DisplayOrder)
                .FirstOrDefault();
            var trackedReplacement = replacement is null ? null
                : await _unitOfWork.Products.GetImageByIdAsync(businessId, replacement.ProductImageId, ct);
            if (trackedReplacement is not null)
                trackedReplacement.IsPrimary = true;
        }

        await _unitOfWork.Products.DeleteImageAsync(image, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        await _audit.LogAsync("Delete", "ProductImage", productImageId.ToString(), MapImage(image), null, ct);
    }


    public async Task<ProductImageDto> SetPrimaryImageAsync(
        Guid tenantId,
        Guid businessId,
        Guid productId,
        Guid productImageId,
        CancellationToken ct = default)
    {
        await EnsureProductAsync(tenantId, businessId, productId, ct);
        var selected = await _unitOfWork.Products.GetImageByIdAsync(businessId, productImageId, ct)
            ?? throw new NotFoundException(nameof(ProductImage), productImageId);
        if (selected.ProductId != productId)
            throw new NotFoundException(nameof(ProductImage), productImageId);

        var before = MapImage(selected);
        var images = await _unitOfWork.Products.GetImagesAsync(businessId, productId, ct);
        foreach (var image in images)
        {
            var tracked = image.ProductImageId == selected.ProductImageId
                ? selected
                : await _unitOfWork.Products.GetImageByIdAsync(businessId, image.ProductImageId, ct);
            if (tracked is null) continue;
            tracked.IsPrimary = tracked.ProductImageId == selected.ProductImageId;
            tracked.UpdatedAt = DateTime.UtcNow;
        }

        await _unitOfWork.SaveChangesAsync(ct);
        await _audit.LogAsync("SetPrimary", "ProductImage", productImageId.ToString(), before, MapImage(selected), ct);
        return MapImage(selected);
    }
    private async Task<ProductImageDto> AddImageAsync(
        Guid businessId,
        Guid productId,
        Guid? offerId,
        string url,
        string? altText,
        int displayOrder,
        bool isPrimary,
        CancellationToken ct)
    {
        if (isPrimary)
        {
            var existing = await _unitOfWork.Products.GetImagesAsync(businessId, productId, ct);
            foreach (var image in existing.Where(value => value.IsPrimary))
            {
                var tracked = await _unitOfWork.Products.GetImageByIdAsync(businessId, image.ProductImageId, ct);
                if (tracked is not null)
                {
                    tracked.IsPrimary = false;
                    tracked.UpdatedAt = DateTime.UtcNow;
                }
            }
        }
        var entity = new ProductImage
        {
            ProductImageId = Guid.NewGuid(),
            ProductId = productId,
            BusinessId = businessId,
            ProductOfferId = offerId,
            MediaUrl = url,
            AltText = string.IsNullOrWhiteSpace(altText) ? null : altText.Trim(),
            DisplayOrder = Math.Max(0, displayOrder),
            IsPrimary = isPrimary,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        await _unitOfWork.Products.CreateImageAsync(entity, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        await _audit.LogAsync("Create", "ProductImage", entity.ProductImageId.ToString(), null, MapImage(entity), ct);
        return MapImage(entity);
    }

    private async Task EnsureProductAsync(
        Guid tenantId, Guid businessId, Guid productId, CancellationToken ct)
    {
        var business = await _unitOfWork.Businesses.GetByIdAsync(businessId)
            ?? throw new NotFoundException(nameof(Business), businessId);
        if (business.TenantId != tenantId)
            throw new NotFoundException(nameof(Business), businessId);
        _ = await _unitOfWork.Products.GetByIdAsync(businessId, productId, ct)
            ?? throw new NotFoundException(nameof(Product), productId);
    }

    private async Task EnsureOfferBelongsToProductAsync(
        Guid businessId, Guid productId, Guid? offerId, CancellationToken ct)
    {
        if (!offerId.HasValue)
            return;
        var offer = await _unitOfWork.Products.GetOfferByIdAsync(businessId, offerId.Value, ct)
            ?? throw new NotFoundException(nameof(ProductOffer), offerId.Value);
        if (offer.ProductId != productId)
            throw new NotFoundException(nameof(ProductOffer), offerId.Value);
    }

    private static SaveProductOfferRequest Normalize(SaveProductOfferRequest request)
    {
        var condition = request.Condition?.Trim().ToLowerInvariant() ?? string.Empty;
        if (condition is not ("new" or "used" or "refurbished"))
            throw new DomainValidationException("Condition", "La condicion debe ser new, used o refurbished.");
        if (request.StorageGb <= 0)
            throw new DomainValidationException("StorageGb", "La capacidad debe ser mayor que cero.");
        if (request.UnitPrice < 0)
            throw new DomainValidationException("UnitPrice", "El precio no puede ser negativo.");
        var currency = request.Currency?.Trim().ToUpperInvariant() ?? string.Empty;
        if (currency.Length != 3 || !currency.All(char.IsLetter))
            throw new DomainValidationException("Currency", "La moneda debe tener tres letras.");
        if (request.MinimumBatteryHealthPercent is < 0 or > 100)
            throw new DomainValidationException("MinimumBatteryHealthPercent",
                "El porcentaje debe estar entre 0 y 100.");
        return request with
        {
            Condition = condition,
            Color = string.IsNullOrWhiteSpace(request.Color) ? null : request.Color.Trim(),
            VariantLabel = string.IsNullOrWhiteSpace(request.VariantLabel) ? null : request.VariantLabel.Trim(),
            Currency = currency,
            PriceSourceUrl = string.IsNullOrWhiteSpace(request.PriceSourceUrl) ? null : request.PriceSourceUrl.Trim()
        };
    }

    private static void Apply(ProductOffer offer, SaveProductOfferRequest request)
    {
        offer.Condition = request.Condition;
        offer.StorageGb = request.StorageGb;
        offer.Color = request.Color;
        offer.VariantLabel = request.VariantLabel;
        offer.UnitPrice = request.UnitPrice;
        offer.Currency = request.Currency;
        offer.MinimumBatteryHealthPercent = request.MinimumBatteryHealthPercent;
        offer.IsAvailable = request.IsAvailable;
        offer.IsActive = request.IsActive;
        offer.PriceSourceUrl = request.PriceSourceUrl;
        offer.PriceObservedAtUtc = request.PriceObservedAtUtc;
    }

    private static ProductOfferDto MapOffer(ProductOffer value) => new(
        value.ProductOfferId, value.ProductId, value.Condition, value.StorageGb, value.Color, value.VariantLabel,
        value.UnitPrice, value.Currency, value.MinimumBatteryHealthPercent, value.IsAvailable,
        value.IsActive, value.PriceSourceUrl, value.PriceObservedAtUtc, value.CreatedAt, value.UpdatedAt);

    private static ProductImageDto MapImage(ProductImage value, string? resolvedMediaUrl = null) => new(
        value.ProductImageId, value.ProductId, value.ProductOfferId, resolvedMediaUrl ?? value.MediaUrl, value.AltText,
        value.DisplayOrder, value.IsPrimary, value.IsActive);
}

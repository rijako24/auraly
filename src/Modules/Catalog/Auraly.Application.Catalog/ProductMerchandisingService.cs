using Auraly.BuildingBlocks.Application.Synchronization;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Catalog;

namespace Auraly.Application.Catalog;

public interface IProductMerchandisingStore
{
    Task<IReadOnlyList<ProductBrandSummary>> ListBrandsAsync(CatalogUserIdentity user, bool includeInactive, CancellationToken ct);
    Task<ProductBrandSummary> SaveBrandAsync(CatalogUserIdentity user, Guid? id, SaveProductBrandRequest request, DateTimeOffset now, CancellationToken ct);
    Task<IReadOnlyList<ProductUnitSummary>> ListUnitsAsync(CatalogUserIdentity user, bool includeInactive, CancellationToken ct);
    Task<ProductUnitSummary> SaveUnitAsync(CatalogUserIdentity user, Guid? id, SaveProductUnitRequest request, DateTimeOffset now, CancellationToken ct);
    Task<ProductMerchandisingConfiguration?> GetAsync(CatalogUserIdentity user, Guid productId, CancellationToken ct);
    Task<ProductMerchandisingConfiguration> SaveAsync(CatalogUserIdentity user, Guid productId, SaveProductMerchandisingRequest request, DateTimeOffset now, CancellationToken ct);
}

public sealed class ProductMerchandisingService(
    IProductMerchandisingStore store,
    TimeProvider timeProvider,
    IPosSynchronizationOutboxDispatcher synchronization)
{
    public Task<IReadOnlyList<ProductBrandSummary>> ListBrandsAsync(CatalogUserIdentity user, bool includeInactive, CancellationToken ct)
    {
        Require(user, CatalogPermissionCodes.Read);
        return store.ListBrandsAsync(user, includeInactive, ct);
    }

    public Task<IReadOnlyList<ProductUnitSummary>> ListUnitsAsync(CatalogUserIdentity user, bool includeInactive, CancellationToken ct)
    {
        Require(user, CatalogPermissionCodes.Read);
        return store.ListUnitsAsync(user, includeInactive, ct);
    }

    public Task<ProductMerchandisingConfiguration?> GetAsync(CatalogUserIdentity user, Guid productId, CancellationToken ct)
    {
        Require(user, CatalogPermissionCodes.Read);
        return store.GetAsync(user, productId, ct);
    }

    public Task<ProductBrandSummary> SaveBrandAsync(CatalogUserIdentity user, Guid? id, SaveProductBrandRequest request, CancellationToken ct)
    {
        Require(user, CatalogPermissionCodes.Update);
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 120)
            throw new CatalogValidationException("The product brand name is invalid.");
        return store.SaveBrandAsync(user, id, request with { Name = request.Name.Trim() }, timeProvider.GetUtcNow(), ct);
    }

    public Task<ProductUnitSummary> SaveUnitAsync(CatalogUserIdentity user, Guid? id, SaveProductUnitRequest request, CancellationToken ct)
    {
        Require(user, CatalogPermissionCodes.Update);
        var code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;
        var name = request.Name?.Trim() ?? string.Empty;
        var symbol = request.Symbol?.Trim() ?? string.Empty;
        if (code.Length is < 1 or > 24 || name.Length is < 1 or > 80 || symbol.Length is < 1 or > 16 ||
            request.DecimalPlaces is < 0 or > 6 || (!request.AllowsFractionalQuantity && request.DecimalPlaces != 0))
            throw new CatalogValidationException("The sale unit configuration is invalid.");
        return store.SaveUnitAsync(user, id, request with { Code = code, Name = name, Symbol = symbol }, timeProvider.GetUtcNow(), ct);
    }

    public async Task<ProductMerchandisingConfiguration> SaveAsync(CatalogUserIdentity user, Guid productId, SaveProductMerchandisingRequest request, CancellationToken ct)
    {
        Require(user, CatalogPermissionCodes.Update);
        Validate(productId, request);
        var result = await store.SaveAsync(user, productId,
            request with { BaseUnitCode = request.BaseUnitCode.Trim().ToUpperInvariant() },
            timeProvider.GetUtcNow(), ct);
        await synchronization.DispatchPendingAsync(user.TenantId, user.BusinessId, CancellationToken.None);
        return result;
    }

    private static void Validate(Guid productId, SaveProductMerchandisingRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.BaseUnitCode)) throw new CatalogValidationException("A sale unit is required.");
        if (request.IsWeighable && !request.AllowsFractionalSale) throw new CatalogValidationException("A scale product must allow decimal quantities.");
        if (request.Link is not null && request.LinkedProducts.Count > 0) throw new CatalogValidationException("A linked child cannot also be a root product.");
        if (request.Link is { SharesInventory: true } && request.ManageInventory)
            throw new CatalogValidationException("A product that shares its parent's inventory cannot control a separate inventory.");
        if (request.IsWeighable != (request.Scale is not null)) throw new CatalogValidationException("Scale capture requires exactly one scale configuration.");
        if (request.Barcodes.Any(x => string.IsNullOrWhiteSpace(x.Value)) ||
            request.Barcodes.Select(x => x.Value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != request.Barcodes.Count ||
            request.Barcodes.Count(x => x.IsPrimary) > 1)
            throw new CatalogValidationException("Product barcodes are invalid or duplicated.");
        if (request.Link is { } link && (link.ParentProductId == productId ||
            (link.SharesInventory && link.InventoryFactor is null or <= 0) ||
            (link.SharesPrice && link.PriceFactor is null or <= 0)))
            throw new CatalogValidationException("The linked product configuration is invalid.");
        if (request.LinkedProducts.Select(x => x.ChildProductId).Distinct().Count() != request.LinkedProducts.Count ||
            request.LinkedProducts.Any(link => link.ChildProductId == productId ||
                (link.SharesInventory && link.InventoryFactor is null or <= 0) ||
                (link.SharesPrice && link.PriceFactor is null or <= 0)))
            throw new CatalogValidationException("The linked product list is invalid.");
    }

    private static void Require(CatalogUserIdentity user, string permission)
    {
        if (!user.Permissions.Contains(permission)) throw new CatalogForbiddenException($"Permission '{permission}' is required.");
    }
}

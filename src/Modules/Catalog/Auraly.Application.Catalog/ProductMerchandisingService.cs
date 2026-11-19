using Auraly.BuildingBlocks.Application.Synchronization;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Catalog;

namespace Auraly.Application.Catalog;

public interface IProductMerchandisingStore
{
    Task<IReadOnlyList<PosCatalogCategory>> PosCategoriesAsync(CatalogUserIdentity user, CancellationToken ct);
    Task<IReadOnlyList<ProductVariantOption>> ListVariantOptionsAsync(CatalogUserIdentity user, bool colors, bool includeInactive, CancellationToken ct);
    Task<ProductVariantOption> SaveVariantOptionAsync(CatalogUserIdentity user, bool colors, Guid? id, SaveProductVariantOption request, DateTimeOffset now, CancellationToken ct);
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
    public Task<IReadOnlyList<PosCatalogCategory>> PosCategoriesAsync(CatalogUserIdentity user, CancellationToken ct)
    {
        Require(user, "sales.create");
        return store.PosCategoriesAsync(user, ct);
    }

    public Task<IReadOnlyList<ProductVariantOption>> ListVariantOptionsAsync(CatalogUserIdentity user, bool colors, bool includeInactive, CancellationToken ct)
    {
        Require(user, CatalogPermissionCodes.Read);
        return store.ListVariantOptionsAsync(user, colors, includeInactive, ct);
    }

    public async Task<ProductVariantOption> SaveVariantOptionAsync(CatalogUserIdentity user, bool colors, Guid? id, SaveProductVariantOption request, CancellationToken ct)
    {
        Require(user, CatalogPermissionCodes.Update);
        var code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;
        var name = request.Name?.Trim() ?? string.Empty;
        if (code.Length is < 1 or > 32 || name.Length is < 1 or > 120 || request.SortOrder < 0)
            throw new CatalogValidationException("Indica un código, nombre y orden válidos para el color o la talla.");
        var value = await store.SaveVariantOptionAsync(user, colors, id, request with { Code = code, Name = name }, timeProvider.GetUtcNow(), ct);
        await synchronization.DispatchTenantPendingAsync(user.TenantId, CancellationToken.None);
        return value;
    }

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
            throw new CatalogValidationException("El nombre de la marca del producto no es válido.");
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
            throw new CatalogValidationException("La configuración de la unidad de venta no es válida.");
        return store.SaveUnitAsync(user, id, request with { Code = code, Name = name, Symbol = symbol }, timeProvider.GetUtcNow(), ct);
    }

    public async Task<ProductMerchandisingConfiguration> SaveAsync(CatalogUserIdentity user, Guid productId, SaveProductMerchandisingRequest request, CancellationToken ct)
    {
        Require(user, CatalogPermissionCodes.Update);
        var normalized = request with {
                BaseUnitCode = request.BaseUnitCode.Trim().ToUpperInvariant(),
                ManageInventory = request.IsGenericProduct ? false : request.ManageInventory,
                IsWeighable = request.IsGenericProduct ? false : request.IsWeighable,
                Scale = request.IsGenericProduct ? null : request.Scale,
                Link = request.IsGenericProduct ? null : request.Link,
                LinkedProducts = request.IsGenericProduct ? [] : request.LinkedProducts,
                ConversionMaximumLossPercent = request.IsGenericProduct
                    ? null
                    : request.ConversionMaximumLossPercent
            };
        Validate(productId, normalized);
        var result = await store.SaveAsync(user, productId, normalized,
            timeProvider.GetUtcNow(), ct);
        await synchronization.DispatchTenantPendingAsync(user.TenantId, CancellationToken.None);
        return result;
    }

    private static void Validate(Guid productId, SaveProductMerchandisingRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.BaseUnitCode)) throw new CatalogValidationException("Selecciona una unidad de venta.");
        if (request.IsWeighable && !request.AllowsFractionalSale) throw new CatalogValidationException("Un producto de balanza debe permitir cantidades decimales.");
        if (request.Link is not null && request.LinkedProducts.Count > 0) throw new CatalogValidationException("Un producto vinculado como hijo no puede ser también un producto principal.");
        if (request.Link is { SharesInventory: true } && request.ManageInventory)
            throw new CatalogValidationException("Un producto que comparte el inventario del principal no puede controlar un inventario independiente.");
        if (request.ConversionMaximumLossPercent is < 0 or > 100)
            throw new CatalogValidationException("La merma máxima de conversión debe estar entre 0 % y 100 %.");
        if (request.LinkedProducts.Any(link => link.AllowsConversion) &&
            (!request.ManageInventory || request.ConversionMaximumLossPercent is null))
            throw new CatalogValidationException("Una familia convertible debe controlar inventario y definir su merma máxima.");
        if (request.Scale is not null && !request.IsWeighable) throw new CatalogValidationException("La configuración de balanza requiere un producto vendido por peso.");
        if (request.UnitGrossWeightKg is <= 0) throw new CatalogValidationException("El peso del producto debe ser mayor que cero si se indica.");
        if (request.Barcodes.Any(x => string.IsNullOrWhiteSpace(x.Value)) ||
            request.Barcodes.Select(x => x.Value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != request.Barcodes.Count ||
            request.Barcodes.Count(x => x.IsPrimary) > 1)
            throw new CatalogValidationException("Los códigos de barras del producto no son válidos o están repetidos.");
        if (request.Link is { } link && (link.ParentProductId == productId ||
            (link.SharesInventory && link.InventoryFactor is null or <= 0) ||
            (link.SharesPrice && link.PriceFactor is null or <= 0) ||
            (link.AllowsConversion && (link.SharesInventory || !request.ManageInventory || link.ConversionFactor is null or <= 0)) ||
            (!link.AllowsConversion && link.ConversionFactor is not null)))
            throw new CatalogValidationException("La configuración de productos vinculados no es válida.");
        if (request.LinkedProducts.Select(x => x.ChildProductId).Distinct().Count() != request.LinkedProducts.Count ||
            request.LinkedProducts.Any(link => link.ChildProductId == productId ||
                (link.SharesInventory && link.InventoryFactor is null or <= 0) ||
                (link.SharesPrice && link.PriceFactor is null or <= 0) ||
                (link.AllowsConversion && (link.SharesInventory || link.ConversionFactor is null or <= 0)) ||
                (!link.AllowsConversion && link.ConversionFactor is not null)))
            throw new CatalogValidationException("La lista de productos vinculados no es válida.");
    }

    private static void Require(CatalogUserIdentity user, string permission)
    {
        if (!user.Permissions.Contains(permission)) throw new CatalogForbiddenException($"No tienes permiso para realizar esta acción.");
    }
}

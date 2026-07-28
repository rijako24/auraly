using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Catalog;

namespace Auraly.Application.Catalog;

public interface ICatalogStore
{
    Task<ProductDetail> CreateAsync(CatalogUserIdentity user, Guid productId, SaveProductRequest request, DateTimeOffset now, CancellationToken ct);
    Task<ProductDetail> UpdateAsync(CatalogUserIdentity user, Guid productId, SaveProductRequest request, DateTimeOffset now, CancellationToken ct);
    Task<ProductDetail?> GetAsync(Guid tenantId, Guid businessId, Guid productId, bool includeCosts, CancellationToken ct);
    Task<ProductPage> PageAsync(Guid tenantId, Guid businessId, ProductPageRequest request, bool includeCosts, CancellationToken ct);
    Task DeactivateAsync(CatalogUserIdentity user, Guid productId, DateTimeOffset now, CancellationToken ct);
    Task<CatalogSyncSessionResponse> StartSyncAsync(Guid deviceId, Guid tenantId, Guid businessId, Guid registerId, DateTimeOffset now, CancellationToken ct);
    Task<CatalogBootstrapPage> BootstrapPageAsync(Guid deviceId, Guid sessionId, string? cursor, int pageSize, CancellationToken ct);
    Task<CatalogDeltaPage> ChangesAsync(Guid deviceId, Guid tenantId, Guid businessId, long cursor, int pageSize, CancellationToken ct);
    Task<InventoryAvailabilityResponse> AvailabilityAsync(
        Guid deviceId, Guid tenantId, Guid businessId, Guid registerId,
        InventoryAvailabilityRequest request, CancellationToken ct);
}

public sealed class CatalogService(
    ICatalogStore store,
    IAuralyIdGenerator ids,
    TimeProvider timeProvider)
{
    public Task<ProductDetail> CreateAsync(CatalogUserIdentity user, SaveProductRequest request, CancellationToken ct)
    {
        Require(user, CatalogPermissionCodes.Create);
        RequireCapabilities(user, request);
        ValidateScope(user, request);
        Validate(request);
        return store.CreateAsync(user, ids.NewId(), request, timeProvider.GetUtcNow(), ct);
    }

    public Task<ProductDetail> UpdateAsync(CatalogUserIdentity user, Guid productId, SaveProductRequest request, CancellationToken ct)
    {
        Require(user, CatalogPermissionCodes.Update);
        RequireCapabilities(user, request);
        ValidateScope(user, request);
        Validate(request);
        return store.UpdateAsync(user, productId, request, timeProvider.GetUtcNow(), ct);
    }

    public Task DeactivateAsync(CatalogUserIdentity user, Guid productId, CancellationToken ct)
    {
        Require(user, CatalogPermissionCodes.Deactivate);
        return store.DeactivateAsync(user, productId, timeProvider.GetUtcNow(), ct);
    }

    public Task<ProductDetail?> GetAsync(CatalogUserIdentity user, Guid productId, CancellationToken ct)
    {
        Require(user, CatalogPermissionCodes.Read);
        return store.GetAsync(user.TenantId, user.BusinessId, productId, user.Permissions.Contains(CatalogPermissionCodes.ReadCosts), ct);
    }

    public Task<ProductPage> PageAsync(CatalogUserIdentity user, ProductPageRequest request, CancellationToken ct)
    {
        Require(user, CatalogPermissionCodes.Read);
        if (request.PageSize is < 1 or > 200) throw new CatalogValidationException("PageSize must be between 1 and 200.");
        if (request.MinimumPrice < 0 || request.MaximumPrice < 0 || request.MinimumPrice > request.MaximumPrice)
            throw new CatalogValidationException("The price range is invalid.");
        return store.PageAsync(user.TenantId, user.BusinessId, request, user.Permissions.Contains(CatalogPermissionCodes.ReadCosts), ct);
    }

    private static void ValidateScope(CatalogUserIdentity user, SaveProductRequest request)
    {
        if (request.BusinessId != user.BusinessId)
            throw new CatalogForbiddenException("The product scope does not match the authenticated user.");
    }

    private static void RequireCapabilities(CatalogUserIdentity user, SaveProductRequest request)
    {
        if (request.Prices.Count > 0) Require(user, CatalogPermissionCodes.ManagePrices);
        if (request.Suppliers.Count > 0) Require(user, CatalogPermissionCodes.ManageCosts);
    }

    private static void Require(CatalogUserIdentity user, string permission)
    {
        if (!user.Permissions.Contains(permission)) throw new CatalogForbiddenException($"Permission '{permission}' is required.");
    }

    private static void Validate(SaveProductRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProductCode) || string.IsNullOrWhiteSpace(request.Name) ||
            string.IsNullOrWhiteSpace(request.BaseUnitCode) || request.TaxProfileId == Guid.Empty)
            throw new CatalogValidationException("Product code, name, base unit and tax profile are required.");
        if (request.IsWeighable != (request.Scale is not null))
            throw new CatalogValidationException("A weighable product requires exactly one scale configuration.");
        if (request.Prices.Count != 1 || request.Prices.Any(price => price.Amount < 0))
            throw new CatalogValidationException(
                "Every sellable product requires exactly one non-negative base price for its business.");
        if (request.Suppliers.Any(supplier => supplier.BaseUnitCost < 0))
            throw new CatalogValidationException("Supplier costs cannot be negative.");
        if (request.Barcodes.Any(barcode => string.IsNullOrWhiteSpace(barcode.Value)))
            throw new CatalogValidationException("Barcodes cannot be empty.");
        if (request.Scale is { ValueStart: < 0 } or { ValueLength: <= 0 } or { DecimalPlaces: < 0 or > 6 })
            throw new CatalogValidationException("The scale barcode positions are invalid.");
    }
}

public sealed class CatalogForbiddenException(string message) : Exception(message);
public sealed class CatalogValidationException(string message) : Exception(message);
public sealed class CatalogConflictException(string message) : Exception(message);

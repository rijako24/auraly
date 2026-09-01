using Auraly.BuildingBlocks.Application.Synchronization;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Catalog;
using Auraly.Domain.Catalog;

namespace Auraly.Application.Catalog;

public interface ICatalogStore
{
    Task<ProductDetail> CreateAsync(CatalogUserIdentity user, Guid productId, SaveProductRequest request, DateTimeOffset now, CancellationToken ct);
    Task<ProductDetail> UpdateAsync(CatalogUserIdentity user, Guid productId, SaveProductRequest request, DateTimeOffset now, CancellationToken ct);
    Task<ProductDetail?> GetAsync(Guid tenantId, Guid businessId, Guid productId, bool includeCosts, CancellationToken ct);
    Task<ProductPage> PageAsync(Guid tenantId, Guid businessId, ProductPageRequest request, bool includeCosts, CancellationToken ct);
    Task<IReadOnlyList<ProductRotationDetail>> ProductRotationAsync(Guid tenantId, Guid businessId, Guid productId, CancellationToken ct);
    Task SetStatusAsync(CatalogUserIdentity user, Guid productId, bool isActive, DateTimeOffset now, CancellationToken ct);
    Task<CatalogSyncSessionResponse> StartSyncAsync(Guid deviceId, Guid tenantId, Guid businessId, Guid warehouseId, DateTimeOffset now, CancellationToken ct);
    Task<CatalogBootstrapPage> BootstrapPageAsync(Guid deviceId, Guid sessionId, string? cursor, int pageSize, CancellationToken ct);
    Task<CatalogDeltaPage> ChangesAsync(Guid deviceId, Guid tenantId, Guid businessId, long cursor, int pageSize, CancellationToken ct);
    Task<InventoryAvailabilityResponse> AvailabilityAsync(
        Guid deviceId, Guid tenantId, Guid businessId,
        InventoryAvailabilityRequest request, CancellationToken ct);
    Task<IReadOnlyList<ProductWarehouseAvailabilityItem>> WarehouseAvailabilityAsync(
        Guid? deviceId, Guid tenantId, Guid businessId, Guid productId,
        bool includeOtherBusinesses, CancellationToken ct);
    Task<PosPricingSnapshot> PricingSnapshotAsync(Guid deviceId, Guid tenantId, Guid businessId, Guid warehouseId, CancellationToken ct);
    Task<IReadOnlyList<TaxProfileSummary>> ListTaxProfilesAsync(CatalogUserIdentity user, bool includeInactive, CancellationToken ct);
    Task<TaxProfileSummary> SaveTaxProfileAsync(CatalogUserIdentity user, Guid? taxProfileId, SaveTaxProfileRequest request, DateTimeOffset now, CancellationToken ct);
    Task<ProductTaxConfiguration?> GetProductTaxConfigurationAsync(CatalogUserIdentity user, Guid productId, CancellationToken ct);
    Task<ProductTaxConfiguration> SaveProductTaxConfigurationAsync(CatalogUserIdentity user, Guid productId, SaveProductTaxConfigurationRequest request, DateTimeOffset now, CancellationToken ct);
}

public sealed class CatalogService(
    ICatalogStore store,
    IAuralyIdGenerator ids,
    TimeProvider timeProvider,
    IPosSynchronizationOutboxDispatcher synchronization)
{
    private const string InventoryReadPermission = "inventory.read";
    private const string PosInventoryAvailabilityReadPermission = "pos.inventory.availability.read";
    private const string BusinessesReadPermission = "businesses.read";
    public async Task<ProductDetail> CreateAsync(
        CatalogUserIdentity user,
        SaveProductRequest request,
        CancellationToken ct)
    {
        Require(user, CatalogPermissionCodes.Create);
        RequireCapabilities(user, request);
        ValidateScope(user, request);
        request = NormalizeRelatedData(request);
        Validate(request);
        var product = await store.CreateAsync(
            user, ids.NewId(), request, timeProvider.GetUtcNow(), ct);
        await synchronization.DispatchPendingAsync(
            user.TenantId, user.BusinessId, CancellationToken.None);
        return product;
    }

    public async Task<ProductDetail> UpdateAsync(
        CatalogUserIdentity user,
        Guid productId,
        SaveProductRequest request,
        CancellationToken ct)
    {
        Require(user, CatalogPermissionCodes.Update);
        RequireCapabilities(user, request);
        ValidateScope(user, request);
        request = NormalizeRelatedData(request);
        Validate(request);
        var product = await store.UpdateAsync(
            user, productId, request, timeProvider.GetUtcNow(), ct);
        await synchronization.DispatchPendingAsync(
            user.TenantId, user.BusinessId, CancellationToken.None);
        return product;
    }

    public Task DeactivateAsync(
        CatalogUserIdentity user, Guid productId, CancellationToken ct) =>
        SetStatusAsync(user, productId, false, ct);

    public async Task SetStatusAsync(
        CatalogUserIdentity user, Guid productId, bool isActive, CancellationToken ct)
    {
        Require(user, isActive ? CatalogPermissionCodes.Update : CatalogPermissionCodes.Deactivate);
        await store.SetStatusAsync(user, productId, isActive, timeProvider.GetUtcNow(), ct);
        await synchronization.DispatchPendingAsync(
            user.TenantId, user.BusinessId, CancellationToken.None);
    }

    public Task<ProductDetail?> GetAsync(CatalogUserIdentity user, Guid productId, CancellationToken ct)
    {
        Require(user, CatalogPermissionCodes.Read);
        return store.GetAsync(user.TenantId, user.BusinessId, productId, user.Permissions.Contains(CatalogPermissionCodes.ReadCosts), ct);
    }

    public Task<IReadOnlyList<ProductRotationDetail>> ProductRotationAsync(
        CatalogUserIdentity user, Guid productId, CancellationToken ct)
    {
        Require(user, CatalogPermissionCodes.Read);
        if (productId == Guid.Empty) throw new CatalogValidationException("ProductId is required.");
        return store.ProductRotationAsync(user.TenantId, user.BusinessId, productId, ct);
    }

    public Task<IReadOnlyList<ProductWarehouseAvailabilityItem>> WarehouseAvailabilityAsync(
        CatalogUserIdentity user, Guid productId, CancellationToken ct)
        => WarehouseAvailabilityAsync(user, productId, InventoryReadPermission, ct);

    public Task<IReadOnlyList<ProductWarehouseAvailabilityItem>> PosWarehouseAvailabilityAsync(
        CatalogUserIdentity user, Guid productId, CancellationToken ct)
        => WarehouseAvailabilityAsync(user, productId, PosInventoryAvailabilityReadPermission, ct);

    private Task<IReadOnlyList<ProductWarehouseAvailabilityItem>> WarehouseAvailabilityAsync(
        CatalogUserIdentity user, Guid productId, string requiredPermission, CancellationToken ct)
    {
        Require(user, requiredPermission);
        if (productId == Guid.Empty)
            throw new CatalogValidationException("ProductId is required.");
        return store.WarehouseAvailabilityAsync(
            null, user.TenantId, user.BusinessId, productId,
            user.Permissions.Contains(BusinessesReadPermission), ct);
    }

    public Task<ProductPage> PageAsync(CatalogUserIdentity user, ProductPageRequest request, CancellationToken ct)
    {
        Require(user, CatalogPermissionCodes.Read);
        if (request.PageSize is < 1 or > 200) throw new CatalogValidationException("PageSize must be between 1 and 200.");
        if (request.MinimumPrice < 0 || request.MaximumPrice < 0 || request.MinimumPrice > request.MaximumPrice)
            throw new CatalogValidationException("The price range is invalid.");
        return store.PageAsync(user.TenantId, user.BusinessId, request, user.Permissions.Contains(CatalogPermissionCodes.ReadCosts), ct);
    }


    public Task<IReadOnlyList<TaxProfileSummary>> ListTaxProfilesAsync(
        CatalogUserIdentity user, bool includeInactive, CancellationToken ct)
    {
        Require(user, CatalogPermissionCodes.Read);
        return store.ListTaxProfilesAsync(user, includeInactive, ct);
    }

    public Task<TaxProfileSummary> SaveTaxProfileAsync(
        CatalogUserIdentity user, Guid? taxProfileId, SaveTaxProfileRequest request, CancellationToken ct)
    {
        Require(user, CatalogPermissionCodes.Update);
        if (request.BusinessId != user.BusinessId)
            throw new CatalogForbiddenException("The tax profile scope does not match the authenticated user.");
        var code = request.Code?.Trim() ?? string.Empty;
        var name = request.Name?.Trim() ?? string.Empty;
        if (code.Length is < 1 or > 32 || name.Length is < 1 or > 120 || request.Rate is < 0 or > 100)
            throw new CatalogValidationException("Code, name and VAT rate are invalid.");
        return store.SaveTaxProfileAsync(user, taxProfileId, request with { Code = code, Name = name },
            timeProvider.GetUtcNow(), ct);
    }

    public Task<ProductTaxConfiguration?> GetProductTaxConfigurationAsync(
        CatalogUserIdentity user, Guid productId, CancellationToken ct)
    {
        Require(user, CatalogPermissionCodes.Read);
        return store.GetProductTaxConfigurationAsync(user, productId, ct);
    }

    public Task<ProductTaxConfiguration> SaveProductTaxConfigurationAsync(
        CatalogUserIdentity user, Guid productId, SaveProductTaxConfigurationRequest request,
        CancellationToken ct)
    {
        Require(user, CatalogPermissionCodes.Update);
        if (request.SalesTaxProfileId == Guid.Empty || request.PurchaseTaxProfileId == Guid.Empty
            || !PurchasingTaxTreatmentIsSupported(request.PurchaseTaxTreatment))
            throw new CatalogValidationException("The product VAT configuration is invalid.");
        return store.SaveProductTaxConfigurationAsync(
            user, productId, request, timeProvider.GetUtcNow(), ct);
    }
    private static void ValidateScope(CatalogUserIdentity user, SaveProductRequest request)
    {
        if (request.BusinessId != user.BusinessId)
            throw new CatalogForbiddenException("The product scope does not match the authenticated user.");
    }

    private static void RequireCapabilities(CatalogUserIdentity user, SaveProductRequest request)
    {
        if (request.Prices.Count > 0) Require(user, CatalogPermissionCodes.ManagePrices);
        if (request.Suppliers.Count > 0 || request.Prices.Any(price => price.CostBasisAmount is not null))
            Require(user, CatalogPermissionCodes.ManageCosts);
    }


    private static bool PurchasingTaxTreatmentIsSupported(string value) =>
        value is "DeductibleInputVat" or "CapitalizedCost" or "NotApplicable";
    private static SaveProductRequest NormalizeRelatedData(SaveProductRequest request) => request with
    {
        Aliases = request.Aliases is null ? null : request.Aliases
            .Select(value => new ProductAliasInput(value.Alias.Trim(), ProductAliasNormalizer.Normalize(value.Alias)))
            .Where(value => value.NormalizedAlias!.Length > 0)
            .DistinctBy(value => value.NormalizedAlias, StringComparer.Ordinal)
            .ToArray()
    };
    private static void Require(CatalogUserIdentity user, string permission)
    {
        if (!user.Permissions.Contains(permission)) throw new CatalogForbiddenException($"Permission '{permission}' is required.");
    }

    private static void Validate(SaveProductRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) ||
            string.IsNullOrWhiteSpace(request.BaseUnitCode) || request.TaxProfileId == Guid.Empty ||
            request.PurchaseTaxProfileId == Guid.Empty)
            throw new CatalogValidationException("Name, base unit, sales VAT and purchase VAT are required.");
        if (!PurchasingTaxTreatmentIsSupported(request.PurchaseTaxTreatment))
            throw new CatalogValidationException("The purchase VAT treatment is invalid.");
        if (request.IsWeighable != (request.Scale is not null))
            throw new CatalogValidationException("A weighable product requires exactly one scale configuration.");
        if (request.Prices.Count != 1 || request.Prices.Any(price => price.Amount <= 0))
            throw new CatalogValidationException(
                "Every sellable product requires exactly one positive base price for its business.");
        if (request.Prices.Any(price =>
                price.CostBasisAmount is null or <= 0 ||
                price.TargetMarginPercent is null or < 0 or >= 100))
            throw new CatalogValidationException(
                "Every product requires a positive cost and a margin between zero and less than 100 percent.");
        if (request.Prices.Any(price =>
                (price.PreparedAmount ?? price.Amount) <= 0 ||
                price.RoundingIncrement <= 0 ||
                price.InputMode is not ("Margin" or "SalePrice") ||
                price.RoundingMode is not ("Nearest" or "Up" or "Down")))
            throw new CatalogValidationException("The prepared product price configuration is invalid.");
        if ((request.Images ?? []).Count(image => image.IsPrimary) > 1
            || (request.Images ?? []).Any(image => image.ProductImageId == Guid.Empty
                || string.IsNullOrWhiteSpace(image.MediaReference)
                || image.MediaReference.Length > 1500
                || image.AltText?.Length > 300
                || image.DisplayOrder < 0))
            throw new CatalogValidationException("The product image configuration is invalid.");
        if (request.Suppliers.Count == 0 || request.Suppliers.Count(supplier => supplier.IsPrimary) != 1)
            throw new CatalogValidationException("Every product requires exactly one primary supplier.");
        if (request.Suppliers.Any(supplier => supplier.SupplierId == Guid.Empty || supplier.BaseUnitCost <= 0))
            throw new CatalogValidationException("Every product supplier requires an identifier and a positive cost.");
        if (request.Suppliers.Any(supplier => string.IsNullOrWhiteSpace(supplier.PurchasePresentationName)
            || supplier.PurchasePresentationName.Trim().Length > 80 || supplier.UnitsPerPresentation <= 0))
            throw new CatalogValidationException("Every supplier presentation requires a name and a positive conversion factor.");
        if (request.Suppliers.GroupBy(supplier => supplier.Identification.Trim(), StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            throw new CatalogValidationException("A supplier cannot be repeated for the same product.");
        if (request.Barcodes.Any(barcode => string.IsNullOrWhiteSpace(barcode.Value)))
            throw new CatalogValidationException("Barcodes cannot be empty.");
        if (request.Scale is { ValueStart: < 0 } or { ValueLength: <= 0 } or { DecimalPlaces: < 0 or > 6 })
            throw new CatalogValidationException("The scale barcode positions are invalid.");
        if (request.IsWeighable && !request.AllowsFractionalSale)
            throw new CatalogValidationException("A weighable product must allow fractional quantities.");
        if (request.Link is { SharesInventory: true } && request.ManageInventory)
            throw new CatalogValidationException("A product that shares its parent's inventory cannot control a separate inventory.");
        if (request.Link is { SharesInventory: true, InventoryFactor: null or <= 0 })
            throw new CatalogValidationException("The linked inventory factor must be positive.");
        if (request.Link is { SharesPrice: true, PriceFactor: null or <= 0 })
            throw new CatalogValidationException("The linked price factor must be positive.");
        if (request.Link is { AllowsConversion: true } link &&
            (link.SharesInventory || !request.ManageInventory || link.ConversionFactor is null or <= 0))
            throw new CatalogValidationException("A convertible linked product must manage separate inventory and define a positive conversion factor.");
        if (request.Link is { AllowsConversion: false, ConversionFactor: not null })
            throw new CatalogValidationException("A conversion factor requires conversion to be enabled.");

    }
}

public sealed class CatalogForbiddenException(string message) : Exception(message);
public sealed class CatalogValidationException(string message) : Exception(message);
public sealed class CatalogConflictException(string message) : Exception(message);

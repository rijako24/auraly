namespace Auraly.Contracts.Catalog;

public static class CatalogPermissionCodes
{
    public const string Read = "catalog.read";
    public const string Create = "catalog.create";
    public const string Update = "catalog.update";
    public const string Deactivate = "catalog.deactivate";
    public const string ManagePrices = "catalog.prices.manage";
    public const string ReadCosts = "catalog.costs.read";
    public const string ManageCosts = "catalog.costs.manage";
    public const string Sync = "catalog.sync";
}

public sealed record ProductBarcodeInput(string Value, bool IsPrimary = false);
public sealed record ProductIdentifierInput(string Type, string Value);
public sealed record ProductAliasInput(string Alias, string? NormalizedAlias = null);
public sealed record ProductImageInput(
    Guid ProductImageId,
    Guid? ProductOfferId,
    string MediaReference,
    string? AltText,
    int DisplayOrder,
    bool IsPrimary);
public sealed record ProductPriceInput(
    decimal Amount,
    string CurrencyCode = "COP",
    decimal? CostBasisAmount = null,
    decimal? TargetMarginPercent = null,
    decimal? PreparedAmount = null,
    string InputMode = "Margin",
    decimal RoundingIncrement = 1,
    string RoundingMode = "Nearest");
public sealed record SupplierCostInput(
    Guid SupplierId,
    string Identification,
    string Name,
    string? SupplierProductCode,
    decimal BaseUnitCost,
    bool IsPrimary = true,
    string PurchasePresentationName = "Unidad",
    decimal UnitsPerPresentation = 1);
public sealed record ScaleConfigurationInput(
    string ScaleCode,
    string BarcodePrefix,
    string EmbeddedValueType,
    int ValueStart,
    int ValueLength,
    int DecimalPlaces);

public sealed record SaveProductRequest(
    Guid BusinessId,
    string ProductCode,
    string? Reference,
    string Name,
    string? Description,
    string BaseUnitCode,
    Guid TaxProfileId,
    bool ManageInventory,
    bool IsWeighable,
    IReadOnlyCollection<ProductBarcodeInput> Barcodes,
    IReadOnlyCollection<ProductIdentifierInput> Identifiers,
    IReadOnlyCollection<ProductPriceInput> Prices,
    IReadOnlyCollection<SupplierCostInput> Suppliers,
    ScaleConfigurationInput? Scale,
    Guid PurchaseTaxProfileId = default,
    string PurchaseTaxTreatment = "DeductibleInputVat",
    Guid? ProductCategoryId = null,
    Guid? ProductBrandId = null,
    bool AllowsFractionalSale = false,
    ProductLinkInput? Link = null,
    IReadOnlyCollection<LinkedProductInput>? LinkedProducts = null,
    decimal? ConversionMaximumLossPercent = null,
    IReadOnlyCollection<ProductAliasInput>? Aliases = null,
    IReadOnlyCollection<ProductImageInput>? Images = null);

public sealed record ProductDetail(
    Guid ProductId,
    Guid BusinessId,
    string ProductCode,
    string? Reference,
    string Name,
    bool IsActive,
    IReadOnlyCollection<string> Barcodes,
    IReadOnlyCollection<ProductPriceInput> Prices,
    IReadOnlyCollection<SupplierCostInput>? Suppliers,
    Guid SalesTaxProfileId = default,
    Guid PurchaseTaxProfileId = default,
    string PurchaseTaxTreatment = "DeductibleInputVat",
    string? Description = null,
    string BaseUnitCode = "EA",
    bool ManageInventory = true,
    bool IsWeighable = false);

public sealed record ProductPageRequest(
    int PageSize = 50,
    string? AfterProductCode = null,
    string? ProductCode = null,
    string? Reference = null,
    string? Barcode = null,
    string? Name = null,
    bool? IsActive = null,
    Guid? SupplierId = null,
    decimal? MinimumPrice = null,
    decimal? MaximumPrice = null,
    bool SortDescending = false);

public sealed record ProductPage(IReadOnlyCollection<ProductDetail> Items, string? NextCursor);

public sealed record CatalogUserIdentity(
    Guid UserId,
    Guid TenantId,
    Guid BusinessId,
    IReadOnlySet<string> Permissions);

public sealed record CatalogDeviceIdentity(
    Guid DeviceId,
    Guid TenantId,
    Guid BusinessId,
    Guid WarehouseId,
    IReadOnlySet<string> Permissions);

[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record PosCatalogItem(
    Guid ProductId,
    string ProductCode,
    string? Reference,
    string Name,
    string BaseUnitCode,
    string TaxCode,
    decimal TaxRate,
    decimal UnitPrice,
    string CurrencyCode,
    bool IsActive,
    bool IsWeighable,
    bool AllowsFractionalSale,
    ScaleConfigurationInput? Scale,
    IReadOnlyCollection<string> Barcodes,
    IReadOnlyCollection<ProductIdentifierInput> Identifiers,
    decimal UnitCost = 0,
    bool ManagesStock = true)
{
    public PosCatalogItem(
        Guid productId,
        string productCode,
        string? reference,
        string name,
        string baseUnitCode,
        string taxCode,
        decimal taxRate,
        decimal unitPrice,
        string currencyCode,
        bool isActive,
        ScaleConfigurationInput? scale,
        IReadOnlyCollection<string> barcodes,
        IReadOnlyCollection<ProductIdentifierInput> identifiers)
        : this(
            productId,
            productCode,
            reference,
            name,
            baseUnitCode,
            taxCode,
            taxRate,
            unitPrice,
            currencyCode,
            isActive,
            false,
            false,
            scale,
            barcodes,
            identifiers)
    {
    }
}

public sealed record CatalogSyncSessionResponse(
    Guid SessionId,
    long HighWaterMark,
    int TotalProducts,
    DateTimeOffset ExpiresAt);

public sealed record CatalogBootstrapPage(
    Guid SessionId,
    long HighWaterMark,
    string? NextCursor,
    bool HasMore,
    string IntegrityHash,
    IReadOnlyCollection<PosCatalogItem> Items);

public sealed record CatalogDelta(long Version, string Kind, PosCatalogItem Product);
public sealed record CatalogDeltaPage(long FromCursor, long ToCursor, bool HasMore, IReadOnlyCollection<CatalogDelta> Changes);

public sealed record InventoryAvailabilityRequest(Guid ProductId, Guid WarehouseId, decimal Quantity, Guid OperationId);
public sealed record InventoryAvailabilityResponse(
    Guid ProductId,
    Guid WarehouseId,
    decimal RequestedQuantity,
    decimal AvailableQuantity,
    bool ValidationRequired,
    bool IsAvailable,
    string Status);

public sealed record ProductWarehouseAvailabilityItem(
    Guid BusinessId,
    string BusinessName,
    Guid WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    Guid ProductId,
    string ProductCode,
    decimal QuantityOnHand,
    bool IsCurrentBusiness);

public sealed record TaxProfileSummary(
    Guid TaxProfileId, Guid BusinessId, string Code, string DianTaxCode, string Name,
    decimal Rate, bool IsActive);

public sealed record SaveTaxProfileRequest(
    Guid BusinessId, string Code, string Name, decimal Rate, bool IsActive = true,
    string DianTaxCode = "01");
public sealed record ProductTaxConfiguration(
    Guid ProductId, Guid SalesTaxProfileId, Guid PurchaseTaxProfileId,
    string PurchaseTaxTreatment);

public sealed record SaveProductTaxConfigurationRequest(
    Guid SalesTaxProfileId, Guid PurchaseTaxProfileId, string PurchaseTaxTreatment);

public sealed record SetProductStatusRequest(bool IsActive);

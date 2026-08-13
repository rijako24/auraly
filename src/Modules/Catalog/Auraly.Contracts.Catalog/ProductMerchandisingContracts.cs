namespace Auraly.Contracts.Catalog;

public sealed record ProductBrandSummary(Guid ProductBrandId, string Name, bool IsActive);
public sealed record SaveProductBrandRequest(string Name, bool IsActive = true);

public sealed record ProductUnitSummary(
    Guid ProductUnitId,
    string Code,
    string Name,
    string Symbol,
    bool AllowsFractionalQuantity,
    int DecimalPlaces,
    bool IsActive);

public sealed record SaveProductUnitRequest(
    string Code,
    string Name,
    string Symbol,
    bool AllowsFractionalQuantity,
    int DecimalPlaces,
    bool IsActive = true);

public sealed record ProductLinkInput(
    Guid ParentProductId,
    bool SharesInventory,
    decimal? InventoryFactor,
    bool SharesPrice,
    decimal? PriceFactor);

public sealed record ProductLinkDetail(
    Guid ParentProductId,
    string ParentProductCode,
    string ParentProductName,
    bool SharesInventory,
    decimal? InventoryFactor,
    bool SharesPrice,
    decimal? PriceFactor);

public sealed record LinkedProductInput(
    Guid ChildProductId,
    bool SharesInventory,
    decimal? InventoryFactor,
    bool SharesPrice,
    decimal? PriceFactor);

public sealed record LinkedProductDetail(
    Guid ChildProductId,
    string ChildProductCode,
    string ChildProductName,
    bool SharesInventory,
    decimal? InventoryFactor,
    bool SharesPrice,
    decimal? PriceFactor);

public sealed record ProductMerchandisingConfiguration(
    Guid ProductId,
    Guid? ProductCategoryId,
    Guid? ProductBrandId,
    string BaseUnitCode,
    bool ManageInventory,
    bool AllowsFractionalSale,
    bool IsWeighable,
    ScaleConfigurationInput? Scale,
    IReadOnlyCollection<ProductBarcodeInput> Barcodes,
    ProductLinkDetail? Link,
    IReadOnlyCollection<LinkedProductDetail> LinkedProducts);

public sealed record SaveProductMerchandisingRequest(
    Guid? ProductCategoryId,
    Guid? ProductBrandId,
    string BaseUnitCode,
    bool ManageInventory,
    bool AllowsFractionalSale,
    bool IsWeighable,
    ScaleConfigurationInput? Scale,
    IReadOnlyCollection<ProductBarcodeInput> Barcodes,
    ProductLinkInput? Link,
    IReadOnlyCollection<LinkedProductInput> LinkedProducts);

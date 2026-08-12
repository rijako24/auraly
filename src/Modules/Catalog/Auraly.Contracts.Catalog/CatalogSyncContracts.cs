using Auraly.BuildingBlocks.Domain.Identifiers;

namespace Auraly.Contracts.Catalog;

public sealed record PosCatalogProduct(
    ProductId ProductId,
    string ProductCode,
    string Name,
    IReadOnlyCollection<string> Barcodes,
    bool IsActive,
    bool IsWeighed,
    string TaxCode,
    decimal TaxRate);

public sealed record CatalogChange(
    long Version,
    string ChangeKind,
    PosCatalogProduct Product);

public sealed record CatalogSyncPage(
    string NextCursor,
    bool HasMore,
    string IntegrityHash,
    IReadOnlyCollection<CatalogChange> Changes);

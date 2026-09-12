using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Auraly.Contracts.Catalog;

/// <summary>
/// Computes the stable integrity digest for a POS catalog bootstrap page.
/// The protected core deliberately remains additive-contract safe so a client
/// and server can be updated in either order without rejecting the page.
/// </summary>
public static class CatalogBootstrapIntegrity
{
    public static string Compute(IEnumerable<PosCatalogItem> items)
    {
        var stableItems = items.Select(item => new StableCatalogItem(
            item.ProductId,
            item.ProductCode,
            item.Reference,
            item.Name,
            item.BaseUnitCode,
            item.TaxCode,
            item.TaxRate,
            item.UnitPrice,
            item.CurrencyCode,
            item.IsActive,
            item.IsWeighable,
            item.AllowsFractionalSale,
            item.Scale,
            item.Barcodes,
            item.Identifiers,
            item.UnitCost,
            item.ManagesStock,
            item.CategoryName,
            item.ProductCategoryId,
            item.ProductBrandId,
            item.ProductCategoryAncestorIds,
            item.AverageUnitCost,
            item.LatestUnitCost,
            item.TargetMarginPercent)).ToArray();
        return Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(stableItems))))
            .ToLowerInvariant();
    }

    public static bool IsValid(IEnumerable<PosCatalogItem> items, string integrityHash)
    {
        if (string.IsNullOrWhiteSpace(integrityHash))
            return false;
        try
        {
            var expected = Convert.FromHexString(Compute(items));
            var received = Convert.FromHexString(integrityHash);
            return expected.Length == received.Length &&
                   CryptographicOperations.FixedTimeEquals(expected, received);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private sealed record StableCatalogItem(
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
        decimal UnitCost,
        bool ManagesStock,
        string? CategoryName,
        Guid? ProductCategoryId,
        Guid? ProductBrandId,
        IReadOnlyCollection<Guid>? ProductCategoryAncestorIds,
        decimal AverageUnitCost,
        decimal LatestUnitCost,
        decimal? TargetMarginPercent);
}

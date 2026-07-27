using Auraly.BuildingBlocks.Domain.Identifiers;

namespace Auraly.Domain.Catalog;

public sealed class Product
{
    private readonly HashSet<string> _barcodes = new(StringComparer.OrdinalIgnoreCase);

    public Product(ProductId id, TenantId tenantId, string productCode, string name)
    {
        if (id.Value == Guid.Empty) throw new ArgumentException("A product ID is required.", nameof(id));
        if (tenantId.Value == Guid.Empty) throw new ArgumentException("A tenant ID is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(productCode)) throw new ArgumentException("A product code is required.", nameof(productCode));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A product name is required.", nameof(name));

        Id = id;
        TenantId = tenantId;
        ProductCode = productCode.Trim();
        Name = name.Trim();
        IsActive = true;
    }

    public ProductId Id { get; }
    public TenantId TenantId { get; }
    public string ProductCode { get; }
    public string Name { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsWeighed { get; private set; }
    public string? ScalePrefix { get; private set; }
    public IReadOnlySet<string> Barcodes => _barcodes;

    public void AddBarcode(string barcode)
    {
        var normalized = NormalizeBarcode(barcode);
        if (!_barcodes.Add(normalized))
        {
            throw new InvalidOperationException($"Barcode '{normalized}' is already assigned to this product.");
        }
    }

    public void ConfigureScale(bool isWeighed, string? scalePrefix)
    {
        if (isWeighed && string.IsNullOrWhiteSpace(scalePrefix))
        {
            throw new ArgumentException("A weighed product requires a scale prefix.", nameof(scalePrefix));
        }

        IsWeighed = isWeighed;
        ScalePrefix = isWeighed ? scalePrefix!.Trim() : null;
    }

    public void Deactivate() => IsActive = false;

    private static string NormalizeBarcode(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            throw new ArgumentException("A barcode is required.", nameof(barcode));
        }

        var normalized = barcode.Trim();
        if (normalized.Length > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(barcode), "A barcode cannot exceed 64 characters.");
        }

        return normalized;
    }
}

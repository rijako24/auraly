using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Domain.Catalog;

namespace Auraly.Foundation.Tests;

public sealed class ProductTests
{
    [Fact]
    public void Product_supports_multiple_normalized_barcodes_and_scale_configuration()
    {
        var product = new Product(
            new ProductId(Guid.NewGuid()),
            new TenantId(Guid.NewGuid()),
            "PROD-001",
            "Arroz");

        product.AddBarcode(" 7701234567890 ");
        product.AddBarcode("ALT-001");
        product.ConfigureScale(true, "21");

        Assert.Equal(2, product.Barcodes.Count);
        Assert.Contains("7701234567890", product.Barcodes);
        Assert.True(product.IsWeighed);
        Assert.Equal("21", product.ScalePrefix);
    }

    [Fact]
    public void Duplicate_barcode_on_same_product_is_rejected()
    {
        var product = new Product(
            new ProductId(Guid.NewGuid()),
            new TenantId(Guid.NewGuid()),
            "PROD-001",
            "Arroz");
        product.AddBarcode("ABC");

        Assert.Throws<InvalidOperationException>(() => product.AddBarcode(" abc "));
    }
}

using Auraly.BuildingBlocks.Domain.Documents;

namespace Auraly.Foundation.Tests;

public sealed class AuralyDocumentNumberTests
{
    [Theory]
    [InlineData(AuralyDocumentTypes.SalesInvoice, "VTA")]
    [InlineData(AuralyDocumentTypes.SalesOrder, "PED")]
    [InlineData(AuralyDocumentTypes.SalesReturn, "DVT")]
    [InlineData(AuralyDocumentTypes.GoodsReceipt, "EMC")]
    [InlineData(AuralyDocumentTypes.PurchaseOrder, "OCP")]
    [InlineData(AuralyDocumentTypes.Purchase, "CMP")]
    [InlineData(AuralyDocumentTypes.PurchaseReturn, "DCP")]
    [InlineData(AuralyDocumentTypes.WarehouseTransfer, "TRB")]
    [InlineData(AuralyDocumentTypes.InventoryEntry, "EIN")]
    [InlineData(AuralyDocumentTypes.InventoryExit, "SIN")]
    [InlineData(AuralyDocumentTypes.InventoryAdjustment, "AJI")]
    [InlineData(AuralyDocumentTypes.Damage, "AVE")]
    [InlineData(AuralyDocumentTypes.ProductConversion, "CNV")]
    [InlineData(AuralyDocumentTypes.CashCount, "ARQ")]
    [InlineData(AuralyDocumentTypes.CustomsLoad, "ADU")]
    [InlineData(AuralyDocumentTypes.CashReceipt, "ING")]
    [InlineData(AuralyDocumentTypes.CashDisbursement, "EGR")]
    [InlineData(AuralyDocumentTypes.ReceivablePayment, "RCC")]
    [InlineData(AuralyDocumentTypes.PayablePayment, "PGP")]
    public void Every_supported_document_has_its_canonical_prefix(
        string documentType,
        string prefix)
    {
        Assert.Equal(prefix, AuralyDocumentTypes.DefaultPrefix(documentType));
    }

    [Fact]
    public void Register_code_and_eight_digit_counter_produce_the_compact_number()
    {
        var number = AuralyDocumentNumberAssignment.Create(
            Guid.NewGuid(),
            AuralyDocumentTypes.SalesInvoice,
            "vta",
            "03",
            42,
            8);

        Assert.Equal("VTA03-00000042", number.FullNumber);
    }

    [Fact]
    public void Equal_counters_from_two_registers_are_not_ambiguous()
    {
        var series1 = Guid.NewGuid();
        var series2 = Guid.NewGuid();
        var first = AuralyDocumentNumberAssignment.Create(
            series1, AuralyDocumentTypes.SalesInvoice, "VTA", "03", 42, 8);
        var second = AuralyDocumentNumberAssignment.Create(
            series2, AuralyDocumentTypes.SalesInvoice, "VTA", "06", 42, 8);

        Assert.NotEqual(first.FullNumber, second.FullNumber);
        Assert.Equal("VTA03-00000042", first.FullNumber);
        Assert.Equal("VTA06-00000042", second.FullNumber);
    }

    [Fact]
    public void A_legacy_or_arbitrary_prefix_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            AuralyDocumentNumberAssignment.Create(
                Guid.NewGuid(),
                AuralyDocumentTypes.SalesInvoice,
                "FB",
                "03",
                1,
                8));
    }
}

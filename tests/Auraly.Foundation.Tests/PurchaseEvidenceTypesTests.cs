using Auraly.Contracts.Purchasing;

namespace Auraly.Foundation.Tests;

public sealed class PurchaseEvidenceTypesTests
{
    [Theory]
    [InlineData(null, 3)]
    [InlineData(PurchaseEvidenceTypes.SupplierElectronicInvoice, 2)]
    [InlineData(PurchaseEvidenceTypes.BuyerElectronicSupportDocument, 2)]
    [InlineData(PurchaseEvidenceTypes.InternalReceiptVoucher, 1)]
    public void Supplier_policy_limits_receipt_choices_and_always_keeps_internal_voucher(
        string? policy,
        int expectedCount)
    {
        var allowed = PurchaseEvidenceTypes.AllowedFor(policy);

        Assert.Equal(expectedCount, allowed.Count);
        Assert.Contains(PurchaseEvidenceTypes.InternalReceiptVoucher, allowed);
        if (policy is not null)
            Assert.Contains(policy, allowed);
    }

    [Fact]
    public void Unsupported_supplier_policy_allows_no_receipt_choice()
    {
        Assert.Empty(PurchaseEvidenceTypes.AllowedFor("ParallelFiscalEngine"));
    }
}

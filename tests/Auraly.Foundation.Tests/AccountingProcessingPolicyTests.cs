using Auraly.Commerce.Accounting.Application;

namespace Auraly.Foundation.Tests;

public sealed class AccountingProcessingPolicyTests
{
    [Theory]
    [InlineData("SalesInvoice")]
    [InlineData("SalesReceipt")]
    [InlineData("SalesReturn")]
    [InlineData("GoodsReceipt")]
    [InlineData("Expense")]
    [InlineData("PurchaseReturn")]
    [InlineData("PayablePayment")]
    [InlineData("ReceivablePayment")]
    [InlineData("CashReceipt")]
    [InlineData("CashDisbursement")]
    [InlineData("PayrollAccrual")]
    [InlineData("PayrollPayment")]
    [InlineData("PayrollAdjustment")]
    [InlineData("StockCount")]
    [InlineData("InventoryAdjustment")]
    [InlineData("Damage")]
    [InlineData("ProductConversion")]
    [InlineData("WarehouseTransferReceipt")]
    [InlineData("DispatchCashDifference")]
    public void Supports_returns_true_for_every_canonical_document_type(string documentType)
    {
        Assert.True(AccountingProcessingPolicy.Supports(documentType));
    }

    [Theory]
    [InlineData("")]
    [InlineData("SalesOrder")]
    [InlineData("salesinvoice")]
    public void Supports_rejects_unknown_or_noncanonical_document_types(string documentType)
    {
        Assert.False(AccountingProcessingPolicy.Supports(documentType));
    }
}

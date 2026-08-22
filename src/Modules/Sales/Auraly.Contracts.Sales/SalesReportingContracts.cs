namespace Auraly.Contracts.Sales;

public static class SalesReportingPermissionCodes
{
    public const string Read = "sales.reports.read";
}

public static class SalesReportingDimensions
{
    public const string Customer = "customer";
    public const string Seller = "seller";
    public const string Supplier = "supplier";
    public const string Product = "product";
    public const string Category = "category";
    public const string Warehouse = "warehouse";
    public const string Day = "day";
    public const string Month = "month";
    public const string PaymentMethod = "payment-method";
    public const string Tax = "tax";

    public static bool IsSupported(string value) => value is Customer or Seller or Supplier or Product or
        Category or Warehouse or Day or Month or PaymentMethod or Tax;
}

public sealed record SalesReportingUserIdentity(
    Guid UserId, Guid TenantId, Guid BusinessId, IReadOnlySet<string> Permissions);

public sealed record SalesReportFilter(
    DateOnly From, DateOnly To, Guid? CustomerId = null, Guid? SellerId = null,
    Guid? SupplierId = null, Guid? ProductId = null, Guid? CategoryId = null,
    Guid? WarehouseId = null, string? DocumentType = null);

public sealed record SalesReportTotals(
    long DocumentCount, decimal UnitsSold, decimal UnitsReturned, decimal GrossSales,
    decimal Discounts, decimal Returns, decimal NetUntaxedSales, decimal NetTax,
    decimal NetTotalSales, decimal NetRecognizedCost, decimal GrossProfit,
    decimal GrossMarginPercent, decimal CreditSales, decimal Collected, decimal Refunded);

public sealed record SalesReportTrendPoint(
    DateOnly Period, long DocumentCount, decimal GrossSales, decimal Returns,
    decimal NetSales, decimal GrossProfit);

public sealed record SalesReportSummary(
    SalesReportTotals Current, SalesReportTotals? Comparison,
    decimal? NetSalesChangePercent, IReadOnlyList<SalesReportTrendPoint> Trend,
    DateTimeOffset? ProjectedThrough);

public sealed record SalesReportBreakdownRow(
    string Key, string Label, long DocumentCount, decimal Quantity,
    decimal GrossSales, decimal Discounts, decimal Returns, decimal NetUntaxedSales,
    decimal Tax, decimal NetSales, decimal RecognizedCost, decimal GrossProfit,
    decimal GrossMarginPercent, decimal ParticipationPercent);

public sealed record SalesReportDocumentRow(
    Guid DocumentId, string DocumentType, string DocumentNumber, string? FiscalNumber,
    DateTimeOffset IssuedAt, string CustomerName, string SellerName, string WarehouseName,
    decimal GrossAmount, decimal DiscountAmount, decimal UntaxedAmount, decimal TaxAmount,
    decimal TotalAmount, decimal ReturnedTotalAmount, decimal NetTotalAmount,
    decimal GrossProfit, string? FiscalStatus);

public sealed record SalesReportDocumentPage(
    IReadOnlyList<SalesReportDocumentRow> Items, int Page, int PageSize, int TotalCount);

public sealed record SalesReportLineRow(
    Guid FactId, string MovementType, DateTimeOffset OccurredAt, string ProductCode,
    string ProductName, string? CategoryName, decimal Quantity, decimal GrossAmount,
    decimal DiscountAmount, decimal UntaxedAmount, decimal TaxAmount, decimal TotalAmount,
    decimal RecognizedCostAmount, string? ReturnReasonCode, string? ReturnDisposition);

public sealed record SalesReportDocumentDetail(
    SalesReportDocumentRow Document, IReadOnlyList<SalesReportLineRow> Lines);

public sealed class SalesReportingForbiddenException(string message) : Exception(message);
public sealed class SalesReportingValidationException(string message) : Exception(message);

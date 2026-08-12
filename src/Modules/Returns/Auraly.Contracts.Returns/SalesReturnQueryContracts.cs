namespace Auraly.Contracts.Returns;

public static class SalesReturnReasonCodes
{
    public const string CustomerChangedMind = "CustomerChangedMind";
    public const string WrongProduct = "WrongProduct";
    public const string QualityIssue = "QualityIssue";
    public const string Damaged = "Damaged";
    public const string BillingCorrection = "BillingCorrection";
    public const string Other = "Other";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(
        [CustomerChangedMind, WrongProduct, QualityIssue, Damaged, BillingCorrection, Other],
        StringComparer.Ordinal);
}

public static class SalesReturnRefundMethods
{
    public const string Cash = "Cash";
}

public sealed record ReturnableSalesQuery(
    int Page,
    int PageSize,
    string? Search,
    DateOnly? From,
    DateOnly? To,
    bool? WithAvailableQuantity);

public sealed record ReturnableSaleListItem(
    Guid DocumentId,
    string DocumentNumber,
    string FiscalNumber,
    string Cufe,
    DateTimeOffset IssuedAt,
    Guid? CustomerId,
    string CustomerName,
    string CustomerIdentification,
    Guid WarehouseId,
    string WarehouseName,
    decimal TotalAmount,
    decimal ReturnedAmount,
    bool HasAvailableQuantity,
    string FiscalStatus);

public sealed record ReturnableSalePage(
    IReadOnlyList<ReturnableSaleListItem> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (decimal)PageSize);
}

public sealed record ReturnableSalePayment(
    int PaymentNumber,
    string MethodCode,
    decimal OriginalAmount,
    decimal RefundedAmount,
    decimal AvailableAmount);

public sealed record ReturnableSaleLine(
    int OriginalLineNumber,
    Guid ProductId,
    string ProductCode,
    string? Reference,
    string Description,
    decimal SoldQuantity,
    decimal ReturnedQuantity,
    decimal AvailableQuantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    string TaxCode,
    decimal TaxRate,
    decimal UntaxedAmount,
    decimal TaxAmount,
    decimal LineTotal);

public sealed record ReturnableSale(
    Guid DocumentId,
    string DocumentNumber,
    string FiscalNumber,
    string Cufe,
    DateTimeOffset IssuedAt,
    Guid? CustomerId,
    string CustomerName,
    string CustomerIdentification,
    Guid WarehouseId,
    string WarehouseName,
    decimal TotalAmount,
    decimal ReturnedAmount,
    decimal ReceivableOutstanding,
    string FiscalStatus,
    IReadOnlyList<ReturnableSalePayment> Payments,
    IReadOnlyList<ReturnableSaleLine> Lines);

public sealed record SalesReturnQuery(
    int Page,
    int PageSize,
    string? Search,
    string? Status,
    DateOnly? From,
    DateOnly? To);

public sealed record SalesReturnListItem(
    Guid ReturnId,
    string DocumentNumber,
    Guid OriginalDocumentId,
    string OriginalDocumentNumber,
    string CustomerName,
    DateTimeOffset ReturnedAt,
    string EconomicResolution,
    decimal TotalAmount,
    string Status,
    string? FiscalStatus,
    string ReasonCode);

public sealed record SalesReturnPage(
    IReadOnlyList<SalesReturnListItem> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (decimal)PageSize);
}

public sealed record SalesReturnDetail(
    Guid ReturnId,
    string DocumentNumber,
    Guid OriginalDocumentId,
    string OriginalDocumentNumber,
    string CustomerName,
    string CustomerIdentification,
    Guid WarehouseId,
    string WarehouseName,
    DateTimeOffset ReturnedAt,
    string EconomicResolution,
    string? RefundMethodCode,
    decimal UntaxedAmount,
    decimal TaxAmount,
    decimal TotalAmount,
    string Status,
    string? FiscalStatus,
    string ReasonCode,
    string ReasonDescription,
    string? Notes,
    IReadOnlyList<SalesReturnLineSnapshot> Lines);

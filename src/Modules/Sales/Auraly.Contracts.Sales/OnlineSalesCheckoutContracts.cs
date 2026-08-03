namespace Auraly.Contracts.Sales;

public sealed record OnlineSalesPayment(
    string MethodCode,
    decimal Amount,
    string? Reference);

public sealed record OnlineSalesCreditTerms(
    decimal Amount,
    DateTimeOffset DueDate);

public sealed record CompleteOnlineSalesDraftRequest(
    long ExpectedVersion,
    IReadOnlyList<OnlineSalesPayment> Payments,
    OnlineSalesCreditTerms? Credit = null);

public sealed record OnlineSalesReceiptLine(
    string ProductCode,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal Discount,
    decimal Tax,
    decimal Total);

public sealed record OnlineSalesReceipt(
    Guid DocumentId,
    string DocumentNumber,
    string FiscalNumber,
    DateTimeOffset IssuedAt,
    string CustomerIdentification,
    IReadOnlyList<OnlineSalesReceiptLine> Lines,
    IReadOnlyList<OnlineSalesPayment> Payments,
    decimal UntaxedAmount,
    decimal TaxAmount,
    decimal PayableAmount,
    string Cufe,
    string QrPayload,
    string FiscalStatus,
    string CustomerName);

public sealed record CompleteOnlineSalesDraftResponse(
    OnlineSalesReceipt Receipt,
    OnlineSalesDraft NextDraft,
    bool IsDuplicate);

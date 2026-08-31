namespace Auraly.Contracts.Sales;

public static class ServiceInvoicePermissionCodes
{
    public const string Read = "service-invoices.read";
    public const string Create = "service-invoices.create";
    public const string OverridePrice = "service-invoices.price.override";
    public const string Discount = "service-invoices.discount";
    public const string Issue = "service-invoices.issue";
    public const string Print = "service-invoices.print";
}

public sealed record ServiceInvoiceSearchRequest(
    Guid BusinessId,
    string? Query = null,
    int Page = 1,
    int PageSize = 20);

public sealed record BillableServiceItem(
    Guid BillableServiceId,
    string Code,
    string Name,
    string? Description,
    string UnitLabel,
    string UblUnitCode,
    decimal UnitPrice,
    string TaxCode,
    string TaxName,
    decimal TaxRate);

public sealed record BillableServicePage(
    IReadOnlyList<BillableServiceItem> Items,
    int Page,
    int PageSize,
    int Total);

public sealed record ServiceInvoiceCustomerItem(
    Guid CustomerId,
    string Identification,
    string DisplayName,
    string? Email);

public sealed record ServiceInvoiceCustomerPage(
    IReadOnlyList<ServiceInvoiceCustomerItem> Items,
    int Page,
    int PageSize,
    int Total);

public sealed record IssueServiceInvoiceLineRequest(
    Guid BillableServiceId,
    decimal Quantity,
    string? Description = null,
    decimal? UnitPrice = null,
    string? DiscountKind = null,
    decimal DiscountValue = 0);

public sealed record IssueServiceInvoiceRequest(
    Guid BusinessId,
    Guid CustomerId,
    IReadOnlyList<IssueServiceInvoiceLineRequest> Lines,
    string PaymentMethodCode,
    string? PaymentReference = null,
    decimal CreditAmount = 0,
    DateTimeOffset? CreditDueDate = null);

public sealed record IssuedServiceInvoice(
    Guid DocumentId,
    string DocumentNumber,
    string FiscalNumber,
    string Cufe,
    decimal UntaxedAmount,
    decimal TaxAmount,
    decimal PayableAmount,
    decimal CreditAmount,
    string FiscalStatus,
    bool IsReplay);

public sealed record ServiceInvoiceHistoryRequest(
    Guid BusinessId,
    string? Query = null,
    DateOnly? From = null,
    DateOnly? To = null,
    string? FiscalStatus = null,
    int Page = 1,
    int PageSize = 20);

public sealed record ServiceInvoiceHistoryItem(
    Guid DocumentId,
    string DocumentNumber,
    string FiscalNumber,
    DateTimeOffset IssuedAt,
    string CustomerIdentification,
    string CustomerName,
    decimal PayableAmount,
    decimal CreditAmount,
    string FiscalStatus);

public sealed record ServiceInvoiceHistoryPage(
    IReadOnlyList<ServiceInvoiceHistoryItem> Items,
    int Page,
    int PageSize,
    int Total);

public sealed record ServiceInvoiceDetailLine(
    int LineNumber,
    string ServiceCode,
    string Description,
    string UnitCode,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal UntaxedAmount,
    string TaxName,
    decimal TaxRate,
    decimal TaxAmount,
    decimal LineTotal);

public sealed record ServiceInvoiceDetailPayment(
    int PaymentNumber,
    string MethodCode,
    decimal Amount,
    string? Reference);

public sealed record ServiceInvoiceDetail(
    Guid DocumentId,
    Guid BusinessId,
    string BusinessName,
    string DocumentNumber,
    string FiscalNumber,
    DateTimeOffset IssuedAt,
    string CustomerIdentification,
    string CustomerName,
    string? CustomerEmail,
    decimal UntaxedAmount,
    decimal TaxAmount,
    decimal PayableAmount,
    decimal CreditAmount,
    DateTimeOffset? CreditDueDate,
    string Cufe,
    string FiscalStatus,
    string QrPayload,
    IReadOnlyList<ServiceInvoiceDetailLine> Lines,
    IReadOnlyList<ServiceInvoiceDetailPayment> Payments);

using Auraly.Contracts.Sales;

namespace Auraly.Contracts.Orders;

public sealed record InvoiceOrdersRequest(
    Guid WorkSessionId,
    Guid WarehouseId,
    Guid UserId,
    IReadOnlyList<Guid> OrderIds,
    string PaymentMethodCode,
    string? PaymentReference,
    string DocumentType = "SalesInvoice",
    Guid? BankAccountId = null,
    string? PaymentNotes = null,
    OrderInvoiceChargeSelection? Charge = null,
    IReadOnlyList<OrderInvoiceChargeSelection>? Charges = null);

public sealed record OrderInvoiceChargeSelection(
    Guid ChargeId,
    long ChargeVersion,
    Guid SupplierId,
    decimal? ManualAmount = null);

public sealed record InvoiceOrderResult(
    Guid OrderId,
    string OrderNumber,
    string Status,
    Guid? DocumentId,
    string? DocumentNumber,
    string? Error,
    OnlineSalesReceipt? Receipt = null);

public sealed record OrderCreditValidationIssue(
    Guid? CustomerId,
    string CustomerName,
    string? CustomerIdentification,
    decimal RequestedAmount,
    decimal? AvailableCredit,
    string Reason);

public sealed record InvoiceOrdersResponse(
    Guid OperationId,
    string Status,
    int RequestedCount,
    int CompletedCount,
    int FailedCount,
    bool IsReplay,
    IReadOnlyList<InvoiceOrderResult> Results,
    string? PrintStatus = null,
    string? PrintError = null,
    IReadOnlyList<OrderCreditValidationIssue>? CreditValidationIssues = null);

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
    string? PaymentNotes = null);

public sealed record InvoiceOrderResult(
    Guid OrderId,
    string OrderNumber,
    string Status,
    Guid? DocumentId,
    string? DocumentNumber,
    string? Error);

public sealed record InvoiceOrdersResponse(
    Guid OperationId,
    string Status,
    int RequestedCount,
    int CompletedCount,
    int FailedCount,
    bool IsReplay,
    IReadOnlyList<InvoiceOrderResult> Results,
    string? PrintStatus = null,
    string? PrintError = null);

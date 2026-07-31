namespace Auraly.Contracts.Orders;

public sealed record InvoiceOrdersRequest(
    Guid RegisterId,
    Guid UserId,
    IReadOnlyList<Guid> OrderIds,
    string PaymentMethodCode,
    string? PaymentReference);

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
    IReadOnlyList<InvoiceOrderResult> Results);

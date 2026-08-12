namespace Auraly.Contracts.Sales;

public sealed record OnlineSalesOrderImportLine(
    Guid ProductId,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountAmount);

public sealed record ImportOnlineSalesOrderRequest(
    Guid SourceOrderId,
    string OrderNumber,
    Guid? CustomerId,
    IReadOnlyList<OnlineSalesOrderImportLine> Lines,
    long ExpectedVersion);

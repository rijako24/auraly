namespace Auraly.Contracts.Sales;

public sealed record OnlineSalesOrderImportLine(
    Guid ProductId,
    decimal Quantity,
    decimal PublicUnitPrice,
    decimal DiscountAmount,
    decimal PublicLineTotal,
    string PriceSource,
    decimal DocumentUnitCost);

public sealed record ImportOnlineSalesOrderRequest(
    Guid SourceOrderId,
    string OrderNumber,
    Guid? CustomerId,
    IReadOnlyList<OnlineSalesOrderImportLine> Lines,
    long ExpectedVersion,
    Guid? PartySiteId = null);

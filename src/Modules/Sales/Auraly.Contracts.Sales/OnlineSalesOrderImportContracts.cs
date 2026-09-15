namespace Auraly.Contracts.Sales;

public sealed record OnlineSalesOrderImportLine(
    Guid ProductId,
    decimal Quantity,
    decimal PublicUnitPrice,
    decimal PublicDiscountAmount,
    decimal PublicLineTotal,
    string PriceSource = "Order",
    decimal? DocumentUnitCost = null);

public sealed record ImportOnlineSalesOrderRequest(
    Guid SourceOrderId,
    string OrderNumber,
    Guid? CustomerId,
    IReadOnlyList<OnlineSalesOrderImportLine> Lines,
    long ExpectedVersion,
    Guid? PartySiteId = null);

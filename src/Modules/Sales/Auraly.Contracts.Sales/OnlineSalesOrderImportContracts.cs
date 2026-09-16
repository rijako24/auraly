namespace Auraly.Contracts.Sales;

public sealed record OnlineSalesOrderImportLine(
    Guid ProductId,
    string? ProductCode,
    string Description,
    string UnitCode,
    decimal Quantity,
    decimal PublicUnitPrice,
    decimal DiscountAmount,
    decimal PublicLineTotal,
    string PriceSource,
    decimal DocumentUnitCost,
    string TaxCode,
    decimal TaxRate);

public sealed record ImportOnlineSalesOrderRequest(
    Guid SourceOrderId,
    string OrderNumber,
    Guid? CustomerId,
    string Currency,
    IReadOnlyList<OnlineSalesOrderImportLine> Lines,
    long ExpectedVersion,
    Guid? PartySiteId = null);

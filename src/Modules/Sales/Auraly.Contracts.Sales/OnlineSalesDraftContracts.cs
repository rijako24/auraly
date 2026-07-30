namespace Auraly.Contracts.Sales;

public sealed record OnlineSalesDraftContext(
    Guid BusinessId,
    Guid LocationId,
    Guid RegisterId);

public sealed record OpenOnlineSalesDraftRequest(OnlineSalesDraftContext Context);

public sealed record AddOnlineSalesDraftProductRequest(
    Guid ProductId,
    decimal Quantity,
    long ExpectedVersion);

public sealed record CaptureOnlineSalesDraftProductRequest(
    string Value,
    decimal Quantity,
    long ExpectedVersion);

public sealed record ChangeOnlineSalesDraftQuantityRequest(
    decimal Quantity,
    long ExpectedVersion);

public sealed record SetOnlineSalesDraftDiscountRequest(
    decimal Discount,
    long ExpectedVersion);

public sealed record SelectOnlineSalesDraftCustomerRequest(
    Guid? CustomerId,
    long ExpectedVersion);

public sealed record RemoveOnlineSalesDraftLineRequest(long ExpectedVersion);

public sealed record ResetOnlineSalesDraftRequest(long ExpectedVersion);

public sealed record OnlineSalesCustomer(
    Guid CustomerId,
    string Identification,
    string Name,
    Guid? PriceListId,
    Guid? PriceChannelId);

public sealed record OnlineSalesCustomerSelection(
    OnlineSalesDraft Draft,
    OnlineSalesCustomer? Customer);

public sealed record OnlineSalesDraftLine(
    Guid LineId,
    Guid ProductId,
    string ProductCode,
    string Description,
    string UnitCode,
    string TaxCode,
    decimal TaxRate,
    decimal Quantity,
    decimal BaseUnitPrice,
    decimal UnitPrice,
    string CurrencyCode,
    string PriceSource,
    decimal Discount,
    decimal Net,
    decimal Tax,
    decimal Total);

public sealed record OnlineSalesDraft(
    Guid DraftId,
    Guid BusinessId,
    Guid LocationId,
    Guid WarehouseId,
    Guid RegisterId,
    Guid UserId,
    Guid? CustomerId,
    Guid? SellerId,
    string Status,
    long Version,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<OnlineSalesDraftLine> Lines,
    decimal UntaxedAmount,
    decimal TaxAmount,
    decimal PayableAmount);

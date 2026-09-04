namespace Auraly.Contracts.Sales;

public sealed record OnlineSalesDraftContext(
    Guid BusinessId,
    Guid WarehouseId,
    Guid WorkSessionId);

public sealed record OpenOnlineSalesDraftRequest(OnlineSalesDraftContext Context);

public sealed record AddOnlineSalesDraftItemRequest(
    string Selector,
    decimal Quantity,
    long ExpectedVersion);

public sealed record ChangeOnlineSalesDraftQuantityRequest(
    decimal Quantity,
    long ExpectedVersion);

public sealed record SetOnlineSalesDraftDiscountRequest(
    decimal Discount,
    long ExpectedVersion);

public sealed record UpdateOnlineSalesDraftLinesRequest(
    IReadOnlyList<UpdateOnlineSalesDraftLineRequest> Lines,
    long ExpectedVersion);

public sealed record UpdateOnlineSalesDraftLineRequest(
    Guid LineId,
    string Description,
    decimal UnitPrice,
    decimal Discount,
    decimal DocumentUnitCost = 0);

public sealed record SelectOnlineSalesDraftCustomerRequest(
    Guid? CustomerId,
    long ExpectedVersion);

public sealed record RemoveOnlineSalesDraftLineRequest(long ExpectedVersion);

public sealed record ResetOnlineSalesDraftRequest(long ExpectedVersion);

public sealed record PauseOnlineSalesDraftRequest(
    string Name,
    string? Reference,
    string? Observation,
    long ExpectedVersion);

public sealed record RecoverOnlineSalesDraftRequest(
    long ExpectedTemporaryVersion,
    long ExpectedActiveVersion);

public sealed record RemoveOnlineSalesTemporaryRequest(long ExpectedVersion);

public sealed record SearchOnlineSalesRequest(
    OnlineSalesDraftContext Context,
    string? Search = null,
    int Skip = 0,
    int Take = 50,
    Guid? CustomerId = null);

public sealed record OnlineSalesProduct(
    Guid ProductId,
    string ProductCode,
    string? Reference,
    string Name,
    string BaseUnitCode,
    string TaxCode,
    decimal TaxRate,
    decimal UnitPrice,
    string CurrencyCode,
    bool IsActive,
    bool IsWeighable,
    bool AllowsFractionalSale,
    string PriceSource);

public sealed record OnlineSalesProductPage(
    IReadOnlyList<OnlineSalesProduct> Items,
    bool HasMore,
    int? NextOffset);

public sealed record OnlineSalesCustomerPage(
    IReadOnlyList<OnlineSalesCustomer> Items,
    bool HasMore,
    int? NextOffset);

public sealed record OnlineSalesCustomer(
    Guid CustomerId,
    string Identification,
    string Name,
    Guid? PriceChannelId,
    bool RequiresElectronicInvoice,
    bool IsCreditEnabled,
    int DefaultCreditDueDays,
    decimal? AvailableCredit);

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
    decimal DocumentUnitCost,
    bool AllowsDocumentCostOverride,
    bool AllowsFractionalSale,
    decimal Net,
    decimal Tax,
    decimal Total,
    decimal PromotionDiscount = 0)
{
    public decimal TotalDiscount => Discount + PromotionDiscount;
}

public sealed record OnlineSalesDraft(
    Guid DraftId,
    Guid BusinessId,
    Guid WarehouseId,
    Guid WorkSessionId,
    Guid UserId,
    Guid? CustomerId,
    Guid? SellerId,
    string Status,
    string? Name,
    string? Reference,
    string? Observation,
    long Version,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<OnlineSalesDraftLine> Lines,
    decimal UntaxedAmount,
    decimal TaxAmount,
    decimal PayableAmount,
    Guid? SourceOrderId = null);

public sealed record OnlineSalesInventoryIssue(
    Guid LineId,
    Guid ProductId,
    string ProductCode,
    string Description,
    decimal RequestedQuantity,
    decimal AvailableQuantity);

public sealed record OnlineSalesInventoryValidation(
    bool IsValid,
    bool WasValidated,
    IReadOnlyList<OnlineSalesInventoryIssue> Issues);

public sealed record GetOnlineSalesCustomerRequest(
    OnlineSalesDraftContext Context,
    Guid CustomerId);

public sealed record SearchOnlineSalesIssuedSalesRequest(
    OnlineSalesDraftContext Context,
    string? Search = null,
    int Skip = 0,
    int Take = 50);

public sealed record OnlineSalesIssuedSale(
    Guid DocumentId,
    string DocumentType,
    string DocumentNumber,
    string FiscalNumber,
    DateTimeOffset IssuedAt,
    decimal Total,
    string CustomerIdentification,
    string CustomerName,
    string FiscalStatus);

public sealed record OnlineSalesIssuedSalePage(
    IReadOnlyList<OnlineSalesIssuedSale> Items,
    bool HasMore,
    int? NextOffset);

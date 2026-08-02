namespace Auraly.Contracts.Orders;

public sealed record RecoverOrderIntoSaleRequest(
    Guid WorkSessionId,
    Guid UserId,
    Guid DraftId,
    long ExpectedDraftVersion);

public sealed record RecoveredOrderSale(
    Guid OrderId,
    Guid DraftId,
    long DraftVersion,
    string OrderNumber,
    decimal PayableAmount);

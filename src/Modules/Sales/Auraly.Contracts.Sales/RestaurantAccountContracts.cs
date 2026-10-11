namespace Auraly.Contracts.Sales;

public sealed record RestaurantAccount(
    Guid AccountId, Guid BusinessId, Guid TableId, Guid WarehouseId,
    Guid ResponsibleUserId, string Status, long Version,
    DateTimeOffset OpenedAt, DateTimeOffset UpdatedAt,
    string? ResponsibleUserName = null);

public sealed record OpenRestaurantAccountRequest(
    Guid AccountId, Guid TableId, Guid WarehouseId);

public sealed record CompleteRestaurantAccountRequest(
    Guid WorkSessionId, CompleteOnlineSalesDraftRequest Checkout);

public sealed record RestaurantCollectionAllocationRequest(
    Guid AdditionId, decimal Quantity);

public sealed record CaptureRestaurantCollectionRequest(
    Guid CollectionId, Guid WorkSessionId, long ExpectedAccountVersion,
    IReadOnlyList<RestaurantCollectionAllocationRequest> Allocations,
    IReadOnlyList<OnlineSalesPayment> Payments);

public sealed record ReverseRestaurantCollectionRequest(long ExpectedAccountVersion);

public sealed record RestaurantCollectionAllocation(
    Guid AdditionId, decimal Quantity);

public sealed record RestaurantCollection(
    Guid CollectionId, Guid AccountId, Guid WorkSessionId,
    Guid CashierUserId, decimal Amount, DateTimeOffset CapturedAt,
    IReadOnlyList<RestaurantCollectionAllocation> Allocations,
    IReadOnlyList<OnlineSalesPayment> Payments);

public sealed record RestaurantCollectionSummary(
    Guid AccountId, long AccountVersion, decimal Total, decimal Collected,
    decimal Remaining, IReadOnlyList<RestaurantCollection> Collections,
    IReadOnlyList<RestaurantCollectionAllocation> CoveredQuantities);

public sealed record PrintRestaurantPrebillRequest(long ExpectedVersion);

public sealed record RestaurantPrebillLine(
    string Description, decimal Quantity, decimal UnitPrice,
    decimal Total, string AddedByName);

public sealed record RestaurantPrebill(
    Guid AccountId, string TableCode, long AccountVersion,
    DateTimeOffset PrintedAt, IReadOnlyList<RestaurantPrebillLine> Lines,
    decimal Total);

public sealed record RestaurantTableOccupancy(
    Guid TableId, Guid? AccountId, string State, Guid? ResponsibleUserId,
    DateTimeOffset? OpenedAt, long? AccountVersion);

public sealed record RestaurantAccountAddition(
    Guid AdditionId, Guid AccountId, Guid ProductId,
    string ProductCode, string Description, string UnitCode,
    string TaxCode, decimal TaxRate, decimal Quantity,
    bool AllowsFractionalSale,
    decimal PublicUnitPrice, decimal PromotionDiscountPerUnit,
    string CurrencyCode, string PriceSource, Guid AddedByUserId,
    string AddedByName, DateTimeOffset AddedAt, string Status,
    Guid? StationId = null, string? StationName = null,
    string? PreparationNote = null,
    Guid? ReplacementOfCancellationId = null);

public sealed record RestaurantAccountDraft(
    RestaurantAccount Account,
    IReadOnlyList<RestaurantAccountAddition> Additions);

public sealed record AddRestaurantAccountItemRequest(
    Guid AdditionId, Guid ProductId, decimal Quantity,
    long ExpectedVersion, Guid? ReplacementOfCancellationId = null);

public sealed record RestaurantMenuCategory(
    Guid CategoryId, string Name, int ProductCount);

public sealed record ChangeRestaurantAccountQuantityRequest(
    Guid ChangeId, decimal Quantity, long ExpectedVersion);

public sealed record ChangeRestaurantAccountNoteRequest(
    string? PreparationNote, long ExpectedVersion);

public sealed record SendRestaurantPendingRequest(
    Guid OperationId, long ExpectedVersion, Guid? AdditionId = null);

public sealed record RestaurantCommandCardSummary(
    Guid CardId, Guid StationId, string StationName, string State,
    int LineCount);

public sealed record SendRestaurantPendingResult(
    Guid RoundId, RestaurantAccountDraft Draft,
    IReadOnlyList<RestaurantCommandCardSummary> Cards);

public sealed record RestaurantCommandLineView(
    Guid AdditionId, decimal Quantity, string ProductName,
    string AddedByName, DateTimeOffset AddedAt,
    string? PreparationNote = null,
    Guid? CancellationId = null, decimal? CancellationQuantity = null,
    string? CancellationStatus = null,
    string? CancellationPreparationState = null,
    string? CancellationRowVersion = null);

public sealed record RestaurantCommandCardView(
    Guid CardId, Guid StationId, string StationName, string TableCode,
    string State, DateTimeOffset ReceivedAt, string RowVersion,
    IReadOnlyList<RestaurantCommandLineView> Lines,
    string? PrintState = null, string? PrintError = null);

public sealed record RestaurantCommandBoardPage(
    IReadOnlyList<RestaurantCommandCardView> Items, int Page, bool HasMore,
    IReadOnlyList<string>? StatesWithPendingCorrections = null);

public sealed record ChangeRestaurantCommandStateRequest(
    string State, string RowVersion);

public sealed record RequestRestaurantCancellation(
    Guid CancellationId, decimal Quantity, string ReasonCode,
    string Explanation, long ExpectedAccountVersion);

public sealed record ConfirmRestaurantCancellation(
    string PreparationState, string RowVersion,
    IReadOnlyList<RestaurantCancellationConsumption>? ConsumedComponents = null);
public sealed record RestaurantCancellationConsumption(
    Guid ProductId, decimal Quantity);

public sealed record RestaurantCancellationComponentOption(
    Guid ProductId, string ProductName, decimal ExpectedQuantity);

public sealed record ResolveRestaurantCancellation(
    bool Approve, string RowVersion);

public sealed record RestaurantCancellation(
    Guid CancellationId, Guid AccountId, Guid AdditionId, Guid CardId,
    decimal Quantity, string ReasonCode, string Explanation,
    string Status, string? PreparationState, Guid RequestedByUserId,
    DateTimeOffset RequestedAt, string RowVersion,
    long ExpectedAccountVersion,
    IReadOnlyList<RestaurantCancellationConsumption>? ConsumedComponents = null);

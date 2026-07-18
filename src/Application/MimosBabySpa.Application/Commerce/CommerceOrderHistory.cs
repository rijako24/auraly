namespace MimosBabySpa.Application.Commerce;

public sealed record CommerceOrderHistoryQuery(
    string? ExternalOrderId = null,
    string? ExternalCustomerLookupId = null,
    DateOnly? From = null,
    DateOnly? To = null);

public sealed record CommerceOrderHistoryItem(
    string ExternalProductId,
    string ProductName,
    string? Presentation,
    decimal Quantity,
    decimal UnitPrice);

public sealed record CommerceOrderHistoryRecord(
    string ExternalOrderId,
    string ExternalCustomerLookupId,
    string? CustomerName,
    DateOnly? OrderedOn,
    IReadOnlyList<CommerceOrderHistoryItem> Items);

public interface ICommerceOrderHistorySource
{
    Task<IReadOnlyList<CommerceOrderHistoryRecord>> GetOrderHistoryAsync(
        CommerceAdapterContext context,
        CommerceOrderHistoryQuery query,
        CancellationToken ct = default);
}

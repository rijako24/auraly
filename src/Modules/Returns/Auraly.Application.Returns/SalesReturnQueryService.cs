using Auraly.Contracts.Returns;

namespace Auraly.Application.Returns;

public interface ISalesReturnQueryStore
{
    Task<ReturnableSalePage> ListReturnableSalesAsync(
        SalesReturnUserIdentity user, ReturnableSalesQuery query, CancellationToken cancellationToken);
    Task<ReturnableSale?> GetReturnableSaleAsync(
        SalesReturnUserIdentity user, Guid documentId, CancellationToken cancellationToken);
    Task<SalesReturnPage> ListReturnsAsync(
        SalesReturnUserIdentity user, SalesReturnQuery query, CancellationToken cancellationToken);
    Task<SalesReturnDetail?> GetReturnAsync(
        SalesReturnUserIdentity user, Guid returnId, CancellationToken cancellationToken);
}

public sealed class SalesReturnQueryService(ISalesReturnQueryStore store)
{
    public Task<ReturnableSalePage> ListReturnableSalesAsync(
        SalesReturnUserIdentity user, ReturnableSalesQuery query,
        CancellationToken cancellationToken = default)
    {
        RequireRead(user);
        ValidatePage(query.Page, query.PageSize);
        ValidateDates(query.From, query.To);
        return store.ListReturnableSalesAsync(
            user, query with { Search = Normalize(query.Search, 160) }, cancellationToken);
    }

    public Task<ReturnableSale?> GetReturnableSaleAsync(
        SalesReturnUserIdentity user, Guid documentId,
        CancellationToken cancellationToken = default)
    {
        RequireRead(user);
        if (documentId == Guid.Empty)
            throw new SalesReturnValidationException("DocumentId is required.");
        return store.GetReturnableSaleAsync(user, documentId, cancellationToken);
    }

    public Task<SalesReturnPage> ListReturnsAsync(
        SalesReturnUserIdentity user, SalesReturnQuery query,
        CancellationToken cancellationToken = default)
    {
        RequireRead(user);
        ValidatePage(query.Page, query.PageSize);
        ValidateDates(query.From, query.To);
        if (query.Status is not null && query.Status is not ("Accepted" or "Processed"))
            throw new SalesReturnValidationException("The return status is invalid.");
        return store.ListReturnsAsync(
            user, query with { Search = Normalize(query.Search, 160) }, cancellationToken);
    }

    public Task<SalesReturnDetail?> GetReturnAsync(
        SalesReturnUserIdentity user, Guid returnId,
        CancellationToken cancellationToken = default)
    {
        RequireRead(user);
        if (returnId == Guid.Empty)
            throw new SalesReturnValidationException("ReturnId is required.");
        return store.GetReturnAsync(user, returnId, cancellationToken);
    }

    private static void RequireRead(SalesReturnUserIdentity user)
    {
        if (!user.Permissions.Contains(SalesReturnPermissionCodes.Read))
            throw new SalesReturnForbiddenException(
                $"Permission '{SalesReturnPermissionCodes.Read}' is required.");
    }

    private static void ValidatePage(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 100)
            throw new SalesReturnValidationException("Page and PageSize are invalid.");
    }

    private static void ValidateDates(DateOnly? from, DateOnly? to)
    {
        if (from is not null && to is not null && from > to)
            throw new SalesReturnValidationException("From cannot be later than To.");
    }

    private static string? Normalize(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new SalesReturnValidationException($"The value exceeds {maximumLength} characters.");
        return normalized;
    }
}

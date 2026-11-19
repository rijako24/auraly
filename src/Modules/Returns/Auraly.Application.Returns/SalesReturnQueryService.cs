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
        RequireReturnAccess(user);
        ValidatePage(query.Page, query.PageSize);
        ValidateDates(query.From, query.To);
        return store.ListReturnableSalesAsync(
            user, query with { Search = Normalize(query.Search, 160) }, cancellationToken);
    }

    public Task<ReturnableSale?> GetReturnableSaleAsync(
        SalesReturnUserIdentity user, Guid documentId,
        CancellationToken cancellationToken = default)
    {
        RequireReturnAccess(user);
        if (documentId == Guid.Empty)
            throw new SalesReturnValidationException("Se requiere un documento válido.");
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
            throw new SalesReturnValidationException("El estado de la devolución no es válido.");
        if(query.OriginalDocumentId==Guid.Empty)
            throw new SalesReturnValidationException("Selecciona una venta válida para consultar sus devoluciones.");
        return store.ListReturnsAsync(
            user, query with { Search = Normalize(query.Search, 160) }, cancellationToken);
    }

    public async Task<SalesReturnDetail?> GetReturnAsync(
        SalesReturnUserIdentity user, Guid returnId,
        CancellationToken cancellationToken = default)
    {
        RequireRead(user);
        if (returnId == Guid.Empty)
            throw new SalesReturnValidationException("Se requiere una devolución válida.");
        var detail = await store.GetReturnAsync(user, returnId, cancellationToken);
        return detail is null ? null : detail with { Receipt = SalesReturnReceipt.Create(detail) };
    }

    private static void RequireRead(SalesReturnUserIdentity user)
    {
        if (!user.Permissions.Contains(SalesReturnPermissionCodes.Read))
            throw new SalesReturnForbiddenException(
                $"No tienes permiso para consultar devoluciones de venta.");
    }

    private static void RequireReturnAccess(SalesReturnUserIdentity user)
    {
        if (!user.Permissions.Contains(SalesReturnPermissionCodes.Read) &&
            !user.Permissions.Contains(SalesReturnPermissionCodes.Create))
            throw new SalesReturnForbiddenException(
                $"No tienes permiso para crear devoluciones de venta.");
    }

    private static void ValidatePage(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 100)
            throw new SalesReturnValidationException("La página y la cantidad de registros no son válidas.");
    }

    private static void ValidateDates(DateOnly? from, DateOnly? to)
    {
        if (from is not null && to is not null && from > to)
            throw new SalesReturnValidationException("La fecha inicial no puede ser posterior a la fecha final.");
    }

    private static string? Normalize(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new SalesReturnValidationException($"El valor admite máximo {maximumLength} caracteres.");
        return normalized;
    }
}

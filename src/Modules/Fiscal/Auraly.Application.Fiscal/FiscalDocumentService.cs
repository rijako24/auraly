using Auraly.Contracts.Fiscal;

namespace Auraly.Application.Fiscal;

public sealed record FiscalUserIdentity(
    Guid UserId,
    Guid BusinessId,
    IReadOnlySet<string> Permissions);

public sealed class FiscalForbiddenException(string message) : Exception(message);
public sealed class FiscalOperationException(string message) : Exception(message);

public interface IFiscalDocumentStore
{
    Task<FiscalDocumentView?> GetAsync(Guid businessId, Guid documentId, CancellationToken cancellationToken);
    Task<FiscalDocumentPage> PageAsync(Guid businessId, FiscalDocumentQuery query, CancellationToken cancellationToken);
    Task<FiscalDocumentView?> RetryAsync(Guid businessId, Guid documentId, DateTimeOffset requestedAt, CancellationToken cancellationToken);
}

public sealed class FiscalDocumentService(
    IFiscalDocumentStore store,
    TimeProvider timeProvider)
{
    public Task<FiscalDocumentView?> GetAsync(
        FiscalUserIdentity user,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        Demand(user, FiscalPermissionCodes.DocumentsRead);
        return store.GetAsync(user.BusinessId, documentId, cancellationToken);
    }

    public Task<FiscalDocumentPage> PageAsync(
        FiscalUserIdentity user,
        FiscalDocumentQuery query,
        CancellationToken cancellationToken = default)
    {
        Demand(user, FiscalPermissionCodes.DocumentsRead);
        if (query.Page < 1 || query.PageSize is < 1 or > 200)
            throw new FiscalOperationException("Page must be positive and pageSize must be between 1 and 200.");
        if (query.IssuedFrom > query.IssuedTo)
            throw new FiscalOperationException("IssuedFrom cannot be later than IssuedTo.");
        return store.PageAsync(user.BusinessId, query, cancellationToken);
    }

    public async Task<FiscalDocumentView?> RetryAsync(
        FiscalUserIdentity user,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        Demand(user, FiscalPermissionCodes.Retry);
        return await store.RetryAsync(user.BusinessId, documentId, timeProvider.GetUtcNow(), cancellationToken);
    }

    private static void Demand(FiscalUserIdentity user, string permission)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.UserId == Guid.Empty || user.BusinessId == Guid.Empty || !user.Permissions.Contains(permission))
            throw new FiscalForbiddenException($"Permission '{permission}' is required.");
    }
}
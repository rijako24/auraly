using Auraly.Contracts.Inventory;

namespace Auraly.Application.Inventory;

public interface IInventoryPhysicalCountStore
{
    Task<InventoryPhysicalCountDetail> CreateAsync(InventoryUserIdentity user, CreateInventoryPhysicalCountRequest request, CancellationToken token);
    Task<InventoryPhysicalCountPage> ListAsync(InventoryUserIdentity user, InventoryPhysicalCountQuery query, CancellationToken token);
    Task<InventoryPhysicalCountDetail?> GetAsync(InventoryUserIdentity user, Guid countId, CancellationToken token);
    Task<InventoryPhysicalCountDetail> StartAsync(InventoryUserIdentity user, Guid countId, CancellationToken token);
    Task<InventoryPhysicalCountDetail> SaveCaptureAsync(InventoryUserIdentity user, Guid countId, Guid listId, bool isFinalCount, SaveInventoryPhysicalCountCaptureRequest request, CancellationToken token);
    Task<InventoryPhysicalCountClosePreparation> PrepareCloseAsync(InventoryUserIdentity user, Guid countId, CancellationToken token);
    Task<InventoryPhysicalCountDetail> CompleteCloseAsync(InventoryUserIdentity user, Guid countId, InventoryOperationAcceptance acceptance, CancellationToken token);
}

public sealed class InventoryPhysicalCountService(
    IInventoryPhysicalCountStore store,
    InventoryOperationService operations,
    TimeProvider timeProvider)
{
    public Task<InventoryPhysicalCountDetail> CreateAsync(InventoryUserIdentity user, CreateInventoryPhysicalCountRequest request, CancellationToken token = default)
    {
        Require(user, request.BusinessId, InventoryPermissionCodes.ManagePhysicalCounts);
        if (request.InventoryPhysicalCountId == Guid.Empty || request.WarehouseId == Guid.Empty)
            throw new InventoryValidationException("Inventory physical count and warehouse are required.");
        if (request.ScopeType is not ("General" or "Partial"))
            throw new InventoryValidationException("Physical count scope must be General or Partial.");
        if (string.IsNullOrWhiteSpace(request.ReasonCode) || request.Lists.Count == 0 ||
            request.Lists.Any(list => list.ListId == Guid.Empty || string.IsNullOrWhiteSpace(list.Name) || list.ProductIds.Count == 0))
            throw new InventoryValidationException("A physical count requires a reason and non-empty lists.");
        var products = request.Lists.SelectMany(list => list.ProductIds).ToArray();
        if (products.Any(id => id == Guid.Empty) || products.Distinct().Count() != products.Length)
            throw new InventoryValidationException("A product can belong to only one list in a physical count.");
        var normalized = request with
        {
            ReasonCode = request.ReasonCode.Trim().ToUpperInvariant(),
            Notes = Normalize(request.Notes, 1000),
            Lists = request.Lists.Select(list => list with
            {
                Name = NormalizeRequired(list.Name, 120, "List name"),
                ProductIds = list.ProductIds.Distinct().ToArray()
            }).ToArray()
        };
        return store.CreateAsync(user, normalized, token);
    }

    public Task<InventoryPhysicalCountPage> ListAsync(InventoryUserIdentity user, InventoryPhysicalCountQuery query, CancellationToken token = default)
    {
        Require(user, query.BusinessId, InventoryPermissionCodes.Read);
        if (query.Page < 1 || query.PageSize is < 1 or > 200) throw new InventoryValidationException("Invalid pagination.");
        return store.ListAsync(user, query with { Search = Normalize(query.Search, 120), Status = Normalize(query.Status, 24) }, token);
    }

    public Task<InventoryPhysicalCountDetail?> GetAsync(InventoryUserIdentity user, Guid countId, CancellationToken token = default)
    {
        Require(user, user.BusinessId, InventoryPermissionCodes.Read);
        if (countId == Guid.Empty) throw new InventoryValidationException("Physical count is required.");
        return store.GetAsync(user, countId, token);
    }

    public Task<InventoryPhysicalCountDetail> StartAsync(InventoryUserIdentity user, Guid countId, CancellationToken token = default)
    {
        Require(user, user.BusinessId, InventoryPermissionCodes.ManagePhysicalCounts);
        return store.StartAsync(user, countId, token);
    }

    public Task<InventoryPhysicalCountDetail> SavePreCountAsync(InventoryUserIdentity user, Guid countId, Guid listId, SaveInventoryPhysicalCountCaptureRequest request, CancellationToken token = default) =>
        SaveAsync(user, countId, listId, false, request, token);

    public Task<InventoryPhysicalCountDetail> SaveCountAsync(InventoryUserIdentity user, Guid countId, Guid listId, SaveInventoryPhysicalCountCaptureRequest request, CancellationToken token = default) =>
        SaveAsync(user, countId, listId, true, request, token);

    public async Task<InventoryPhysicalCountDetail> CloseAsync(InventoryUserIdentity user, Guid countId, CloseInventoryPhysicalCountRequest request, CancellationToken token = default)
    {
        Require(user, request.BusinessId, InventoryPermissionCodes.ManagePhysicalCounts);
        Require(user, request.BusinessId, InventoryPermissionCodes.Count);
        var prepared = await store.PrepareCloseAsync(user, countId, token);
        var now = timeProvider.GetUtcNow();
        try
        {
            await operations.StartCountAsync(user, new(
                prepared.FinalInventoryOperationId, prepared.BusinessId, prepared.WarehouseId, now,
                prepared.ReasonCode, prepared.Notes,
                prepared.Lines.Select(line => new StartStockCountLineRequest(line.ProductId, line.PreCountQuantity)).ToArray()), token);
        }
        catch (InventoryConflictException)
        {
            // A previous close attempt may have persisted the deterministic draft before losing its response.
        }
        var acceptance = await operations.ConfirmCountAsync(user, prepared.FinalInventoryOperationId,
            $"physical-count:{countId:D}:close", new(prepared.BusinessId,
                prepared.Lines.Select((line, index) => new StockCountLineRequest(index + 1, line.ProductId, line.AdjustedCountQuantity)).ToArray()), token);
        return await store.CompleteCloseAsync(user, countId, acceptance, token);
    }

    private Task<InventoryPhysicalCountDetail> SaveAsync(InventoryUserIdentity user, Guid countId, Guid listId, bool final, SaveInventoryPhysicalCountCaptureRequest request, CancellationToken token)
    {
        Require(user, request.BusinessId, InventoryPermissionCodes.CapturePhysicalCounts);
        if (request.Lines.Count == 0 || request.Lines.Any(line => line.ProductId == Guid.Empty || line.Quantity < 0) ||
            request.Lines.Select(line => line.ProductId).Distinct().Count() != request.Lines.Count)
            throw new InventoryValidationException("Capture lines must be distinct and non-negative.");
        return store.SaveCaptureAsync(user, countId, listId, final, request, token);
    }

    private static void Require(InventoryUserIdentity user, Guid businessId, string permission)
    {
        if (user.BusinessId != businessId) throw new InventoryForbiddenException("The physical count belongs to another business.");
        if (!user.Permissions.Contains(permission)) throw new InventoryForbiddenException($"Permission '{permission}' is required.");
    }

    private static string? Normalize(string? value, int maximum) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= maximum ? value.Trim() : throw new InventoryValidationException($"Value cannot exceed {maximum} characters.");

    private static string NormalizeRequired(string value, int maximum, string label)
    {
        var normalized = value.Trim();
        return normalized.Length <= maximum
            ? normalized
            : throw new InventoryValidationException($"{label} cannot exceed {maximum} characters.");
    }
}

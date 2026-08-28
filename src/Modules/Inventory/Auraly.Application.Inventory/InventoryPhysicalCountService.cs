using Auraly.Contracts.Inventory;

namespace Auraly.Application.Inventory;

public interface IInventoryPhysicalCountStore
{
    Task<InventoryPhysicalCountDetail> CreateAsync(InventoryUserIdentity user, CreateInventoryPhysicalCountRequest request, CancellationToken token);
    Task<InventoryPhysicalCountPage> ListAsync(InventoryUserIdentity user, InventoryPhysicalCountQuery query, CancellationToken token);
    Task<InventoryPhysicalCountDraftPage> ListDraftsAsync(InventoryUserIdentity user, InventoryPhysicalCountDraftQuery query, CancellationToken token);
    Task<InventoryPhysicalCountDetail?> GetAsync(InventoryUserIdentity user, Guid countId, CancellationToken token);
    Task<InventoryPhysicalCountDetail> CreateDraftAsync(InventoryUserIdentity user, Guid countId, CreateInventoryPhysicalCountDraftRequest request, CancellationToken token);
    Task<InventoryPhysicalCountDetail> SaveDraftAsync(InventoryUserIdentity user, Guid countId, Guid draftId, SaveInventoryPhysicalCountDraftRequest request, CancellationToken token);
    Task<InventoryReconciliationDetail> PrepareReconciliationAsync(InventoryUserIdentity user, Guid countId, PrepareInventoryReconciliationRequest request, CancellationToken token);
    Task<InventoryReconciliationDetail?> GetReconciliationAsync(InventoryUserIdentity user, Guid countId, CancellationToken token);
    Task<InventoryPhysicalCountDetail> SaveReconciliationDraftAsync(InventoryUserIdentity user, Guid countId, Guid reconciliationId, SaveInventoryReconciliationDraftRequest request, CancellationToken token);
    Task<InventoryPhysicalCountClosePreparation> PrepareApplyAsync(InventoryUserIdentity user, Guid countId, Guid reconciliationId, string section, CancellationToken token);
    Task<InventoryPhysicalCountDetail> RecordApplyAcceptanceAsync(InventoryUserIdentity user, Guid countId, Guid reconciliationId, string section, InventoryOperationAcceptance acceptance, CancellationToken token);
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
        if (string.IsNullOrWhiteSpace(request.ReasonCode) || string.IsNullOrWhiteSpace(request.InitialDraftName))
            throw new InventoryValidationException("A reason and an initial draft name are required.");
        if (request.ScopeType == "Partial" && request.ProductIds.Count == 0)
            throw new InventoryValidationException("A partial count requires at least one product.");
        ValidateProductIds(request.ProductIds);
        return store.CreateAsync(user, request with
        {
            ReasonCode = request.ReasonCode.Trim().ToUpperInvariant(),
            Notes = Normalize(request.Notes, 1000),
            InitialDraftName = NormalizeRequired(request.InitialDraftName, 120, "Draft name"),
            ProductIds = request.ProductIds.Distinct().ToArray()
        }, token);
    }

    public Task<InventoryPhysicalCountPage> ListAsync(InventoryUserIdentity user, InventoryPhysicalCountQuery query, CancellationToken token = default)
    {
        Require(user, query.BusinessId, InventoryPermissionCodes.Read);
        if (query.Page < 1 || query.PageSize is < 1 or > 200) throw new InventoryValidationException("Invalid pagination.");
        return store.ListAsync(user, query with { Search = Normalize(query.Search, 120), Status = Normalize(query.Status, 24) }, token);
    }

    public Task<InventoryPhysicalCountDraftPage> ListDraftsAsync(InventoryUserIdentity user, InventoryPhysicalCountDraftQuery query, CancellationToken token = default)
    {
        Require(user, query.BusinessId, InventoryPermissionCodes.Read);
        if (query.Page < 1 || query.PageSize is < 1 or > 200)
            throw new InventoryValidationException("Invalid pagination.");
        if (query.From is not null && query.To is not null && query.From >= query.To)
            throw new InventoryValidationException("The draft date range is invalid.");
        var status = Normalize(query.Status, 24);
        if (status is not null && status is not ("Ready" or "InProgress"))
            throw new InventoryValidationException("Draft status must be Ready or InProgress.");
        return store.ListDraftsAsync(user, query with { Search = Normalize(query.Search, 120), Status = status }, token);
    }

    public Task<InventoryPhysicalCountDetail?> GetAsync(InventoryUserIdentity user, Guid countId, CancellationToken token = default)
    {
        Require(user, user.BusinessId, InventoryPermissionCodes.Read);
        ValidateId(countId, "Physical count");
        return store.GetAsync(user, countId, token);
    }

    public Task<InventoryPhysicalCountDetail> CreateDraftAsync(InventoryUserIdentity user, Guid countId, CreateInventoryPhysicalCountDraftRequest request, CancellationToken token = default)
    {
        Require(user, request.BusinessId, InventoryPermissionCodes.CapturePhysicalCounts);
        ValidateId(countId, "Physical count");
        ValidateId(request.DraftId, "Draft");
        if (request.ProductIds.Count == 0) throw new InventoryValidationException("A draft requires at least one product.");
        ValidateProductIds(request.ProductIds);
        return store.CreateDraftAsync(user, countId, request with
        {
            Name = NormalizeRequired(request.Name, 120, "Draft name"),
            ProductIds = request.ProductIds.Distinct().ToArray()
        }, token);
    }

    public Task<InventoryPhysicalCountDetail> SaveDraftAsync(InventoryUserIdentity user, Guid countId, Guid draftId, SaveInventoryPhysicalCountDraftRequest request, CancellationToken token = default)
    {
        Require(user, request.BusinessId, InventoryPermissionCodes.CapturePhysicalCounts);
        ValidateId(countId, "Physical count");
        ValidateId(draftId, "Draft");
        if (request.Version < 1 || request.Lines.Count == 0 || request.Lines.Select(line => line.ProductId).Distinct().Count() != request.Lines.Count)
            throw new InventoryValidationException("Draft lines and a valid version are required.");
        if (request.CaptureStage is not ("Count" or "Recount"))
            throw new InventoryValidationException("CaptureStage must be Count or Recount.");
        foreach (var line in request.Lines)
        {
            ValidateId(line.ProductId, "Product");
            if (line.InitialQuantity < 0 || line.VerificationQuantity < 0)
                throw new InventoryValidationException("Counted quantities cannot be negative.");
            if (line.VerificationQuantity is not null && line.InitialQuantity is null)
                throw new InventoryValidationException("Verification requires an initial count.");
        }
        if (request.ReadyForReconciliation && request.Lines.All(line => line.InitialQuantity is null))
            throw new InventoryValidationException("A ready draft must contain at least one initial count.");
        if (request.CaptureStage == "Recount" && request.ReadyForReconciliation &&
            request.Lines.Any(line => line.InitialQuantity is not null && line.VerificationQuantity is null))
            throw new InventoryValidationException("Every counted product must be recounted before the draft is ready for reconciliation.");
        return store.SaveDraftAsync(user, countId, draftId, request with
        {
            Name = NormalizeRequired(request.Name, 120, "Draft name"),
            Lines = request.Lines.Select(line => line with { PendingReason = Normalize(line.PendingReason, 250) }).ToArray()
        }, token);
    }

    public Task<InventoryReconciliationDetail> PrepareReconciliationAsync(InventoryUserIdentity user, Guid countId, PrepareInventoryReconciliationRequest request, CancellationToken token = default)
    {
        Require(user, request.BusinessId, InventoryPermissionCodes.ManagePhysicalCounts);
        ValidateId(countId, "Physical count");
        if (request.Drafts.Count == 0 || request.Drafts.Any(draft => draft.DraftId == Guid.Empty || draft.Version < 1) ||
            request.Drafts.Select(draft => draft.DraftId).Distinct().Count() != request.Drafts.Count)
            throw new InventoryValidationException("Select at least one distinct ready draft.");
        return store.PrepareReconciliationAsync(user, countId, request, token);
    }

    public Task<InventoryReconciliationDetail?> GetReconciliationAsync(InventoryUserIdentity user, Guid countId, CancellationToken token = default)
    {
        Require(user, user.BusinessId, InventoryPermissionCodes.ManagePhysicalCounts);
        ValidateId(countId, "Physical count");
        return store.GetReconciliationAsync(user, countId, token);
    }

    public Task<InventoryPhysicalCountDetail> SaveReconciliationDraftAsync(InventoryUserIdentity user, Guid countId, Guid reconciliationId, SaveInventoryReconciliationDraftRequest request, CancellationToken token = default)
    {
        Require(user, request.BusinessId, InventoryPermissionCodes.ManagePhysicalCounts);
        Require(user, request.BusinessId, InventoryPermissionCodes.CapturePhysicalCounts);
        ValidateId(countId, "Physical count");
        ValidateId(reconciliationId, "Reconciliation");
        ValidateSection(request.Section);
        ValidateId(request.DraftId, "Draft");
        return store.SaveReconciliationDraftAsync(user, countId, reconciliationId, request with { Name = NormalizeRequired(request.Name, 120, "Draft name") }, token);
    }

    public async Task<InventoryPhysicalCountDetail> ApplyAsync(InventoryUserIdentity user, Guid countId, Guid reconciliationId, ApplyInventoryReconciliationRequest request, CancellationToken token = default)
    {
        Require(user, request.BusinessId, InventoryPermissionCodes.ManagePhysicalCounts);
        Require(user, request.BusinessId, InventoryPermissionCodes.Count);
        ValidateId(countId, "Physical count");
        ValidateId(reconciliationId, "Reconciliation");
        ValidateSection(request.Section);
        var prepared = await store.PrepareApplyAsync(user, countId, reconciliationId, request.Section, token);
        var now = timeProvider.GetUtcNow();
        try
        {
            await operations.StartCountAsync(user, new(
                prepared.FinalInventoryOperationId, prepared.BusinessId, prepared.WarehouseId, now,
                prepared.ReasonCode, prepared.Notes,
                prepared.Lines.Select(line => new StartStockCountLineRequest(line.ProductId, line.InitialQuantity)).ToArray()), token);
        }
        catch (InventoryConflictException)
        {
            // A previous close attempt may have persisted the deterministic draft before losing its response.
        }
        var acceptance = await operations.ConfirmCountAsync(user, prepared.FinalInventoryOperationId,
            $"physical-count:{countId:D}:{reconciliationId:D}:{prepared.Section.ToLowerInvariant()}", new(prepared.BusinessId,
                prepared.Lines.Select((line, index) => new StockCountLineRequest(index + 1, line.ProductId, line.AdjustedCountQuantity)).ToArray()), token);
        return await store.RecordApplyAcceptanceAsync(user, countId, reconciliationId, prepared.Section, acceptance, token);
    }

    private static void ValidateSection(string section)
    {
        if (section is not ("Counted" or "Uncounted"))
            throw new InventoryValidationException("Reconciliation section must be Counted or Uncounted.");
    }

    private static void ValidateProductIds(IEnumerable<Guid> ids)
    {
        if (ids.Any(id => id == Guid.Empty)) throw new InventoryValidationException("Product identifiers are invalid.");
    }

    private static void ValidateId(Guid id, string label)
    {
        if (id == Guid.Empty) throw new InventoryValidationException($"{label} is required.");
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
        if (normalized.Length == 0) throw new InventoryValidationException($"{label} is required.");
        return normalized.Length <= maximum ? normalized : throw new InventoryValidationException($"{label} cannot exceed {maximum} characters.");
    }
}

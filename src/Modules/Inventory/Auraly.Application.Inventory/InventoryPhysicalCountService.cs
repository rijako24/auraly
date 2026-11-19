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
    Task DiscardDraftAsync(InventoryUserIdentity user, Guid countId, Guid draftId, DiscardInventoryPhysicalCountDraftRequest request, CancellationToken token);
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
            throw new InventoryValidationException("Se requiere un inventario físico y una bodega válidos.");
        if (request.ScopeType is not ("General" or "Partial"))
            throw new InventoryValidationException("El alcance del inventario debe ser general o parcial.");
        if (string.IsNullOrWhiteSpace(request.ReasonCode) || string.IsNullOrWhiteSpace(request.InitialDraftName))
            throw new InventoryValidationException("Selecciona un motivo y escribe el nombre del borrador inicial.");
        if (request.ScopeType == "Partial" && request.ProductIds.Count == 0)
            throw new InventoryValidationException("Un inventario parcial requiere al menos un producto.");
        ValidateProductIds(request.ProductIds);
        return store.CreateAsync(user, request with
        {
            ReasonCode = request.ReasonCode.Trim().ToUpperInvariant(),
            Notes = Normalize(request.Notes, 1000),
            InitialDraftName = NormalizeRequired(request.InitialDraftName, 120, "nombre del borrador"),
            ProductIds = request.ProductIds.Distinct().ToArray()
        }, token);
    }

    public Task<InventoryPhysicalCountPage> ListAsync(InventoryUserIdentity user, InventoryPhysicalCountQuery query, CancellationToken token = default)
    {
        Require(user, query.BusinessId, InventoryPermissionCodes.Read);
        InventoryOperationAccess.RequireRead(user, InventoryDocumentTypes.StockCount);
        if (query.Page < 1 || query.PageSize is < 1 or > 200) throw new InventoryValidationException("La página y la cantidad de registros no son válidas.");
        return store.ListAsync(user, query with { Search = Normalize(query.Search, 120), Status = Normalize(query.Status, 24) }, token);
    }

    public Task<InventoryPhysicalCountDraftPage> ListDraftsAsync(InventoryUserIdentity user, InventoryPhysicalCountDraftQuery query, CancellationToken token = default)
    {
        Require(user, query.BusinessId, InventoryPermissionCodes.Read);
        InventoryOperationAccess.RequireRead(user, InventoryDocumentTypes.StockCount);
        if (query.Page < 1 || query.PageSize is < 1 or > 200)
            throw new InventoryValidationException("La página y la cantidad de registros no son válidas.");
        if (query.From is not null && query.To is not null && query.From >= query.To)
            throw new InventoryValidationException("El rango de fechas de los borradores no es válido.");
        var status = Normalize(query.Status, 24);
        if (status is not null && status is not ("Ready" or "InProgress"))
            throw new InventoryValidationException("El estado del borrador debe ser listo o en proceso.");
        return store.ListDraftsAsync(user, query with { Search = Normalize(query.Search, 120), Status = status }, token);
    }

    public Task<InventoryPhysicalCountDetail?> GetAsync(InventoryUserIdentity user, Guid countId, CancellationToken token = default)
    {
        Require(user, user.BusinessId, InventoryPermissionCodes.Read);
        InventoryOperationAccess.RequireRead(user, InventoryDocumentTypes.StockCount);
        ValidateId(countId, "inventario físico");
        return store.GetAsync(user, countId, token);
    }

    public Task<InventoryPhysicalCountDetail> CreateDraftAsync(InventoryUserIdentity user, Guid countId, CreateInventoryPhysicalCountDraftRequest request, CancellationToken token = default)
    {
        Require(user, request.BusinessId, InventoryPermissionCodes.CapturePhysicalCounts);
        ValidateId(countId, "inventario físico");
        ValidateId(request.DraftId, "borrador");
        if (request.ProductIds.Count == 0) throw new InventoryValidationException("Un borrador requiere al menos un producto.");
        ValidateProductIds(request.ProductIds);
        return store.CreateDraftAsync(user, countId, request with
        {
            Name = NormalizeRequired(request.Name, 120, "nombre del borrador"),
            ProductIds = request.ProductIds.Distinct().ToArray()
        }, token);
    }

    public Task<InventoryPhysicalCountDetail> SaveDraftAsync(InventoryUserIdentity user, Guid countId, Guid draftId, SaveInventoryPhysicalCountDraftRequest request, CancellationToken token = default)
    {
        Require(user, request.BusinessId, InventoryPermissionCodes.CapturePhysicalCounts);
        ValidateId(countId, "inventario físico");
        ValidateId(draftId, "borrador");
        if (request.Version < 1 || request.Lines.Count == 0 || request.Lines.Select(line => line.ProductId).Distinct().Count() != request.Lines.Count)
            throw new InventoryValidationException("Se requieren líneas del borrador y una versión válida.");
        if (request.CaptureStage is not ("Count" or "Recount"))
            throw new InventoryValidationException("Selecciona la etapa de conteo o reconteo.");
        foreach (var line in request.Lines)
        {
            ValidateId(line.ProductId, "producto");
            if (line.InitialQuantity < 0 || line.VerificationQuantity < 0)
                throw new InventoryValidationException("Las cantidades contadas no pueden ser negativas.");
            if (line.VerificationQuantity is not null && line.InitialQuantity is null)
                throw new InventoryValidationException("Verification requires an initial count.");
        }
        if (request.ReadyForReconciliation && request.Lines.All(line => line.InitialQuantity is null))
            throw new InventoryValidationException("Un borrador listo requiere al menos un conteo inicial.");
        if (request.CaptureStage == "Recount" && request.ReadyForReconciliation &&
            request.Lines.Any(line => line.InitialQuantity is not null && line.VerificationQuantity is null))
            throw new InventoryValidationException("Todos los productos contados deben recontarse antes de dejar el borrador listo para conciliar.");
        return store.SaveDraftAsync(user, countId, draftId, request with
        {
            Name = NormalizeRequired(request.Name, 120, "nombre del borrador"),
            Lines = request.Lines.Select(line => line with { PendingReason = Normalize(line.PendingReason, 250) }).ToArray()
        }, token);
    }

    public Task DiscardDraftAsync(InventoryUserIdentity user, Guid countId, Guid draftId, DiscardInventoryPhysicalCountDraftRequest request, CancellationToken token = default)
    {
        Require(user, request.BusinessId, InventoryPermissionCodes.CapturePhysicalCounts);
        ValidateId(countId, "inventario físico");
        ValidateId(draftId, "borrador");
        if (request.Version < 1)
            throw new InventoryValidationException("Se requiere una versión válida del borrador.");
        return store.DiscardDraftAsync(user, countId, draftId, request, token);
    }

    public Task<InventoryReconciliationDetail> PrepareReconciliationAsync(InventoryUserIdentity user, Guid countId, PrepareInventoryReconciliationRequest request, CancellationToken token = default)
    {
        Require(user, request.BusinessId, InventoryPermissionCodes.ManagePhysicalCounts);
        ValidateId(countId, "inventario físico");
        if (request.Drafts.Count == 0 || request.Drafts.Any(draft => draft.DraftId == Guid.Empty || draft.Version < 1) ||
            request.Drafts.Select(draft => draft.DraftId).Distinct().Count() != request.Drafts.Count)
            throw new InventoryValidationException("Selecciona al menos un borrador listo, sin repetirlo.");
        return store.PrepareReconciliationAsync(user, countId, request, token);
    }

    public Task<InventoryReconciliationDetail?> GetReconciliationAsync(InventoryUserIdentity user, Guid countId, CancellationToken token = default)
    {
        Require(user, user.BusinessId, InventoryPermissionCodes.ManagePhysicalCounts);
        ValidateId(countId, "inventario físico");
        return store.GetReconciliationAsync(user, countId, token);
    }

    public Task<InventoryPhysicalCountDetail> SaveReconciliationDraftAsync(InventoryUserIdentity user, Guid countId, Guid reconciliationId, SaveInventoryReconciliationDraftRequest request, CancellationToken token = default)
    {
        Require(user, request.BusinessId, InventoryPermissionCodes.ManagePhysicalCounts);
        Require(user, request.BusinessId, InventoryPermissionCodes.CapturePhysicalCounts);
        ValidateId(countId, "inventario físico");
        ValidateId(reconciliationId, "conciliación");
        ValidateSection(request.Section);
        ValidateId(request.DraftId, "borrador");
        return store.SaveReconciliationDraftAsync(user, countId, reconciliationId, request with { Name = NormalizeRequired(request.Name, 120, "nombre del borrador") }, token);
    }

    public async Task<InventoryPhysicalCountDetail> ApplyAsync(InventoryUserIdentity user, Guid countId, Guid reconciliationId, ApplyInventoryReconciliationRequest request, CancellationToken token = default)
    {
        Require(user, request.BusinessId, InventoryPermissionCodes.ManagePhysicalCounts);
        Require(user, request.BusinessId, InventoryPermissionCodes.Count);
        ValidateId(countId, "inventario físico");
        ValidateId(reconciliationId, "conciliación");
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
            throw new InventoryValidationException("Selecciona la sección de productos contados o sin contar de la conciliación.");
    }

    private static void ValidateProductIds(IEnumerable<Guid> ids)
    {
        if (ids.Any(id => id == Guid.Empty)) throw new InventoryValidationException("Los productos seleccionados no son válidos.");
    }

    private static void ValidateId(Guid id, string label)
    {
        if (id == Guid.Empty) throw new InventoryValidationException($"El campo {label} es obligatorio.");
    }

    private static void Require(InventoryUserIdentity user, Guid businessId, string permission)
    {
        if (user.BusinessId != businessId) throw new InventoryForbiddenException("El inventario físico pertenece a otra sede.");
        if (!user.Permissions.Contains(permission)) throw new InventoryForbiddenException("No tienes permiso para realizar esta operación de inventario.");
    }

    private static string? Normalize(string? value, int maximum) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= maximum ? value.Trim() : throw new InventoryValidationException($"El valor admite máximo {maximum} caracteres.");

    private static string NormalizeRequired(string value, int maximum, string label)
    {
        var normalized = value.Trim();
        if (normalized.Length == 0) throw new InventoryValidationException($"El campo {label} es obligatorio.");
        return normalized.Length <= maximum ? normalized : throw new InventoryValidationException($"El campo {label} admite máximo {maximum} caracteres.");
    }
}
